using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodeNag;

public class PlaybackMonitor : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PlaybackMonitor> _logger;
    private readonly HashSet<string> _naggedPlaybacks = new();

    public PlaybackMonitor(
        ISessionManager sessionManager,
        ILogger<PlaybackMonitor> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.LogInformation("PlaybackMonitor started - listening for playback events");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _logger.LogInformation("PlaybackMonitor stopped");
        return Task.CompletedTask;
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (Plugin.Instance == null || e.Session == null)
        {
            return;
        }

        var config = Plugin.Instance.Configuration;

        // Wait for transcoding info to be available
        await Task.Delay(config.DelaySeconds * 1000).ConfigureAwait(false);

        // Re-fetch session to get updated transcoding info
        var session = _sessionManager.Sessions.FirstOrDefault(s => s.Id == e.Session.Id);
        if (session == null || session.NowPlayingItem == null)
        {
            return;
        }

        var playbackKey = $"{session.Id}_{session.NowPlayingItem.Id}";

        var transcodeInfo = session.TranscodingInfo;
        if (transcodeInfo == null || transcodeInfo.IsVideoDirect)
        {
            // Not transcoding - remove from nagged list if present
            _naggedPlaybacks.Remove(playbackKey);
            return;
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

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Session?.Id != null && e.Item?.Id != null)
        {
            var playbackKey = $"{e.Session.Id}_{e.Item.Id}";
            _naggedPlaybacks.Remove(playbackKey);
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
}
