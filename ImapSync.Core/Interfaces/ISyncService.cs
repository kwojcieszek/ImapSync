using ImapSync.Core.Models;

namespace ImapSync.Core.Interfaces;

public interface ISyncService
{
    Task<SyncResult> SyncMailboxPairAsync(MailboxPair mailboxPair, CancellationToken cancellationToken = default);
}
