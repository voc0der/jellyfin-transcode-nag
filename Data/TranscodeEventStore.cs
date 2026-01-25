using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.TranscodeNag.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodeNag.Data;

public class TranscodeEventStore
{
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<TranscodeEventStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string DataFileName = "events.json";

    public TranscodeEventStore(IApplicationPaths applicationPaths, ILogger<TranscodeEventStore> logger)
    {
        _appPaths = applicationPaths;
        _logger = logger;
    }

    private string GetDataFilePath()
    {
        var dataDir = Path.Combine(_appPaths.DataPath, "plugins", "data", "TranscodeNag");
        Directory.CreateDirectory(dataDir);
        return Path.Combine(dataDir, DataFileName);
    }

    public async void AddEvent(TranscodeEvent transcodeEvent)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var events = await LoadEventsAsync().ConfigureAwait(false);
            events.Add(transcodeEvent);

            // Clean up old events (keep last 30 days)
            var cutoff = DateTime.UtcNow.AddDays(-30);
            events.RemoveAll(e => e.Timestamp < cutoff);

            await SaveEventsAsync(events).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding transcode event");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async System.Threading.Tasks.Task<List<TranscodeEvent>> GetUserEventsAsync(string userId, int days)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var events = await LoadEventsAsync().ConfigureAwait(false);
            var cutoff = DateTime.UtcNow.AddDays(-days);

            return events
                .Where(e => e.UserId == userId && e.Timestamp >= cutoff)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async System.Threading.Tasks.Task<List<TranscodeEvent>> LoadEventsAsync()
    {
        var filePath = GetDataFilePath();

        if (!File.Exists(filePath))
        {
            return new List<TranscodeEvent>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<TranscodeEvent>>(json) ?? new List<TranscodeEvent>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transcode events from {FilePath}", filePath);
            return new List<TranscodeEvent>();
        }
    }

    private async System.Threading.Tasks.Task SaveEventsAsync(List<TranscodeEvent> events)
    {
        var filePath = GetDataFilePath();

        try
        {
            var json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving transcode events to {FilePath}", filePath);
        }
    }
}
