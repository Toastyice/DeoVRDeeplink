using DeoVRDeeplink.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Configuration;

namespace DeoVRDeeplink.ScheduledTasks;

public class TimelineCleanupTask : IScheduledTask
{
    private readonly ILogger<TimelineCleanupTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILocalizationManager _localization;

    public TimelineCleanupTask(
        ILibraryManager libraryManager,
        ILogger<TimelineCleanupTask> logger,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILocalizationManager localization
        )
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _localization = localization;
    }

    public string Name => "Timeline Images Cleanup";
    public string Description => "Removes orphaned timeline preview images that are no longer needed";
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");
    public string Key => "TimelineCleanup";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromDays(7).Ticks // Weekly cleanup by default
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting timeline images cleanup task");

        try
        {
            var timelineImagesPath = Path.Combine(_appPaths.DataPath, "deovr-timeline");
            
            if (!_fileSystem.DirectoryExists(timelineImagesPath))
            {
                _logger.LogInformation("Timeline images directory does not exist: {Path}", timelineImagesPath);
                progress.Report(100);
                return;
            }
            
            var configLibraries = DeoVrDeeplinkPlugin.Instance?.Configuration?.Libraries;
            if (configLibraries == null)
            {
                _logger.LogWarning("Plugin configuration not available, skipping cleanup");
                progress.Report(100);
                return;
            }
            
            // Get all valid video items that should have timeline images
            var validVideoIds = await GetValidVideoIdsAsync(configLibraries, cancellationToken);
            _logger.LogInformation("Found {Count} valid videos that should have timeline images", validVideoIds.Count);

            // Get all timeline image files (.jpg files only)
            var allImageFiles = _fileSystem.GetFiles(timelineImagesPath, false)
                .Where(file => string.Equals(Path.GetExtension(file.FullName), ".jpg", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            _logger.LogInformation("Found {Count} timeline image files to examine", allImageFiles.Length);

            if (allImageFiles.Length == 0)
            {
                _logger.LogInformation("No timeline image files found for cleanup");
                progress.Report(100);
                return;
            }

            // Analyze files to categorize them
            var (orphanedFiles, invalidFiles) = await AnalyzeImageFilesAsync(allImageFiles, validVideoIds, progress, cancellationToken);

            _logger.LogInformation("Found {OrphanedCount} orphaned and {InvalidCount} invalid timeline image files out of {TotalCount} total files", 
                orphanedFiles.Count, invalidFiles.Count, allImageFiles.Length);

            // Delete orphaned and invalid files
            var allFilesToDelete = orphanedFiles.Concat(invalidFiles).ToList();
            var (deletedCount, deletedSize, failedCount) = await DeleteFilesAsync(allFilesToDelete, progress, cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            progress.Report(100);

            _logger.LogInformation(
                "Timeline images cleanup completed in {Duration:mm\\:ss}. Deleted {DeletedCount} files ({FailedCount} failed), freed {FreedSpace:F2} MB", 
                duration,
                deletedCount,
                failedCount,
                deletedSize / (1024.0 * 1024.0));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Timeline cleanup was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during timeline images cleanup");
            throw;
        }
    }

    private Task<HashSet<Guid>> GetValidVideoIdsAsync(List<LibraryConfiguration> configLibraries, CancellationToken cancellationToken)
    {
        var validVideoIds = new HashSet<Guid>();
        var librariesToProcess = GetAllLibraries()
            .Where(library =>
            {
                var libraryConfig = configLibraries.FirstOrDefault(config => config.Id == library.Id);
                return libraryConfig is { Enabled: true, TimelineImages: true };
            })
            .ToArray();

        foreach (var library in librariesToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var libraryVideos = GetVideosFromLibrary(library);
            foreach (var video in libraryVideos)
            {
                validVideoIds.Add(video.Id);
            }
        }

        return Task.FromResult(validVideoIds);
    }

    private Task<(List<FileSystemMetadata> orphaned, List<FileSystemMetadata> invalid)> 
        AnalyzeImageFilesAsync(FileSystemMetadata[] allImageFiles, HashSet<Guid> validVideoIds, 
                             IProgress<double> progress, CancellationToken cancellationToken)
    {
        var orphanedFiles = new List<FileSystemMetadata>();
        var invalidFiles = new List<FileSystemMetadata>();
        var processedFiles = 0;

        foreach (var imageFile in allImageFiles)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var videoId = ExtractVideoIdFromImagePath(imageFile.FullName);
                if (videoId == null)
                {
                    invalidFiles.Add(imageFile);
                    _logger.LogDebug("Found invalid timeline image filename: {FilePath}", imageFile.FullName);
                }
                else if (!validVideoIds.Contains(videoId.Value))
                {
                    orphanedFiles.Add(imageFile);
                    _logger.LogDebug("Found orphaned timeline image: {FilePath}", imageFile.FullName);
                }

                processedFiles++;
                var scanProgress = (double)processedFiles / allImageFiles.Length * 80; // Use 80% for scanning
                progress.Report(scanProgress);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing timeline image file: {FilePath}", imageFile.FullName);
                invalidFiles.Add(imageFile); // Treat as invalid if we can't process it
            }
        }

        return Task.FromResult((orphanedFiles, invalidFiles));
    }

    private Task<(int deletedCount, long deletedSize, int failedCount)> 
        DeleteFilesAsync(List<FileSystemMetadata> filesToDelete, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var deletedCount = 0;
        var deletedSize = 0L;
        var failedCount = 0;
        var processedCount = 0;

        foreach (var fileToDelete in filesToDelete)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileSize = fileToDelete.Length;
                _fileSystem.DeleteFile(fileToDelete.FullName);
                
                // Only count size if deletion succeeded
                deletedSize += fileSize;
                deletedCount++;

                _logger.LogDebug("Deleted timeline image: {FilePath} ({Size} bytes)", 
                    fileToDelete.FullName, fileSize);
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex, "Error deleting timeline image: {FilePath}", fileToDelete.FullName);
            }

            processedCount++;
            var deleteProgress = 80 + ((double)processedCount / filesToDelete.Count * 20);
            progress.Report(deleteProgress);
        }

        if (failedCount > 0)
        {
            _logger.LogWarning("Failed to delete {ErrorCount} files during cleanup", failedCount);
        }

        return Task.FromResult((deletedCount, deletedSize, failedCount));
    }

    private Guid? ExtractVideoIdFromImagePath(string imagePath)
    {
        try
        {
            // Images are named {itemId}.jpg directly in the deovr-timeline folder
            var fileName = Path.GetFileNameWithoutExtension(imagePath);
            
            if (Guid.TryParse(fileName, out var videoId))
            {
                return videoId;
            }
            
            _logger.LogDebug("Invalid GUID format in filename: {FileName}", fileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not extract video ID from image path: {ImagePath}", imagePath);
            return null;
        }
    }

    private IEnumerable<Folder> GetAllLibraries()
    {
        return _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>();
    }

    private IEnumerable<Video> GetVideosFromLibrary(Folder library)
    {
        var query = new InternalItemsQuery
        {
            ParentId = library.Id,
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsFolder = false
        };

        return _libraryManager.GetItemList(query).OfType<Video>();
    }
}
