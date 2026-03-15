using Jellyfin.Plugin.TranscodeNag.Data;
using Jellyfin.Plugin.TranscodeNag.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.TranscodeNag.Tests;

public class TranscodeEventStoreTests
{
    [Fact]
    public async Task GetUserNagStatusAsync_TracksBadTranscodesCreditsAndRecentNags()
    {
        using var harness = new StoreHarness();
        var userId = Guid.NewGuid().ToString();

        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-40), "old-bad"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-3), "bad-1"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.ImprovementCredit, DateTime.UtcNow.AddDays(-2), "credit-1"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.NagSent, DateTime.UtcNow.AddDays(-1), "nag-1"));

        var status = await harness.Store.GetUserNagStatusAsync(userId, 7);
        var events = await harness.Store.GetUserEventsAsync(userId, 30);

        Assert.Equal(1, status.BadTranscodeCount);
        Assert.True(status.HasImprovementCredit);
        Assert.True(status.NaggedRecently);
        Assert.NotNull(status.LastBadTranscodeUtc);
        Assert.NotNull(status.LastNagUtc);
        Assert.Equal(3, events.Count);
        Assert.DoesNotContain(events, e => e.ItemId == "old-bad");
    }

    [Fact]
    public async Task AddEvent_RemovesOlderImprovementCreditAndClearsItAfterAnotherBadTranscode()
    {
        using var harness = new StoreHarness();
        var userId = Guid.NewGuid().ToString();

        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-6), "bad-1"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.ImprovementCredit, DateTime.UtcNow.AddDays(-5), "credit-old"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.ImprovementCredit, DateTime.UtcNow.AddDays(-4), "credit-new"));

        var afterCredits = await harness.Store.GetUserEventsAsync(userId, 30);

        Assert.Single(afterCredits.Where(e => e.Kind == NagEventKind.ImprovementCredit));
        Assert.Contains(afterCredits, e => e.ItemId == "credit-new");
        Assert.DoesNotContain(afterCredits, e => e.ItemId == "credit-old");

        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-1), "bad-2"));

        var status = await harness.Store.GetUserNagStatusAsync(userId, 30);
        var afterRegression = await harness.Store.GetUserEventsAsync(userId, 30);

        Assert.Equal(2, status.BadTranscodeCount);
        Assert.False(status.HasImprovementCredit);
        Assert.DoesNotContain(afterRegression, e => e.Kind == NagEventKind.ImprovementCredit);
    }

    [Fact]
    public async Task GetUserEventsAsync_FiltersByUserAndTimeWindowAndSortsNewestFirst()
    {
        using var harness = new StoreHarness();
        var userId = Guid.NewGuid().ToString();
        var otherUserId = Guid.NewGuid().ToString();

        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-2), "older"));
        await harness.AddEventAsync(CreateEvent(otherUserId, NagEventKind.BadTranscode, DateTime.UtcNow.AddDays(-1), "other-user"));
        await harness.AddEventAsync(CreateEvent(userId, NagEventKind.NagSent, DateTime.UtcNow.AddHours(-2), "newer"));

        var recentEvents = await harness.Store.GetUserEventsAsync(userId, 7);

        Assert.Equal(2, recentEvents.Count);
        Assert.Equal("newer", recentEvents[0].ItemId);
        Assert.Equal("older", recentEvents[1].ItemId);
        Assert.DoesNotContain(recentEvents, e => e.UserId == otherUserId);
    }

    private static TranscodeEvent CreateEvent(string userId, NagEventKind kind, DateTime timestampUtc, string itemId)
    {
        return new TranscodeEvent
        {
            UserId = userId,
            UserName = "Test User",
            ItemId = itemId,
            ItemName = itemId,
            Timestamp = timestampUtc,
            Reasons = kind == NagEventKind.BadTranscode ? MediaBrowser.Model.Session.TranscodeReason.VideoCodecNotSupported : 0,
            Client = "xUnit",
            Kind = kind
        };
    }

    private sealed class StoreHarness : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "transcode-nag-tests", Guid.NewGuid().ToString("N"));

        public StoreHarness()
        {
            Directory.CreateDirectory(_rootPath);
            Store = new TranscodeEventStore(new TestApplicationPaths(_rootPath), NullLogger<TranscodeEventStore>.Instance);
        }

        public TranscodeEventStore Store { get; }

        public async Task AddEventAsync(TranscodeEvent transcodeEvent)
        {
            Store.AddEvent(transcodeEvent);
            await WaitForAsync(async () =>
            {
                var events = await Store.GetUserEventsAsync(transcodeEvent.UserId, 365);
                return transcodeEvent.Timestamp < DateTime.UtcNow.AddDays(-30)
                    ? !events.Any(e => e.ItemId == transcodeEvent.ItemId)
                    : events.Any(e => e.ItemId == transcodeEvent.ItemId);
            });
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup for test data.
            }
        }
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(await condition(), "Timed out waiting for async event store operation to complete.");
    }
}
