using System;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.TranscodeNag.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    private static readonly string[] DefaultAlertReasons =
    {
        nameof(TranscodeReason.ContainerNotSupported),
        nameof(TranscodeReason.VideoCodecNotSupported),
        nameof(TranscodeReason.AudioCodecNotSupported),
        nameof(TranscodeReason.SubtitleCodecNotSupported),
        nameof(TranscodeReason.VideoProfileNotSupported),
        nameof(TranscodeReason.VideoLevelNotSupported),
        nameof(TranscodeReason.VideoResolutionNotSupported),
        nameof(TranscodeReason.VideoBitDepthNotSupported),
        nameof(TranscodeReason.VideoFramerateNotSupported),
        nameof(TranscodeReason.RefFramesNotSupported),
        nameof(TranscodeReason.AnamorphicVideoNotSupported),
        nameof(TranscodeReason.InterlacedVideoNotSupported),
        nameof(TranscodeReason.AudioChannelsNotSupported),
        nameof(TranscodeReason.AudioProfileNotSupported),
        nameof(TranscodeReason.AudioSampleRateNotSupported),
        nameof(TranscodeReason.SecondaryAudioNotSupported),
        nameof(TranscodeReason.VideoRangeTypeNotSupported),
        nameof(TranscodeReason.DirectPlayError)
    };

    public static string[] GetDefaultAlertTranscodeReasons()
    {
        return (string[])DefaultAlertReasons.Clone();
    }

    public string NagMessage { get; set; } = "Your client is transcoding because it doesn't support the video format. Consider using a client that supports direct play (like mpv, VLC, or Jellyfin Media Player) to reduce server load and improve quality!";

    public int MessageTimeoutMs { get; set; } = 10000;

    public bool EnableLogging { get; set; } = true;

    public int DelaySeconds { get; set; } = 5;

    public bool EnableLoginNag { get; set; } = true;

    public int LoginNagThreshold { get; set; } = 5;

    public string LoginNagTimeWindow { get; set; } = "Week";

    public string LoginNagMessage { get; set; } = "You've transcoded {{transcodes}} videos in the last {{timewindow}} due to unsupported formats. Consider switching to mpv, VLC, or Jellyfin Media Player to improve quality and reduce server load!";

    public string[] AlertTranscodeReasons { get; set; } = GetDefaultAlertTranscodeReasons();

    public string[] ExcludedUserIds { get; set; } = Array.Empty<string>();
}
