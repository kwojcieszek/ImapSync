using ImapSync.Application.Services;
using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;

namespace ImapSync.Tests;

public class SyncServiceTests
{
    private readonly Mock<IImapService> _imapServiceMock;
    private readonly Mock<ISyncStateService> _stateMock;
    private readonly Mock<ISyncCacheService> _cacheMock;
    private readonly Mock<IImapConnection> _sourceConnMock;
    private readonly Mock<IImapConnection> _dest1ConnMock;
    private readonly Mock<IImapConnection> _dest2ConnMock;
    private readonly SyncService _sut;

    private static ImapCredentials Source => new() { Host = "source.imap.com", Port = 993, UseSsl = true, Username = "src@test.com", Password = "pass" };
    private static ImapCredentials Dest1 => new() { Host = "dest1.imap.com", Port = 993, UseSsl = true, Username = "dst1@test.com", Password = "pass" };
    private static ImapCredentials Dest2 => new() { Host = "dest2.imap.com", Port = 993, UseSsl = true, Username = "dst2@test.com", Password = "pass" };

    private static MailboxPair MakePair(params ImapCredentials[] destinations) => new()
    {
        Name = "TestPair",
        Source = Source,
        Destinations = [.. destinations]
    };

    public SyncServiceTests()
    {
        _imapServiceMock = new Mock<IImapService>();
        _stateMock = new Mock<ISyncStateService>();
        _cacheMock = new Mock<ISyncCacheService>();
        _sourceConnMock = new Mock<IImapConnection>();
        _dest1ConnMock = new Mock<IImapConnection>();
        _dest2ConnMock = new Mock<IImapConnection>();

        // Default: cache reports nothing as synced
        _cacheMock.Setup(c => c.IsKnownSynced(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(false);

        // Wire connections to credentials by username
        _imapServiceMock
            .Setup(s => s.OpenConnectionAsync(It.Is<ImapCredentials>(c => c.Username == "src@test.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sourceConnMock.Object);
        _imapServiceMock
            .Setup(s => s.OpenConnectionAsync(It.Is<ImapCredentials>(c => c.Username == "dst1@test.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_dest1ConnMock.Object);
        _imapServiceMock
            .Setup(s => s.OpenConnectionAsync(It.Is<ImapCredentials>(c => c.Username == "dst2@test.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_dest2ConnMock.Object);

        _sut = new SyncService(_imapServiceMock.Object, _stateMock.Object, _cacheMock.Object, NullLogger<SyncService>.Instance);
    }

    private void SetInitialized(bool initialized, DateTimeOffset? lastSyncedAt = null)
    {
        _stateMock.Setup(s => s.IsInitialized(It.IsAny<string>())).Returns(initialized);
        _stateMock.Setup(s => s.GetLastSyncedAt(It.IsAny<string>()))
            .Returns(initialized ? (lastSyncedAt ?? DateTimeOffset.UtcNow.AddHours(-1)) : null);
    }

    private void SetupEmptyFolders()
    {
        _sourceConnMock
            .Setup(c => c.GetFoldersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    // ── Incremental mode ──────────────────────────────────────────────────

    [Fact]
    public async Task SyncMailboxPair_NoDestinations_ReturnsEmptyResult()
    {
        SetInitialized(true);
        var result = await _sut.SyncMailboxPairAsync(MakePair());

        Assert.True(result.Success);
        _imapServiceMock.Verify(s => s.OpenConnectionAsync(It.IsAny<ImapCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_Initialized_UsesGetMessagesSince()
    {
        var lastSync = DateTimeOffset.UtcNow.AddDays(-2);
        SetInitialized(true, lastSync);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _dest1ConnMock
            .Setup(c => c.FolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _sourceConnMock.Verify(
            c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _sourceConnMock.Verify(
            c => c.GetAllMessageIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_Initialized_NoErrors_UpdatesLastSyncedAt()
    {
        SetInitialized(true);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _stateMock.Verify(s => s.UpdateLastSyncedAtAsync("TestPair", It.IsAny<CancellationToken>()), Times.Once);
        _stateMock.Verify(s => s.MarkAsInitializedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_Initialized_WithErrors_DoesNotUpdateLastSyncedAt()
    {
        SetInitialized(true);
        var messageId = "<err@example.com>";
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MimeMessage?)null); // triggers error

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.False(result.Success);
        _stateMock.Verify(s => s.UpdateLastSyncedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_MessageAlreadyExistsOnAllDestinations_Skipped()
    {
        SetInitialized(true);
        var messageId = "<test-msg-id@example.com>";
        var pair = MakePair(Dest1, Dest2);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dest2ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.Equal(0, result.MessagesCopied);
        Assert.Equal(2, result.MessagesSkipped);
        _sourceConnMock.Verify(
            c => c.FetchMessageAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_NewMessage_CopiedToAllDestinations_FetchedOnce()
    {
        SetInitialized(true);
        var messageId = "<multi-dest-msg@example.com>";
        var mimeMessage = new MimeMessage();
        mimeMessage.MessageId = messageId.Trim('<', '>');
        var pair = MakePair(Dest1, Dest2);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        // First call: initial check → not present; second call: verification after append → confirmed
        _dest1ConnMock.SetupSequence(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        _dest2ConnMock.SetupSequence(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mimeMessage);

        _dest1ConnMock.Setup(c => c.EnsureFolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest1ConnMock.Setup(c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest2ConnMock.Setup(c => c.EnsureFolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest2ConnMock.Setup(c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.Equal(2, result.MessagesCopied);
        Assert.True(result.Success);
        _sourceConnMock.Verify(
            c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncMailboxPair_AppendSucceeds_ButVerificationFails_ReportsError()
    {
        SetInitialized(true);
        var messageId = "<unverified-msg@example.com>";
        var mimeMessage = new MimeMessage();
        mimeMessage.MessageId = messageId.Trim('<', '>');
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        // Both calls return false: initial check and post-append verification
        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mimeMessage);

        _dest1ConnMock.Setup(c => c.EnsureFolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest1ConnMock.Setup(c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.Equal(0, result.MessagesCopied);
        Assert.False(result.Success);
        _cacheMock.Verify(c => c.MarkAsSynced(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_NewMessage_ExistsOnOneDest_CopiedOnlyToMissing()
    {
        SetInitialized(true);
        var messageId = "<partial-msg@example.com>";
        var mimeMessage = new MimeMessage();
        mimeMessage.MessageId = messageId.Trim('<', '>');
        var pair = MakePair(Dest1, Dest2);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        // Dest1: already exists (skipped)
        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Dest2: not present initially, confirmed after append
        _dest2ConnMock.SetupSequence(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mimeMessage);

        _dest2ConnMock.Setup(c => c.EnsureFolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest2ConnMock.Setup(c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.Equal(1, result.MessagesCopied);
        Assert.Equal(1, result.MessagesSkipped);
        _dest2ConnMock.Verify(
            c => c.AppendMessageAsync("INBOX", mimeMessage, It.IsAny<CancellationToken>()),
            Times.Once);
        _dest1ConnMock.Verify(
            c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_MultipleSubFolders_AllProcessed()
    {
        SetInitialized(true);
        var pair = MakePair(Dest1);

        _sourceConnMock
            .Setup(c => c.GetFoldersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Sent", "Archive" });

        _dest1ConnMock
            .Setup(c => c.FolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _sourceConnMock.Verify(
            c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // INBOX + Sent + Archive
    }

    // ── Full sync mode ────────────────────────────────────────────────────

    [Fact]
    public async Task SyncMailboxPair_NotInitialized_UsesGetAllMessages()
    {
        SetInitialized(false);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetAllMessageIdsAsync("INBOX", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _sourceConnMock.Verify(c => c.GetAllMessageIdsAsync("INBOX", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sourceConnMock.Verify(
            c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_NotInitialized_FullSyncNoErrors_MarksInitialized()
    {
        SetInitialized(false);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetAllMessageIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _stateMock.Verify(s => s.MarkAsInitializedAsync("TestPair", It.IsAny<CancellationToken>()), Times.Once);
        _stateMock.Verify(s => s.UpdateLastSyncedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_NotInitialized_FullSyncWithErrors_DoesNotMark()
    {
        SetInitialized(false);
        var messageId = "<err-msg@example.com>";
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetAllMessageIdsAsync("INBOX", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MimeMessage?)null);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.False(result.Success);
        _stateMock.Verify(s => s.MarkAsInitializedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _stateMock.Verify(s => s.UpdateLastSyncedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_AlreadyInitialized_DoesNotMarkAgain()
    {
        SetInitialized(true);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _stateMock.Verify(s => s.MarkAsInitializedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Cache tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task SyncMailboxPair_MessageInCache_SkipsImapCheck()
    {
        SetInitialized(true);
        var messageId = "<cached-msg@example.com>";
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        // Cache reports message as already synced
        _cacheMock.Setup(c => c.IsKnownSynced("TestPair", "dst1@test.com", messageId))
                  .Returns(true);

        var result = await _sut.SyncMailboxPairAsync(pair);

        Assert.Equal(1, result.MessagesSkipped);
        _dest1ConnMock.Verify(
            c => c.MessageExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sourceConnMock.Verify(
            c => c.FetchMessageAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMailboxPair_MessageCopied_MarkedInCache()
    {
        SetInitialized(true);
        var messageId = "<new-msg@example.com>";
        var mimeMessage = new MimeMessage();
        mimeMessage.MessageId = messageId.Trim('<', '>');
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        _dest1ConnMock.SetupSequence(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)   // initial check
            .ReturnsAsync(true);   // post-append verification

        _sourceConnMock
            .Setup(c => c.FetchMessageAsync("INBOX", 1u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mimeMessage);

        _dest1ConnMock.Setup(c => c.EnsureFolderExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dest1ConnMock.Setup(c => c.AppendMessageAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.SyncMailboxPairAsync(pair);

        _cacheMock.Verify(c => c.MarkAsSynced("TestPair", "dst1@test.com", messageId), Times.Once);
    }

    [Fact]
    public async Task SyncMailboxPair_MessageExistsOnDest_MarkedInCache()
    {
        SetInitialized(true);
        var messageId = "<existing-msg@example.com>";
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync("INBOX", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (messageId, 1u) });

        _dest1ConnMock
            .Setup(c => c.MessageExistsAsync("INBOX", messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.SyncMailboxPairAsync(pair);

        // Confirmed present via IMAP → should be cached for future cycles
        _cacheMock.Verify(c => c.MarkAsSynced("TestPair", "dst1@test.com", messageId), Times.Once);
    }

    [Fact]
    public async Task SyncCycleStart_InvalidateIfDayChangedAlwaysCalled()
    {
        SetInitialized(true);
        var pair = MakePair(Dest1);
        SetupEmptyFolders();

        _sourceConnMock
            .Setup(c => c.GetMessagesSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string, uint)>());

        await _sut.SyncMailboxPairAsync(pair);

        _cacheMock.Verify(c => c.InvalidateIfDayChanged(), Times.Once);
    }
}
