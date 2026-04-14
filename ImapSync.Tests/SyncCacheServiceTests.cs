using ImapSync.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImapSync.Tests;

public class SyncCacheServiceTests
{
    private readonly SyncCacheService _sut = new(NullLogger<SyncCacheService>.Instance);

    [Fact]
    public void IsKnownSynced_ReturnsFalse_WhenKeyNotRegistered()
    {
        var result = _sut.IsKnownSynced("pair", "user@dest.com", "<msg@example.com>");

        Assert.False(result);
    }

    [Fact]
    public void MarkAsSynced_MakesIsKnownSynced_ReturnTrue()
    {
        _sut.MarkAsSynced("pair", "user@dest.com", "<msg@example.com>");

        Assert.True(_sut.IsKnownSynced("pair", "user@dest.com", "<msg@example.com>"));
    }

    [Fact]
    public void MarkAsSynced_DifferentPairName_ProducesIndependentKey()
    {
        _sut.MarkAsSynced("pair-A", "user@dest.com", "<msg@example.com>");

        Assert.False(_sut.IsKnownSynced("pair-B", "user@dest.com", "<msg@example.com>"));
    }

    [Fact]
    public void MarkAsSynced_DifferentDestinationUsername_ProducesIndependentKey()
    {
        _sut.MarkAsSynced("pair", "alice@dest.com", "<msg@example.com>");

        Assert.False(_sut.IsKnownSynced("pair", "bob@dest.com", "<msg@example.com>"));
    }

    [Fact]
    public void MarkAsSynced_DifferentMessageId_ProducesIndependentKey()
    {
        _sut.MarkAsSynced("pair", "user@dest.com", "<msg-1@example.com>");

        Assert.False(_sut.IsKnownSynced("pair", "user@dest.com", "<msg-2@example.com>"));
    }

    [Fact]
    public void MarkAsSynced_CalledTwiceWithSameKey_DoesNotThrow()
    {
        _sut.MarkAsSynced("pair", "user@dest.com", "<msg@example.com>");

        var ex = Record.Exception(() => _sut.MarkAsSynced("pair", "user@dest.com", "<msg@example.com>"));

        Assert.Null(ex);
    }

    [Fact]
    public void InvalidateIfDayChanged_CalledOnSameDay_PreservesCache()
    {
        _sut.MarkAsSynced("pair", "user@dest.com", "<msg@example.com>");

        _sut.InvalidateIfDayChanged(); // same day — must not clear

        Assert.True(_sut.IsKnownSynced("pair", "user@dest.com", "<msg@example.com>"));
    }

    [Fact]
    public void InvalidateIfDayChanged_CalledRepeatedly_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            _sut.InvalidateIfDayChanged();
            _sut.InvalidateIfDayChanged();
            _sut.InvalidateIfDayChanged();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void IsKnownSynced_KeyIsCaseSensitive()
    {
        _sut.MarkAsSynced("Pair", "User@dest.com", "<Msg@example.com>");

        Assert.False(_sut.IsKnownSynced("pair", "user@dest.com", "<msg@example.com>"));
        Assert.True(_sut.IsKnownSynced("Pair", "User@dest.com", "<Msg@example.com>"));
    }

    [Fact]
    public void MarkAsSynced_MultiplePairsAndDestinations_AreIndependent()
    {
        _sut.MarkAsSynced("pair-1", "alice@dest.com", "<msg@example.com>");
        _sut.MarkAsSynced("pair-2", "bob@dest.com", "<msg@example.com>");

        Assert.True(_sut.IsKnownSynced("pair-1", "alice@dest.com", "<msg@example.com>"));
        Assert.True(_sut.IsKnownSynced("pair-2", "bob@dest.com", "<msg@example.com>"));
        Assert.False(_sut.IsKnownSynced("pair-1", "bob@dest.com", "<msg@example.com>"));
        Assert.False(_sut.IsKnownSynced("pair-2", "alice@dest.com", "<msg@example.com>"));
    }
}
