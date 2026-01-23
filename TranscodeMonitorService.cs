using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodeNag;

public class TranscodeMonitorService : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<TranscodeMonitorService> _logger;
    private Timer? _timer;
    private readonly HashSet<string> _naggedPlaybacks = new();

    public TranscodeMonitorService(
        ISessionManager sessionManager,
        ILogger<TranscodeMonitorService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance == null)
        {
            _logger.LogError("Plugin instance is null, cannot start TranscodeMonitorService");
            return Task.CompletedTask;
        }

        var interval = Plugin.Instance.Configuration.CheckIntervalSeconds;
        _timer = new Timer(
            CheckSessions,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(interval));

        _logger.LogInformation("TranscodeMonitorService started with {Interval}s interval", interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        _logger.LogInformation("TranscodeMonitorService stopped");
        return Task.CompletedTask;
    }

    private void CheckSessions(object? state)
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        var config = Plugin.Instance.Configuration;
        var sessions = _sessionManager.Sessions;

        foreach (var session in sessions)
        {
            if (session.NowPlayingItem == null || session.PlayState?.IsPaused == true)
            {
                continue;
            }

            // Create unique key for this playback session (session + item)
            var playbackKey = $"{session.Id}_{session.NowPlayingItem.Id}";

            var transcodeInfo = session.TranscodingInfo;
            if (transcodeInfo == null || transcodeInfo.IsVideoDirect)
            {
                // Not transcoding or direct playing - remove this specific playback from nagged list
                _naggedPlaybacks.Remove(playbackKey);
                continue;
            }

            // Check if transcoding is due to unsupported format/codec
            if (ShouldNagSession(transcodeInfo))
            {
                if (!_naggedPlaybacks.Contains(playbackKey))
                {
                    SendNagMessage(session, config);
                    _naggedPlaybacks.Add(playbackKey);
                }
            }
        }
    }

    private bool ShouldNagSession(TranscodingInfo transcodeInfo)
    {
        var reasons = transcodeInfo.TranscodeReasons;

        // If no transcode reasons specified, it's likely bitrate limiting - don't nag
        if ((int)reasons == 0)
        {
            return false;
        }

        // Check for format/codec compatibility issues
        var badReasons = TranscodeReason.ContainerNotSupported
            | TranscodeReason.VideoCodecNotSupported
            | TranscodeReason.AudioCodecNotSupported
            | TranscodeReason.SubtitleCodecNotSupported
            | TranscodeReason.VideoProfileNotSupported
            | TranscodeReason.VideoLevelNotSupported
            | TranscodeReason.VideoResolutionNotSupported
            | TranscodeReason.VideoBitDepthNotSupported
            | TranscodeReason.VideoFramerateNotSupported
            | TranscodeReason.RefFramesNotSupported
            | TranscodeReason.AnamorphicVideoNotSupported
            | TranscodeReason.InterlacedVideoNotSupported
            | TranscodeReason.AudioChannelsNotSupported
            | TranscodeReason.AudioProfileNotSupported
            | TranscodeReason.AudioSampleRateNotSupported
            | TranscodeReason.SecondaryAudioNotSupported
            | TranscodeReason.VideoRangeTypeNotSupported
            | TranscodeReason.DirectPlayError;

        return (reasons & badReasons) != 0;
    }

    private void SendNagMessage(SessionInfo session, Configuration.PluginConfiguration config)
    {
        if (session.Id == null)
        {
            return;
        }

        try
        {
            var transcodeReasons = session.TranscodingInfo?.TranscodeReasons.ToString() ?? "Unknown";

            if (config.EnableLogging)
            {
                _logger.LogInformation(
                    "Sending nag message to session {SessionId} ({Client}) - Reasons: {Reasons}",
                    session.Id,
                    session.Client ?? "Unknown",
                    transcodeReasons);
            }

            _sessionManager.SendMessageCommand(
                null,
                session.Id,
                new MessageCommand
                {
                    Header = "Transcoding Detected",
                    Text = config.NagMessage,
                    TimeoutMs = config.MessageTimeoutMs
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending nag message to session {SessionId}", session.Id);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
