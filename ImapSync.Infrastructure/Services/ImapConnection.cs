using System.Security.Cryptography;
using System.Text;
using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Org.BouncyCastle.Utilities.Encoders;

namespace ImapSync.Infrastructure.Services;

public sealed class ImapConnection : IImapConnection
{
    private readonly ImapClient _client;
    private readonly ImapCredentials _credentials;
    private readonly SecureSocketOptions _sslOptions;
    private readonly ILogger<ImapConnection> _logger;
    private readonly Dictionary<string, IMailFolder> _folderCache = new(StringComparer.OrdinalIgnoreCase);

    internal ImapConnection(ImapClient client, ImapCredentials credentials, SecureSocketOptions sslOptions, ILogger<ImapConnection> logger)
    {
        _client = client;
        _credentials = credentials;
        _sslOptions = sslOptions;
        _logger = logger;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true })
        {
            return;
        }

        _logger.LogWarning("IMAP connection lost for {Username}@{Host}. Reconnecting…", _credentials.Username, _credentials.Host);

        // Clear folder cache — old IMailFolder handles are tied to the previous session.
        _folderCache.Clear();

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(false, ct);
            }

            await _client.ConnectAsync(_credentials.Host, _credentials.Port, _sslOptions, ct);
            await _client.AuthenticateAsync(_credentials.Username, _credentials.Password, ct);

            _logger.LogInformation("Reconnected to {Username}@{Host}", _credentials.Username, _credentials.Host);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconnect to {Username}@{Host}", _credentials.Username, _credentials.Host);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        var folders = new List<string>();

        if (_client.PersonalNamespaces.Count > 0)
        {
            foreach (var ns in _client.PersonalNamespaces)
            {
                var all = await _client.GetFoldersAsync(ns, cancellationToken: cancellationToken);

                foreach (var folder in all)
                {
                    if ((folder.Attributes & FolderAttributes.NonExistent) != 0)
                    {
                        continue;
                    }

                    if ((folder.Attributes & FolderAttributes.NoSelect) != 0)
                    {
                        continue;
                    }

                    if (string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    folders.Add(folder.FullName);
                }
            }
        }
        else
        {
            await CollectSubfoldersAsync(_client.Inbox, folders, cancellationToken);
        }

        _logger.LogInformation("Found {Count} folder(s): {Folders}", folders.Count, string.Join(", ", folders));

        return folders.AsReadOnly();
    }

    private static async Task CollectSubfoldersAsync(IMailFolder root, List<string> result, CancellationToken ct)
    {
        var subs = await root.GetSubfoldersAsync(false, ct);

        foreach (var sub in subs)
        {
            if ((sub.Attributes & FolderAttributes.NonExistent) != 0)
            {
                continue;
            }

            if ((sub.Attributes & FolderAttributes.NoSelect) == 0)
            {
                result.Add(sub.FullName);
            }

            await CollectSubfoldersAsync(sub, result, ct);
        }
    }

    public async Task<bool> FolderExistsAsync(string folderName, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        if (string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_folderCache.ContainsKey(folderName))
        {
            return true;
        }

        try
        {
            var folder = await _client.GetFolderAsync(folderName, cancellationToken);
            _folderCache[folderName] = folder;
            return true;
        }
        catch (FolderNotFoundException) { return false; }
    }

    public async Task EnsureFolderExistsAsync(string folderName, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        if (string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_folderCache.ContainsKey(folderName))
        {
            return;
        }

        if (await FolderExistsAsync(folderName, cancellationToken))
        {
            return;
        }

        _logger.LogInformation("Creating folder {FolderName}", folderName);

        await CreateFolderHierarchyAsync(folderName, cancellationToken);
    }

    private async Task CreateFolderHierarchyAsync(string fullFolderName, CancellationToken ct)
    {
        var separator = _client.PersonalNamespaces.Count > 0
            ? _client.PersonalNamespaces[0].DirectorySeparator
            : '/';

        var parts = fullFolderName.Split(separator);
        var current = string.Empty;

        var parentFolder = _client.PersonalNamespaces.Count > 0
            ? _client.GetFolder(_client.PersonalNamespaces[0])
            : _client.Inbox;

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            current = string.IsNullOrEmpty(current) ? part : $"{current}{separator}{part}";

            try
            {
                var existing = await _client.GetFolderAsync(current, ct);
                _folderCache[current] = existing;
                parentFolder = existing;
            }
            catch (FolderNotFoundException)
            {
                _logger.LogInformation("Creating folder part {Part} (full path: {FullPath})", part, current);
                var created = await parentFolder.CreateAsync(part, true, ct);
                _folderCache[current] = created;
                parentFolder = created;
            }
        }
    }

    public Task<IReadOnlyList<(string MessageId, uint Uid)>> GetAllMessageIdsAsync(
        string folderName, string accountName, CancellationToken cancellationToken = default)
        => FetchMessageIdsAsync(folderName, accountName, SearchQuery.All, cancellationToken);

    public Task<IReadOnlyList<(string MessageId, uint Uid)>> GetMessagesSinceAsync(
        string folderName, string accountName, DateTime since, CancellationToken cancellationToken = default)
        => FetchMessageIdsAsync(folderName, accountName, SearchQuery.DeliveredAfter(since.Date.AddDays(-1)), cancellationToken);

    private async Task<IReadOnlyList<(string MessageId, uint Uid)>> FetchMessageIdsAsync(
        string folderName, string accountName, SearchQuery query, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        var folder = await SelectFolderAsync(folderName, FolderAccess.ReadOnly, ct);

        if (folder is null)
        {
            return [];
        }

        var uids = await folder.SearchAsync(query, ct);

        if (uids.Count == 0)
        {
            return [];
        }

        var summaries = await folder.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct);

        var result = new List<(string, uint)>(summaries.Count);

        foreach (var s in summaries)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{s.UniqueId.Id}{s.Envelope?.To}{s.Envelope?.Date}{s.Envelope?.Subject}{accountName}"));
            result.Add(($"{s.UniqueId.Id}-{Hex.ToHexString(hash)}@{accountName}", s.UniqueId.Id));
        }

        return result;
    }

    public async Task<MimeMessage?> FetchMessageAsync(string folderName, uint uid, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        var folder = await SelectFolderAsync(folderName, FolderAccess.ReadOnly, cancellationToken);

        if (folder is null)
        {
            return null;
        }

        return await folder.GetMessageAsync(new UniqueId(uid), cancellationToken);
    }

    public async Task<bool> MessageExistsAsync(string folderName, string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        var folder = await SelectFolderAsync(folderName, FolderAccess.ReadOnly, cancellationToken);

        if (folder is null)
        {
            return false;
        }

        var matches = await folder.SearchAsync(SearchQuery.HeaderContains("Message-Id", messageId), cancellationToken);

        return matches.Count > 0;
    }

    public async Task AppendMessageAsync(string folderName, MimeMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        // APPEND does not require the folder to be selected
        var folder = await GetOrCacheFolderAsync(folderName, cancellationToken);
        await folder.AppendAsync(message, MessageFlags.None, cancellationToken);
        _logger.LogDebug("Appended {MessageId} to {FolderName}", message.MessageId, folderName);
    }

    private async Task<IMailFolder?> SelectFolderAsync(string folderName, FolderAccess access, CancellationToken ct)
    {
        IMailFolder folder;
        try
        {
            folder = await GetOrCacheFolderAsync(folderName, ct);
        }
        catch (FolderNotFoundException) { return null; }

        if (!folder.IsOpen || folder.Access < access)
        {
            await folder.OpenAsync(access, ct);
        }

        return folder;
    }

    private async Task<IMailFolder> GetOrCacheFolderAsync(string folderName, CancellationToken ct)
    {
        if (string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return _client.Inbox;
        }

        if (_folderCache.TryGetValue(folderName, out var cached))
        {
            return cached;
        }

        var folder = await _client.GetFolderAsync(folderName, ct);
        _folderCache[folderName] = folder;

        return folder;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(true);
            }
            catch { /* best-effort */ }
        }
        _client.Dispose();
    }
}
