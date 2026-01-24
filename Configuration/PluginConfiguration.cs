using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TranscodeNag.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string NagMessage { get; set; } = "Your client is transcoding because it doesn't support the video format. Consider using a client that supports direct play (like mpv, VLC, or Jellyfin Media Player) to reduce server load and improve quality!";

    public int MessageTimeoutMs { get; set; } = 10000;

    public bool EnableLogging { get; set; } = true;

    public int DelaySeconds { get; set; } = 5;
}
