using System;
using Jellyfin.Plugin.TranscodeNag.Configuration;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.TranscodeNag;

internal static class TranscodeNagRules
{
    internal static TranscodeReason BuildConfiguredNagReasonMask(string[]? configuredReasonNames)
    {
        var selectedReasonNames = configuredReasonNames ?? PluginConfiguration.GetDefaultAlertTranscodeReasons();
        var reasonMask = (TranscodeReason)0;

        foreach (var reasonName in selectedReasonNames)
        {
            if (string.IsNullOrWhiteSpace(reasonName))
            {
                continue;
            }

            if (Enum.TryParse(reasonName, true, out TranscodeReason parsedReason))
            {
                reasonMask |= parsedReason;
            }
        }

        return reasonMask;
    }

    internal static bool ShouldNagTranscode(TranscodingInfo transcodeInfo, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(transcodeInfo);
        ArgumentNullException.ThrowIfNull(config);

        // If no transcode reasons specified, it's likely bitrate limiting - don't nag.
        if (transcodeInfo.TranscodeReasons == (TranscodeReason)0)
        {
            return false;
        }

        var enabledNagReasons = BuildConfiguredNagReasonMask(config.AlertTranscodeReasons);
        if (enabledNagReasons == (TranscodeReason)0)
        {
            return false;
        }

        return (transcodeInfo.TranscodeReasons & enabledNagReasons) != 0;
    }

    internal static (int Days, string Label) ResolveLoginNagWindow(string? configuredTimeWindow)
    {
        return configuredTimeWindow == "Month" ? (30, "month") : (7, "week");
    }

    internal static string FormatLoginNagMessage(string template, int badTranscodeCount, string timeWindowLabel)
    {
        return template
            .Replace("{{transcodes}}", badTranscodeCount.ToString(), StringComparison.Ordinal)
            .Replace("{{timewindow}}", timeWindowLabel, StringComparison.Ordinal);
    }
}
