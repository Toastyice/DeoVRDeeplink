using System.Globalization;
using DeoVRDeeplink.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
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
        var interval = CalculateIntervalForItem(item); // Dynamic calculation
        
        if (!EnableForItem(item, _fileSystem, interval)) return;
        
        var mediaSources = ((IHasMediaSources)item).GetMediaSources(false)
            .ToList();
        
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
                _logger.LogDebug($"Timeline image already exists and is up to date for {item.Name}");
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

    private int CalculateIntervalForItem(BaseItem item)
    {
        if (item is Video videoItem && videoItem.RunTimeTicks.HasValue)
        {
            // Calculate interval to get approximately 252 frames from the entire video
            var totalDurationTicks = videoItem.RunTimeTicks.Value;
            var intervalTicks = totalDurationTicks / 252;
            
            // Ensure minimum 10 second interval
            var minInterval = 10 * TimeSpan.TicksPerSecond; // 10 seconds in ticks
            
            return (int)Math.Max(intervalTicks, minInterval);
        }
        
        return 100_000_000; // Default 10 seconds in ticks
    }


    private static double GetFFmpegFpsForFilter(BaseItem item)
    {
        if (item is not Video { RunTimeTicks: not null } videoItem) return 0.0;
        return (double)videoItem.RunTimeTicks.Value / TimeSpan.TicksPerSecond / 252.0;
    }

    private List<string> GetFFmpegArgumentsForTimeline(BaseItem item, MediaSourceInfo mediaSource, string outputPath)
    {
        var fpsValue = GetFFmpegFpsForFilter(item); // This is your calculated value, not actual FPS
        var fpsString = fpsValue.ToString("F4", CultureInfo.InvariantCulture);
        _logger.LogError($"fps:{fpsString}");
        var args = new List<string>
        {
            "-i", $"\"{mediaSource.Path}\"",
            "-vf", $"\"crop=iw/2:ih:0:0,fps=1/{fpsString},scale=341:195,tile=12x21:margin=0:padding=0\"",
            "-q:v", "1",
            "-y",
            $"\"{outputPath}\""
        };
        return args;
    }

    private string GetTimelineOutputPath(BaseItem item)
    {
        var fileName = $"{item.Id}.jpg";
        var timelineCachePath = Path.Combine(_appPaths.CachePath, "deovr-timeline");
        
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
        _logger.LogInformation($"Executing FFmpeg command: {fullCommand}");
    
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
                _logger.LogError($"FFmpeg failed with exit code {process.ExitCode}: {error}");
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
    
    private static bool EnableForItem(BaseItem item, IFileSystem fileSystem, int interval)
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
