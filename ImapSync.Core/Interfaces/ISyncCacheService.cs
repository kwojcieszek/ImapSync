namespace ImapSync.Core.Interfaces;

public interface ISyncCacheService
{
    /// <summary>
    /// Clears the cache if the current UTC date differs from the date when it was last populated.
    /// </summary>
    void InvalidateIfDayChanged();

    /// <summary>
    /// Returns true if this message is already confirmed to be present on the given destination.
    /// </summary>
    bool IsKnownSynced(string pairName, string destinationUsername, string messageId);

    /// <summary>
    /// Marks the message as confirmed present on the destination (either already existed or just copied).
    /// </summary>
    void MarkAsSynced(string pairName, string destinationUsername, string messageId);
}
