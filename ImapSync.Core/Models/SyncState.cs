namespace ImapSync.Core.Models;

public class PairSyncState
{
    public DateTimeOffset InitializedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public class SyncState
{
    public Dictionary<string, PairSyncState> Pairs { get; set; } = [];
}
