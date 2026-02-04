using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TranscodeNag.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string NagMessage { get; set; } = "Your client is transcoding because it doesn't support the video format. Consider using a client that supports direct play (like mpv, VLC, or Jellyfin Media Player) to reduce server load and improve quality!";

    public int MessageTimeoutMs { get; set; } = 10000;

    public bool EnableLogging { get; set; } = true;

    public int DelaySeconds { get; set; } = 5;

    public bool EnableLoginNag { get; set; } = true;

    public int LoginNagThreshold { get; set; } = 5;

    public string LoginNagTimeWindow { get; set; } = "Week";

    public string LoginNagMessage { get; set; } = "You've transcoded {{transcodes}} videos in the last {{timewindow}} due to unsupported formats. Consider switching to mpv, VLC, or Jellyfin Media Player to improve quality and reduce server load!";

    public string[] ExcludedUserIds { get; set; } = Array.Empty<string>();
}
