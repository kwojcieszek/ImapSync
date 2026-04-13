using ImapSync.Core.Models;

namespace ImapSync.Core.Interfaces;

public interface IImapService
{
    Task<IImapConnection> OpenConnectionAsync(ImapCredentials credentials, CancellationToken cancellationToken = default);
}
