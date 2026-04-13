using System.Collections.Concurrent;
using ImapSync.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImapSync.Application.Services;

public class SyncCacheService(ILogger<SyncCacheService> logger) : ISyncCacheService
{
    // ConcurrentDictionary used as a thread-safe hash set (value is unused).
    private readonly ConcurrentDictionary<string, byte> _synced = new(StringComparer.Ordinal);
    private DateOnly _cacheDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly Lock _dateLock = new();

    public void InvalidateIfDayChanged()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (today == _cacheDate)
        {
            return;
        }

        lock (_dateLock)
        {
            if (today == _cacheDate)
            {
                return;
            }

            logger.LogInformation("Day changed ({Old} → {New}). Clearing sync cache ({Count} entries).",
                _cacheDate, today, _synced.Count);

            _synced.Clear();
            _cacheDate = today;
        }
    }

    public bool IsKnownSynced(string pairName, string destinationUsername, string messageId)
        => _synced.ContainsKey(BuildKey(pairName, destinationUsername, messageId));

    public void MarkAsSynced(string pairName, string destinationUsername, string messageId)
        => _synced.TryAdd(BuildKey(pairName, destinationUsername, messageId), 0);

    private static string BuildKey(string pairName, string destinationUsername, string messageId)
        => $"{pairName}|{destinationUsername}|{messageId}";
}
