using System.Globalization;
using DeoVRDeeplink.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace DeoVRDeeplink.TimelinePreview;

public class VideoProcessor
{
    private readonly ILogger<VideoProcessor> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PluginConfiguration _config;

    public VideoProcessor(
        ILoggerFactory loggerFactory,
        ILogger<VideoProcessor> logger,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        EncodingHelper encodingHelper)
    {
        _loggerFactory = loggerFactory;
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _config = DeoVrDeeplinkPlugin.Instance!.Configuration;
    }

    public async Task Run(BaseItem item, CancellationToken cancellationToken)
    {
        if (!EnableForItem(item)) return;
        
        var mediaSources = ((IHasMediaSources)item).GetMediaSources(false).ToList();
        
        foreach (var mediaSource in mediaSources.Where(mediaSource => item.Id.Equals(Guid.Parse(mediaSource.Id))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Run(item, mediaSource, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Run(BaseItem item, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing video timeline for: {ItemName}", item.Name);
            
            var outputPath = GetTimelineOutputPath(item);
            var ffmpegArgs = GetFFmpegArgumentsForTimeline(item, mediaSource, outputPath);
            
            // Check if timeline already exists and is up to date
            if (IsTimelineUpToDate(outputPath, mediaSource.Path))
            {
                _logger.LogDebug("Timeline image already exists and is up to date for {ItemName}", item.Name);
                return;
            }
            
            await RunFFmpegCommand(ffmpegArgs, cancellationToken);
            
            _logger.LogInformation("Timeline generation completed for: {ItemName}", item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing timeline for: {ItemName}", item.Name);
            throw;
        }
    }
    
    private static string GetFFFpsForFilter(BaseItem item)
    {
        if (item is not Video { RunTimeTicks: not null } videoItem) return "0.0";
        return "fps=1/" + ((double)videoItem.RunTimeTicks.Value / TimeSpan.TicksPerSecond / 252.0)
            .ToString(CultureInfo.InvariantCulture);
    }
    
    private static string GetFFCropFilter(BaseItem item)
    {
        if (item is not Video videoItem) return ",crop=iw/2:ih:0:0"; //TODO This should crash
        return videoItem.Video3DFormat switch
        {
            Video3DFormat.FullSideBySide => ",crop=iw/2:ih:0:0",        // Crop left half
            Video3DFormat.FullTopAndBottom => ",crop=iw:ih/2:0:0",      // Crop top half
            Video3DFormat.HalfSideBySide => ",crop=iw/2:ih:0:0",        // Crop left half
            Video3DFormat.HalfTopAndBottom => ",crop=iw:ih/2:0:0",      // Crop top half
            _ => ",crop=iw/2:ih:0:0"                                    //TODO Default case should assume flat
        };
    }

    private string GetFFDistortionFilter(BaseItem item)
    {
        if (!_config.TimelineRemoveDistortion) return "";
        if (item is not Video videoItem) return ""; //TODO This should crash
        return videoItem.Video3DFormat switch
        {
            Video3DFormat.FullSideBySide =>   ",v360=e:flat:ih_fov=190:iv_fov=90", //Untested
            Video3DFormat.FullTopAndBottom => ",v360=e:flat:ih_fov=190:iv_fov=90",     
            Video3DFormat.HalfSideBySide =>   ",v360=hequirect:flat:h_fov=120:v_fov=110",
            Video3DFormat.HalfTopAndBottom => ",v360=hequirect:flat:h_fov=120:v_fov=90", //Untested
            _ => "" //TODO Default case should assume flat and not apply this filter
        };
    }
    
    private List<string> GetFFmpegArgumentsForTimeline(BaseItem item, MediaSourceInfo mediaSource, string outputPath)
    {
        return
        [
            "-i", $"\"{mediaSource.Path}\"",
            "-vf",
            $"\"{GetFFFpsForFilter(item)}{GetFFCropFilter(item)}{GetFFDistortionFilter(item)},scale=341:195,tile=12x21:margin=0:padding=0\"",
            "-q:v", "1",
            "-y",
            $"\"{outputPath}\""
        ];
    }

    private string GetTimelineOutputPath(BaseItem item)
    {
        var fileName = $"{item.Id}.jpg";
        var timelineCachePath = Path.Combine(_appPaths.ConfigurationDirectoryPath , "deovr-timeline");
        
        Directory.CreateDirectory(timelineCachePath);
        
        return Path.Combine(timelineCachePath, fileName);
    }

    private bool IsTimelineUpToDate(string outputPath, string videoPath)
    {
        if (!File.Exists(outputPath)) return false;
        
        var outputInfo = new FileInfo(outputPath);
        var videoInfo = new FileInfo(videoPath);
        
        return outputInfo.LastWriteTime > videoInfo.LastWriteTime;
    }

    private async Task RunFFmpegCommand(List<string> args, CancellationToken cancellationToken)
    {
        var argString = string.Join(" ", args);
    
        // Log the complete FFmpeg command
        var fullCommand = $"{_mediaEncoder.EncoderPath} {argString}";
        _logger.LogInformation("Executing FFmpeg command: {FullCommand}", fullCommand);
    
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                Arguments = argString,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = processStartInfo;
        
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
        
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
        
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                _logger.LogError("FFmpeg failed with exit code {ProcessExitCode}: {Error}", process.ExitCode, error);
                throw new Exception($"FFmpeg process failed with exit code {process.ExitCode}");
            }

            _logger.LogInformation("FFmpeg command completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg command execution failed");
            throw;
        }
    }
    
    private static bool EnableForItem(BaseItem item)
    {
        // Check if item is a video
        if (item is not Video) return false;
        
        // Check if item has a valid path
        if (string.IsNullOrEmpty(item.Path)) return false;
        
        // Check if file exists
        if (!File.Exists(item.Path)) return false;
        
        // Add any other checks here if needed
        return true;
    }
}
