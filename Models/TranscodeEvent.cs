using System;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.TranscodeNag.Models;

public class TranscodeEvent
{
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public TranscodeReason Reasons { get; set; }

    public string Client { get; set; } = string.Empty;
}
