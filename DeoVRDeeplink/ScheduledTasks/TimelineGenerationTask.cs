using DeoVRDeeplink.TimelinePreview;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Configuration;

namespace DeoVRDeeplink.ScheduledTasks;

    public class TimelineGenerationTask : IScheduledTask
    {
        private readonly ILogger<TimelineGenerationTask> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILocalizationManager _localization;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly EncodingHelper _encodingHelper;
        public TimelineGenerationTask(
            ILibraryManager libraryManager,
            ILogger<TimelineGenerationTask> logger,
            ILoggerFactory loggerFactory,
            IFileSystem fileSystem,
            IApplicationPaths appPaths,
            ILibraryMonitor libraryMonitor,
            ILocalizationManager localization,
            IMediaEncoder mediaEncoder,
            IServerConfigurationManager configurationManager,
            EncodingHelper encodingHelper
            )
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _libraryMonitor = libraryMonitor;
            _localization = localization;
            _mediaEncoder = mediaEncoder;
            _configurationManager = configurationManager;
            _encodingHelper = encodingHelper;
        }

        public string Name => "Generate Timeline Images";
        public string Description => "Generates timeline preview images for videos";
        public string Category => _localization.GetLocalizedString("TasksLibraryCategory");
        public string Key => "TimelineGeneration";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(24).Ticks
                }
            ];
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var items = new List<Video>();
            var includedLibraryNames = DeoVrDeeplinkPlugin.Instance!.Configuration.TimelineIncludedLibrary;
            if (includedLibraryNames.Length > 0)
            {
                var allLibraries = GetAllLibraries().ToArray();
                var librariesToProcess = allLibraries.Where(lib => 
                    includedLibraryNames.Contains(lib.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

                foreach (var library in librariesToProcess)
                {
                    items.AddRange(GetVideosFromLibrary(library).ToArray());
                }
            }
            
            var numComplete = 0;
            
            foreach (var item in items)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create VideoProcessor manually - just like the working example
                    await new VideoProcessor(
                        _loggerFactory,
                        _loggerFactory.CreateLogger<VideoProcessor>(),
                        _mediaEncoder,
                        _configurationManager,
                        _fileSystem,
                        _appPaths,
                        _libraryMonitor,
                        _encodingHelper)
                        .Run(item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error creating timeline images for {0}: {1}", item.Name, ex);
                }

                numComplete++;
                double percent = numComplete;
                percent /= items.Count;
                percent *= 100;

                progress.Report(percent);
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

