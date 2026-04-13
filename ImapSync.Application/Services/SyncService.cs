using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ImapSync.Application.Services;

public class SyncService(
    IImapService imapService,
    ISyncStateService syncStateService,
    ISyncCacheService cache,
    ILogger<SyncService> logger) : ISyncService
{
    public async Task<SyncResult> SyncMailboxPairAsync(MailboxPair mailboxPair, CancellationToken cancellationToken = default)
    {
        var result = new SyncResult { MailboxPairName = mailboxPair.Name };

        if (mailboxPair.Destinations.Count == 0)
        {
            logger.LogWarning("Mailbox pair '{Name}' has no destinations configured, skipping.", mailboxPair.Name);
            return result;
        }

        cache.InvalidateIfDayChanged();

        var isInitialized = syncStateService.IsInitialized(mailboxPair.Name);
        var since = isInitialized ? syncStateService.GetLastSyncedAt(mailboxPair.Name) : null;

        var mode = isInitialized
            ? $"incremental (since {since:yyyy-MM-dd HH:mm} UTC)"
            : "FULL (initial sync)";

        var destNames = string.Join(", ", mailboxPair.Destinations.Select(d => d.Username));
        logger.LogInformation("Starting sync [{Mode}]: {Name} ({Source} -> [{Destinations}])",
            mode, mailboxPair.Name, mailboxPair.Source.Username, destNames);

        IImapConnection? sourceConn = null;
        var destConns = new IImapConnection?[mailboxPair.Destinations.Count];

        try
        {
            sourceConn = await imapService.OpenConnectionAsync(mailboxPair.Source, cancellationToken);

            for (var i = 0; i < mailboxPair.Destinations.Count; i++)
            {
                destConns[i] = await imapService.OpenConnectionAsync(mailboxPair.Destinations[i], cancellationToken);
            }

            var sourceFolders = await sourceConn.GetFoldersAsync(cancellationToken);
            var allFolders = new List<string>{ "INBOX" };
            allFolders.AddRange(sourceFolders);

            await SyncFoldersAsync(mailboxPair, destConns!, sourceFolders, result, cancellationToken);

            foreach (var folder in allFolders)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await SyncFolderAsync(mailboxPair, sourceConn, destConns!, folder, since, result, cancellationToken);
            }

            if (result.Errors.Count == 0)
            {
                if (!isInitialized)
                {
                    await syncStateService.MarkAsInitializedAsync(mailboxPair.Name, cancellationToken);
                }
                else
                {
                    await syncStateService.UpdateLastSyncedAtAsync(mailboxPair.Name, cancellationToken);
                }
            }
            else if (!isInitialized)
            {
                logger.LogWarning(
                    "Full sync for '{Name}' completed with {ErrorCount} error(s) — state NOT saved. Will retry full sync next cycle.",
                    mailboxPair.Name, result.Errors.Count);
            }
            else
            {
                logger.LogWarning(
                    "Incremental sync for '{Name}' completed with {ErrorCount} error(s) — last sync timestamp NOT updated.",
                    mailboxPair.Name, result.Errors.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error syncing mailbox pair {Name}", mailboxPair.Name);
            result.Errors.Add($"Fatal error: {ex.Message}");
        }
        finally
        {
            if (sourceConn is not null) { await sourceConn.DisposeAsync(); }
            foreach (var conn in destConns)
            {
                if (conn is not null) { await conn.DisposeAsync(); }
            }
        }

        logger.LogInformation(
            "Sync [{Mode}] completed for {Name}: checked={Checked}, copied={Copied}, skipped={Skipped}, foldersCreated={Folders}, errors={Errors}",
            mode, mailboxPair.Name, result.MessagesChecked, result.MessagesCopied, result.MessagesSkipped, result.FoldersCreated, result.Errors.Count);

        return result;
    }

    private async Task SyncFoldersAsync(
        MailboxPair mailboxPair,
        IImapConnection[] destConns,
        IReadOnlyList<string> sourceFolders,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        foreach (var folderName in sourceFolders)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            for (var i = 0; i < mailboxPair.Destinations.Count; i++)
            {
                var destination = mailboxPair.Destinations[i];
                var destConn = destConns[i];

                try
                {
                    var exists = await destConn.FolderExistsAsync(folderName, cancellationToken);

                    if (!exists)
                    {
                        logger.LogInformation("Creating folder {FolderName} on {Destination}", folderName, destination.Username);

                        await destConn.EnsureFolderExistsAsync(folderName, cancellationToken);

                        result.FoldersCreated++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error synchronizing folder {FolderName} to {Destination}", folderName, destination.Username);
                    result.Errors.Add($"Error creating folder {folderName} on {destination.Username}: {ex.Message}");
                }
            }
        }
    }

    private async Task SyncFolderAsync(
        MailboxPair mailboxPair,
        IImapConnection sourceConn,
        IImapConnection[] destConns,
        string folderName,
        DateTimeOffset? since,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Checking folder {Folder} [{Mode}]",
                folderName, since.HasValue ? $"since {since.Value:yyyy-MM-dd}" : "all");

            var messages = since.HasValue
                ? await sourceConn.GetMessagesSinceAsync(folderName, mailboxPair.Name, since.Value.UtcDateTime, cancellationToken)
                : await sourceConn.GetAllMessageIdsAsync(folderName, mailboxPair.Name, cancellationToken);

            if (messages.Count == 0)
            {
                logger.LogDebug("No messages in folder {Folder}", folderName);
                return;
            }

            logger.LogInformation("Found {Count} message(s) in folder {Folder}", messages.Count, folderName);

            result.MessagesChecked += messages.Count;

            foreach (var (messageId, uid) in messages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await SyncMessageToAllDestinationsAsync(
                    mailboxPair, sourceConn, destConns, folderName, messageId, uid, result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing folder {Folder}", folderName);
            result.Errors.Add($"Error processing folder {folderName}: {ex.Message}");
        }
    }

    private async Task SyncMessageToAllDestinationsAsync(
        MailboxPair mailboxPair,
        IImapConnection sourceConn,
        IImapConnection[] destConns,
        string folderName,
        string messageId,
        uint uid,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        var destinationsNeedingMessage = new List<int>();

        for (var i = 0; i < mailboxPair.Destinations.Count; i++)
        {
            var destination = mailboxPair.Destinations[i];
            var destConn = destConns[i];

            try
            {
                if (cache.IsKnownSynced(mailboxPair.Name, destination.Username, messageId))
                {
                    logger.LogDebug("Message {MessageId} (UID {Uid}) found in cache for {Destination}/{Folder}, skipping IMAP check",
                        messageId, uid, destination.Username, folderName);

                    result.MessagesSkipped++;

                    continue;
                }

#if DEBUG

                var watch = System.Diagnostics.Stopwatch.StartNew();
#endif
                var exists = await destConn.MessageExistsAsync(folderName, messageId, cancellationToken);
#if DEBUG 
                watch.Stop();

                logger.LogDebug("Time to check messages: {ElapsedMilliseconds} ms",
                    watch.ElapsedMilliseconds);
#endif

                if (exists)
                {
                    logger.LogDebug("Message {MessageId} (UID {Uid}) already exists on {Destination}/{Folder}, skipping",
                        messageId,uid, destination.Username, folderName);

                    cache.MarkAsSynced(mailboxPair.Name, destination.Username, messageId);

                    result.MessagesSkipped++;
                }
                else
                {
                    destinationsNeedingMessage.Add(i);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking message {MessageId} (UID {Uid}) on {Destination}/{Folder}",
                    messageId, uid, destination.Username, folderName);

                result.Errors.Add($"Error checking {messageId} on {destination.Username}/{folderName}: {ex.Message}");
            }
        }

        if (destinationsNeedingMessage.Count == 0)
        {
            return;
        }

        MimeMessage? message;
        try
        {
            message = await sourceConn.FetchMessageAsync(folderName, uid, cancellationToken);
            message?.MessageId = messageId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching message {MessageId} (UID {Uid}) from {Folder}", messageId, uid, folderName);
            result.Errors.Add($"Could not fetch message UUID:{uid} {messageId} from {folderName}: {ex.Message}");
            return;
        }

        if (message is null)
        {
            logger.LogWarning("Could not fetch message {MessageId} (UID {Uid}) from {Folder}", messageId, uid, folderName);
            result.Errors.Add($"Could not fetch message {messageId} from {folderName}");
            return;
        }

        foreach (var i in destinationsNeedingMessage)
        {
            var destination = mailboxPair.Destinations[i];
            var destConn = destConns[i];

            try
            {
                await destConn.EnsureFolderExistsAsync(folderName, cancellationToken);
                await destConn.AppendMessageAsync(folderName, message, cancellationToken);

                var confirmed = await destConn.MessageExistsAsync(folderName, messageId, cancellationToken);

                if (confirmed)
                {
                    logger.LogInformation("Copied and verified message {MessageId} to {Destination}/{Folder}",
                        messageId, destination.Username, folderName);

                    cache.MarkAsSynced(mailboxPair.Name, destination.Username, messageId);

                    result.MessagesCopied++;
                }
                else
                {
                    logger.LogError("Message {MessageId} appended to {Destination}/{Folder} but not found on server — will retry next cycle",
                        messageId, destination.Username, folderName);

                    result.Errors.Add($"Message {messageId} not confirmed on {destination.Username}/{folderName} after append");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error copying message {MessageId} to {Destination}/{Folder}",
                    messageId, destination.Username, folderName);

                result.Errors.Add($"Error copying {messageId} to {destination.Username}/{folderName}: {ex.Message}");
            }
        }
    }
}
