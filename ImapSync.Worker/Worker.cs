using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using Microsoft.Extensions.Options;

namespace ImapSync.Worker;

public class Worker(
    ISyncService syncService,
    IEmailNotificationService emailNotificationService,
    IOptions<SyncSettings> settings,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly SyncSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ImapSync Worker started. Mailbox pairs: {Count}",
            _settings.MailboxPairs.Count);

        if (_settings.ErrorNotification is not null)
        {
            logger.LogInformation("Error notifications enabled — recipient: {To}", _settings.ErrorNotification.To);
        }

        var validPairs = _settings.MailboxPairs
            .Where(p => !string.IsNullOrWhiteSpace(p.Source?.Host)
                     && p.Destinations.Count > 0
                     && p.Destinations.All(d => !string.IsNullOrWhiteSpace(d.Host)))
            .ToList();

        foreach (var pair in _settings.MailboxPairs.Except(validPairs))
        {
            logger.LogWarning("Mailbox pair '{Name}' has incomplete configuration, skipping.", pair.Name);
        }

        if (validPairs.Count == 0)
        {
            logger.LogWarning("No valid mailbox pairs configured. Worker will idle.");
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            return;
        }

        var pairLoops = validPairs.Select(pair => RunPairLoopAsync(pair, stoppingToken));

        await Task.WhenAll(pairLoops);

        logger.LogInformation("ImapSync Worker stopping.");
    }

    private async Task RunPairLoopAsync(MailboxPair pair, CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Pair '{Name}' loop started — interval: {Interval} min.",
            pair.Name, pair.IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncPairAsync(pair, stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(pair.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Pair '{Name}' loop stopped.", pair.Name);
    }

    private async Task SyncPairAsync(MailboxPair pair, CancellationToken cancellationToken)
    {
        try
        {
            var result = await syncService.SyncMailboxPairAsync(pair, cancellationToken);

            if (result.Success)
            {
                logger.LogInformation(
                    "[{Name}] Sync OK — checked: {Checked}, copied: {Copied}, skipped: {Skipped}, folders created: {Folders}",
                    result.MailboxPairName,
                    result.MessagesChecked,
                    result.MessagesCopied,
                    result.MessagesSkipped,
                    result.FoldersCreated);
            }
            else
            {
                logger.LogWarning(
                    "[{Name}] Sync completed with {ErrorCount} error(s): {Errors}",
                    result.MailboxPairName,
                    result.Errors.Count,
                    string.Join("; ", result.Errors));

                await emailNotificationService.SendErrorReportAsync(
                    result.MailboxPairName, result.Errors, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception syncing mailbox pair '{Name}'", pair.Name);

            await emailNotificationService.SendErrorReportAsync(
                pair.Name,
                [$"Unexpected error: {ex.Message}"],
                cancellationToken);
        }
    }
}
