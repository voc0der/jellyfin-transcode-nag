using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TranscodeNag.Data;
using Jellyfin.Plugin.TranscodeNag.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
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
    private readonly TranscodeEventStore _eventStore;
    private readonly HashSet<string> _naggedPlaybacks = new();

    // "Open Jellyfin" detection for long-lived sessions:
    // We poll session activity timestamps (via reflection) and treat a large activity jump as a new "open".
    // This keeps behavior compatible across Jellyfin builds even if the SessionInfo property name changes.
    private readonly Dictionary<string, DateTime> _sessionLastActivityUtc = new();
    private readonly object _sessionLastActivityLock = new();
    private Timer? _sessionPollTimer;

    // If a session goes idle for at least this long and then becomes active again,
    // treat it as the user "opening" Jellyfin again.
    private static readonly TimeSpan OpenIdleThreshold = TimeSpan.FromMinutes(10);

    public PlaybackMonitor(
        ISessionManager sessionManager,
        IApplicationPaths applicationPaths,
        ILogger<PlaybackMonitor> logger,
        ILogger<TranscodeEventStore> eventStoreLogger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        _eventStore = new TranscodeEventStore(applicationPaths, eventStoreLogger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionStarted += OnSessionStarted;
        // Polling is used ONLY to catch re-opens of existing sessions.
        // (SessionStarted covers fresh sessions.)
        _sessionPollTimer = new Timer(PollSessionsForReopen, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        _logger.LogInformation("PlaybackMonitor started - listening for playback and session events");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionPollTimer?.Dispose();
        _logger.LogInformation("PlaybackMonitor stopped");
        return Task.CompletedTask;
    }

    private void PollSessionsForReopen(object? state)
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        var config = Plugin.Instance.Configuration;
        if (!config.EnableLoginNag)
        {
            return;
        }

        // If we can't read a last-activity timestamp, polling can't safely infer "reopen".
        // In that case, SessionStarted still works for fresh sessions.
        foreach (var session in _sessionManager.Sessions)
        {
            if (session.Id == null || session.UserId == Guid.Empty)
            {
                continue;
            }

            var lastActivity = TryGetSessionLastActivityUtc(session);
            if (!lastActivity.HasValue)
            {
                continue;
            }

            var sessionId = session.Id;
            var shouldTreatAsOpen = false;

            lock (_sessionLastActivityLock)
            {
                if (_sessionLastActivityUtc.TryGetValue(sessionId, out var prev))
                {
                    // If the session jumped forward by a lot, consider it a "re-open".
                    if (lastActivity.Value > prev && (lastActivity.Value - prev) >= OpenIdleThreshold)
                    {
                        shouldTreatAsOpen = true;
                    }

                    _sessionLastActivityUtc[sessionId] = lastActivity.Value;
                }
                else
                {
                    // First time seeing this session in the poller - treat as open.
                    _sessionLastActivityUtc[sessionId] = lastActivity.Value;
                    shouldTreatAsOpen = true;
                }
            }

            if (shouldTreatAsOpen)
            {
                _ = MaybeSendLoginOrOpenNagAsync(session, config);
            }
        }
    }

    private static DateTime? TryGetSessionLastActivityUtc(SessionInfo session)
    {
        try
        {
            // Jellyfin SessionInfo commonly exposes LastActivityDate (DateTime) or LastActivityDateUtc.
            var type = session.GetType();
            var prop = type.GetProperty("LastActivityDate", BindingFlags.Instance | BindingFlags.Public)
                       ?? type.GetProperty("LastActivityDateUtc", BindingFlags.Instance | BindingFlags.Public)
                       ?? type.GetProperty("LastActivity", BindingFlags.Instance | BindingFlags.Public);

            if (prop == null)
            {
                return null;
            }

            var value = prop.GetValue(session);
            if (value is DateTime dt)
            {
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            }

            return null;
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }
        catch (TargetException)
        {
            return null;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
        catch (MethodAccessException)
        {
            return null;
        }
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
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

            if (!IsClientAllowed(session, config, "playback nag"))
            {
                _naggedPlaybacks.Remove(playbackKey);
                return;
            }

            var transcodeInfo = session.TranscodingInfo;
            if (transcodeInfo == null || transcodeInfo.IsVideoDirect)
            {
                // Good playback (direct play / direct stream) - record a credit so users don't get dinged
                // on login/open nags until the next bad transcode.
                RecordImprovementCreditIfNeeded(session, config);

                // Not transcoding - remove from nagged list if present
                _naggedPlaybacks.Remove(playbackKey);
                return;
            }

            // Check if transcoding is due to unsupported format/codec
            if (TranscodeNagRules.ShouldNagTranscode(transcodeInfo, config))
            {
                // Record the event
                RecordTranscodeEvent(session, transcodeInfo);

                if (!_naggedPlaybacks.Contains(playbackKey))
                {
                    await SendNagMessageAsync(session, config).ConfigureAwait(false);
                    _naggedPlaybacks.Add(playbackKey);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Error handling playback-start nag");
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

    private bool IsUserExcluded(Guid? userId)
    {
        if (userId == null || Plugin.Instance == null)
        {
            return false;
        }

        var config = Plugin.Instance.Configuration;
        if (config.ExcludedUserIds == null || config.ExcludedUserIds.Length == 0)
        {
            return false;
        }

        var userIdString = userId.Value.ToString("N");
        return Array.IndexOf(config.ExcludedUserIds, userIdString) >= 0;
    }

    private bool IsClientAllowed(SessionInfo session, Configuration.PluginConfiguration config, string context)
    {
        if (TranscodeNagRules.IsClientAllowed(session.Client, config))
        {
            return true;
        }

        if (config.EnableLogging)
        {
            _logger.LogInformation(
                "Skipping {Context} for filtered client {Client} on session {SessionId}",
                context,
                session.Client ?? "Unknown",
                session.Id ?? "Unknown");
        }

        return false;
    }

    private static string ResolveNagMessage(SessionInfo session, Configuration.PluginConfiguration config)
    {
        var transcodeInfo = session.TranscodingInfo;
        var overrides = config.ReasonMessageOverrides;

        if (transcodeInfo != null && overrides != null && overrides.Length > 0 && config.AlertTranscodeReasons != null)
        {
            foreach (var reasonName in config.AlertTranscodeReasons)
            {
                if (string.IsNullOrWhiteSpace(reasonName))
                {
                    continue;
                }

                if (Enum.TryParse<TranscodeReason>(reasonName, true, out var parsedReason)
                    && (transcodeInfo.TranscodeReasons & parsedReason) != 0)
                {
                    foreach (var overrideEntry in overrides)
                    {
                        if (overrideEntry != null
                            && string.Equals(overrideEntry.ReasonName, reasonName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(overrideEntry.Message))
                        {
                            return overrideEntry.Message;
                        }
                    }
                }
            }
        }

        return config.NagMessage;
    }

    private async Task SendNagMessageAsync(SessionInfo session, Configuration.PluginConfiguration config)
    {
        if (session.Id == null)
        {
            return;
        }

        // Check if user is excluded from nag messages
        if (IsUserExcluded(session.UserId))
        {
            if (config.EnableLogging)
            {
                _logger.LogInformation(
                    "Skipping nag for excluded user {UserId} ({UserName})",
                    session.UserId,
                    session.UserName ?? "Unknown");
            }
            return;
        }

        var transcodeReasons = session.TranscodingInfo?.TranscodeReasons.ToString() ?? "Unknown";

        await SendMessageCommandWithDiagnosticsAsync(
            session,
            config,
            new MessageCommand
            {
                Header = "Transcoding Detected",
                Text = ResolveNagMessage(session, config),
                TimeoutMs = config.MessageTimeoutMs
            },
            "playback nag",
            $"Reasons: {transcodeReasons}").ConfigureAwait(false);
    }

    private async Task<bool> SendMessageCommandWithDiagnosticsAsync(
        SessionInfo session,
        Configuration.PluginConfiguration config,
        MessageCommand command,
        string context,
        string detail)
    {
        if (session.Id == null)
        {
            return false;
        }

        LogMessageDeliveryDiagnostics(session, config, context, detail);

        try
        {
            await _sessionManager.SendMessageCommand(
                null,
                session.Id,
                command,
                CancellationToken.None).ConfigureAwait(false);

            if (config.EnableLogging)
            {
                _logger.LogInformation(
                    "Completed {Context} message send to session {SessionId}",
                    context,
                    session.Id);
            }

            return true;
        }
        catch (ResourceNotFoundException ex)
        {
            _logger.LogError(ex, "Error sending {Context} message to session {SessionId}", context, session.Id);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Error sending {Context} message to session {SessionId}", context, session.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error sending {Context} message to session {SessionId}", context, session.Id);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Error sending {Context} message to session {SessionId}", context, session.Id);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Error sending {Context} message to session {SessionId}", context, session.Id);
        }

        return false;
    }

    private void LogMessageDeliveryDiagnostics(
        SessionInfo session,
        Configuration.PluginConfiguration config,
        string context,
        string detail)
    {
        if (!config.EnableLogging)
        {
            return;
        }

        var (controllerCount, activeControllerCount, mediaControlControllerCount) = GetSessionControllerStats(session);
        var supportedCommands = FormatSupportedCommands(session.SupportedCommands);
        var supportsDisplayMessage = session.SupportedCommands?.Contains(GeneralCommandType.DisplayMessage) == true;

        _logger.LogInformation(
            "Sending {Context} message to session {SessionId} ({Client} {ApplicationVersion}) for user {UserName} on device {DeviceName} ({DeviceId}) - {Detail}; Controllers: {ControllerCount} total, {ActiveControllerCount} active, {MediaControlControllerCount} media-control; SupportsRemoteControl: {SupportsRemoteControl}; SupportsMediaControl: {SupportsMediaControl}; SupportsDisplayMessage: {SupportsDisplayMessage}; SupportedCommands: {SupportedCommands}",
            context,
            session.Id ?? "Unknown",
            session.Client ?? "Unknown",
            session.ApplicationVersion ?? "Unknown",
            session.UserName ?? "Unknown",
            session.DeviceName ?? "Unknown",
            session.DeviceId ?? "Unknown",
            detail,
            controllerCount,
            activeControllerCount,
            mediaControlControllerCount,
            session.SupportsRemoteControl,
            session.SupportsMediaControl,
            supportsDisplayMessage,
            supportedCommands);

        if (controllerCount == 0 || activeControllerCount == 0)
        {
            _logger.LogWarning(
                "{Context} target session {SessionId} has {ControllerCount} controller(s) and {ActiveControllerCount} active controller(s). Jellyfin may accept the command without any client receiving a popup; check WebSocket/reverse proxy/client session state.",
                context,
                session.Id ?? "Unknown",
                controllerCount,
                activeControllerCount);
        }
        else if (!supportsDisplayMessage)
        {
            _logger.LogWarning(
                "{Context} target session {SessionId} does not advertise DisplayMessage support. The client may ignore the nag popup.",
                context,
                session.Id ?? "Unknown");
        }
    }

    private static (int ControllerCount, int ActiveControllerCount, int MediaControlControllerCount) GetSessionControllerStats(SessionInfo session)
    {
        var controllers = session.SessionControllers;
        if (controllers == null)
        {
            return (0, 0, 0);
        }

        return (
            controllers.Count,
            controllers.Count(controller => controller.IsSessionActive),
            controllers.Count(controller => controller.SupportsMediaControl));
    }

    private static string FormatSupportedCommands(IReadOnlyList<GeneralCommandType>? supportedCommands)
    {
        if (supportedCommands == null || supportedCommands.Count == 0)
        {
            return "None";
        }

        return string.Join(", ", supportedCommands);
    }

    private void RecordTranscodeEvent(SessionInfo session, TranscodingInfo transcodeInfo)
    {
        if (session.UserId == Guid.Empty || session.NowPlayingItem == null)
        {
            return;
        }

        var transcodeEvent = new TranscodeEvent
        {
            UserId = session.UserId.ToString(),
            UserName = session.UserName ?? "Unknown",
            ItemId = session.NowPlayingItem.Id.ToString(),
            ItemName = session.NowPlayingItem.Name ?? "Unknown",
            Timestamp = DateTime.UtcNow,
            Reasons = transcodeInfo.TranscodeReasons,
            Client = session.Client ?? "Unknown",
            Kind = NagEventKind.BadTranscode
        };

        _eventStore.AddEvent(transcodeEvent);
    }

    private void RecordImprovementCreditIfNeeded(SessionInfo session, Configuration.PluginConfiguration config)
    {
        if (session.UserId == Guid.Empty || session.NowPlayingItem == null)
        {
            return;
        }

        // Only record a credit if the user has had at least one bad transcode recently.
        // This keeps events.json from growing rapidly for users who already direct play everything.
        try
        {
            // Fire-and-forget: GetUserNagStatusAsync takes a lock and reads the file.
            _ = Task.Run(async () =>
            {
                var status = await _eventStore.GetUserNagStatusAsync(
                    session.UserId.ToString(),
                    30,
                    e => TranscodeNagRules.IsClientAllowed(e.Client, config)).ConfigureAwait(false);

                if (!status.LastBadTranscodeUtc.HasValue)
                {
                    return;
                }

                // If they already have an improvement credit after their most recent bad transcode, don't add another.
                if (status.HasImprovementCredit)
                {
                    return;
                }

                var creditEvent = new TranscodeEvent
                {
                    UserId = session.UserId.ToString(),
                    UserName = session.UserName ?? "Unknown",
                    ItemId = session.NowPlayingItem.Id.ToString(),
                    ItemName = session.NowPlayingItem.Name ?? "Unknown",
                    Timestamp = DateTime.UtcNow,
                    Reasons = 0,
                    Client = session.Client ?? "Unknown",
                    Kind = NagEventKind.ImprovementCredit
                };

                _eventStore.AddEvent(creditEvent);
            });
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Skipping improvement credit task because monitor is disposing");
        }
        catch (TaskSchedulerException ex)
        {
            _logger.LogDebug(ex, "Unable to queue improvement credit task");
        }
    }

    private async void OnSessionStarted(object? sender, SessionEventArgs e)
    {
        if (Plugin.Instance == null || e.SessionInfo?.UserId == null)
        {
            return;
        }

        var config = Plugin.Instance.Configuration;

        if (!config.EnableLoginNag)
        {
            return;
        }

        // Wait a moment for session to fully initialize
        await Task.Delay(2000).ConfigureAwait(false);

        try
        {
            await MaybeSendLoginOrOpenNagAsync(e.SessionInfo, config).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Error handling session-start login nag");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error handling session-start login nag");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Error handling session-start login nag");
        }
    }

    private async Task MaybeSendLoginOrOpenNagAsync(SessionInfo session, Configuration.PluginConfiguration config)
    {
        if (session.Id == null || session.UserId == Guid.Empty)
        {
            return;
        }

        if (!config.EnableLoginNag)
        {
            return;
        }

        // Check if user is excluded from nag messages
        if (IsUserExcluded(session.UserId))
        {
            if (config.EnableLogging)
            {
                _logger.LogInformation(
                    "Skipping login nag for excluded user {UserId}",
                    session.UserId);
            }
            return;
        }

        if (!IsClientAllowed(session, config, "login/open nag"))
        {
            return;
        }

        var userId = session.UserId.ToString();

        var (days, timeWindowText) = TranscodeNagRules.ResolveLoginNagWindow(config.LoginNagTimeWindow);

        var status = await _eventStore.GetUserNagStatusAsync(
            userId,
            days,
            e => TranscodeNagRules.IsClientAllowed(e.Client, config)).ConfigureAwait(false);

        // Rate limit: only once per configured period.
        if (status.NaggedRecently)
        {
            return;
        }

        // If they demonstrated improvement (a direct play/stream) after their last bad transcode,
        // don't ding them again until they regress with another bad transcode.
        if (status.HasImprovementCredit)
        {
            return;
        }

        if (status.BadTranscodeCount < config.LoginNagThreshold)
        {
            return;
        }

        var message = TranscodeNagRules.FormatLoginNagMessage(
            config.LoginNagMessage,
            status.BadTranscodeCount,
            timeWindowText);

        var sent = await SendMessageCommandWithDiagnosticsAsync(
            session,
            config,
            new MessageCommand
            {
                Header = "Transcoding Alert",
                Text = message,
                TimeoutMs = config.MessageTimeoutMs
            },
            "login/open nag",
            $"{status.BadTranscodeCount} bad transcodes in last {timeWindowText}").ConfigureAwait(false);

        if (!sent)
        {
            return;
        }

        // Persist the rate-limit marker.
        _eventStore.AddEvent(new TranscodeEvent
        {
            UserId = userId,
            UserName = session.UserName ?? "Unknown",
            ItemId = session.NowPlayingItem?.Id.ToString() ?? string.Empty,
            ItemName = session.NowPlayingItem?.Name ?? string.Empty,
            Timestamp = DateTime.UtcNow,
            Reasons = 0,
            Client = session.Client ?? "Unknown",
            Kind = NagEventKind.NagSent
        });
    }
}
