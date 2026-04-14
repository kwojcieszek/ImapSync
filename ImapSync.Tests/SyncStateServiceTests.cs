using ImapSync.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImapSync.Tests;

public class SyncStateServiceTests : IDisposable
{
    private readonly string _tempFile =
        Path.Combine(Path.GetTempPath(), $"imap-sync-state-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private SyncStateService CreateSut() =>
        new(_tempFile, NullLogger<SyncStateService>.Instance);

    // ── IsInitialized ─────────────────────────────────────────────────────

    [Fact]
    public void IsInitialized_ReturnsFalse_ForUnknownPair()
    {
        using var sut = CreateSut();

        Assert.False(sut.IsInitialized("UnknownPair"));
    }

    [Fact]
    public async Task IsInitialized_ReturnsTrue_AfterMarkAsInitialized()
    {
        using var sut = CreateSut();

        await sut.MarkAsInitializedAsync("MyPair");

        Assert.True(sut.IsInitialized("MyPair"));
    }

    // ── GetLastSyncedAt ───────────────────────────────────────────────────

    [Fact]
    public void GetLastSyncedAt_ReturnsNull_ForUnknownPair()
    {
        using var sut = CreateSut();

        Assert.Null(sut.GetLastSyncedAt("UnknownPair"));
    }

    [Fact]
    public async Task GetLastSyncedAt_ReturnsRecentTimestamp_AfterMarkAsInitialized()
    {
        using var sut = CreateSut();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await sut.MarkAsInitializedAsync("MyPair");

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        var timestamp = sut.GetLastSyncedAt("MyPair");

        Assert.NotNull(timestamp);
        Assert.InRange(timestamp.Value, before, after);
    }

    // ── MarkAsInitializedAsync ────────────────────────────────────────────

    [Fact]
    public async Task MarkAsInitializedAsync_PersistsState_SoNewInstanceSeesIt()
    {
        using (var sut = CreateSut())
        {
            await sut.MarkAsInitializedAsync("PersistPair");
        }

        // Fresh instance reads the same file
        using var sut2 = CreateSut();
        Assert.True(sut2.IsInitialized("PersistPair"));
    }

    [Fact]
    public async Task MarkAsInitializedAsync_CalledTwice_DoesNotThrow()
    {
        using var sut = CreateSut();

        await sut.MarkAsInitializedAsync("MyPair");
        var ex = await Record.ExceptionAsync(() => sut.MarkAsInitializedAsync("MyPair"));

        Assert.Null(ex);
        Assert.True(sut.IsInitialized("MyPair"));
    }

    // ── UpdateLastSyncedAtAsync ───────────────────────────────────────────

    [Fact]
    public async Task UpdateLastSyncedAtAsync_UpdatesTimestamp_ForExistingEntry()
    {
        using var sut = CreateSut();
        await sut.MarkAsInitializedAsync("MyPair");

        var oldTimestamp = sut.GetLastSyncedAt("MyPair");
        await Task.Delay(20); // ensure clock advances
        await sut.UpdateLastSyncedAtAsync("MyPair");

        var newTimestamp = sut.GetLastSyncedAt("MyPair");
        Assert.True(newTimestamp > oldTimestamp, "Timestamp should have moved forward");
    }

    [Fact]
    public async Task UpdateLastSyncedAtAsync_CreatesEntry_WhenPairNotPreviouslyInitialized()
    {
        using var sut = CreateSut();

        // Fallback path: pair was never initialized
        await sut.UpdateLastSyncedAtAsync("NewPair");

        Assert.NotNull(sut.GetLastSyncedAt("NewPair"));
    }

    [Fact]
    public async Task UpdateLastSyncedAtAsync_PersistsState_SoNewInstanceSeesUpdatedTimestamp()
    {
        using (var sut = CreateSut())
        {
            await sut.MarkAsInitializedAsync("MyPair");
            await Task.Delay(20);
            await sut.UpdateLastSyncedAtAsync("MyPair");
        }

        using var sut2 = CreateSut();
        Assert.True(sut2.IsInitialized("MyPair"));
        Assert.NotNull(sut2.GetLastSyncedAt("MyPair"));
    }

    // ── State file loading ────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsEmptyState_WhenFileDoesNotExist()
    {
        // _tempFile guaranteed not to exist yet
        using var sut = CreateSut();

        Assert.False(sut.IsInitialized("AnyPair"));
        Assert.Null(sut.GetLastSyncedAt("AnyPair"));
    }

    [Fact]
    public void Load_ReturnsEmptyState_WhenJsonIsCorrupted()
    {
        File.WriteAllText(_tempFile, "{ this is NOT valid json !!! }");

        using var sut = CreateSut(); // must not throw

        Assert.False(sut.IsInitialized("AnyPair"));
    }

    [Fact]
    public void Load_ReturnsEmptyState_WhenFileContainsNull()
    {
        File.WriteAllText(_tempFile, "null");

        using var sut = CreateSut();

        Assert.False(sut.IsInitialized("AnyPair"));
    }

    [Fact]
    public async Task Load_RestoresMultiplePairs_WhenValidFileExists()
    {
        using (var sut = CreateSut())
        {
            await sut.MarkAsInitializedAsync("PairA");
            await sut.MarkAsInitializedAsync("PairB");
        }

        using var sut2 = CreateSut();

        Assert.True(sut2.IsInitialized("PairA"));
        Assert.True(sut2.IsInitialized("PairB"));
        Assert.False(sut2.IsInitialized("PairC"));
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var sut = CreateSut();

        var ex = Record.Exception(() =>
        {
            sut.Dispose();
            sut.Dispose();
        });

        Assert.Null(ex);
    }
}
