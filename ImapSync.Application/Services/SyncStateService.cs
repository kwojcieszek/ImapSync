using System.Text.Json;
using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImapSync.Application.Services;

public class SyncStateService : ISyncStateService, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<SyncStateService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SyncState _state;
    private bool _disposed;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SyncStateService(string filePath, ILogger<SyncStateService> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _state = Load();
    }

    public bool IsInitialized(string pairName)
        => _state.Pairs.ContainsKey(pairName);

    public DateTimeOffset? GetLastSyncedAt(string pairName)
        => _state.Pairs.TryGetValue(pairName, out var s) ? s.LastSyncedAt : null;

    public async Task MarkAsInitializedAsync(string pairName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var now = DateTimeOffset.UtcNow;

            _state.Pairs[pairName] = new PairSyncState
            {
                InitializedAt = now,
                LastSyncedAt = now
            };
            await SaveAsync(cancellationToken);
            _logger.LogInformation("Pair '{PairName}' marked as initialized. Timestamp: {Timestamp:o}", pairName, now);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateLastSyncedAtAsync(string pairName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_state.Pairs.TryGetValue(pairName, out var existing))
            {
                existing.LastSyncedAt = now;
            }
            else
            {
                // Fallback: should not happen in normal flow, but handle gracefully
                _state.Pairs[pairName] = new PairSyncState { InitializedAt = now, LastSyncedAt = now };
            }
            await SaveAsync(cancellationToken);
            _logger.LogDebug("Pair '{PairName}' last sync updated to {Timestamp:o}", pairName, now);
        }
        finally
        {
            _lock.Release();
        }
    }

    private SyncState Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("State file not found at '{Path}'. Full sync will run for all pairs.", _filePath);
            return new SyncState();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<SyncState>(json);
            if (state is not null)
            {
                _logger.LogInformation("Loaded sync state from '{Path}'. Initialized pairs: {Count}", _filePath, state.Pairs.Count);
                foreach (var (name, pair) in state.Pairs)
                {
                    _logger.LogInformation("  [{Name}] initialized: {Init:o}, last sync: {Last:o}", name, pair.InitializedAt, pair.LastSyncedAt);
                }
                return state;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read state file '{Path}'. Starting fresh.", _filePath);
        }

        return new SyncState();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write state file '{Path}'.", _filePath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
