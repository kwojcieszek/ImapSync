namespace ImapSync.Core.Interfaces;

public interface ISyncStateService
{
    bool IsInitialized(string pairName);
    DateTimeOffset? GetLastSyncedAt(string pairName);
    Task MarkAsInitializedAsync(string pairName, CancellationToken cancellationToken = default);
    Task UpdateLastSyncedAtAsync(string pairName, CancellationToken cancellationToken = default);
}
