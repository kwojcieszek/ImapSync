using System.Text;
using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ImapSync.Infrastructure.Services;

public class EmailNotificationService(SmtpSettings settings, ILogger<EmailNotificationService> logger) : IEmailNotificationService
{
    public async Task SendErrorReportAsync(string pairName, IReadOnlyList<string> errors, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.To))
        {
            logger.LogWarning("Error notification skipped — SMTP host or recipient not configured.");
            return;
        }

        var message = BuildMessage(pairName, errors);

        try
        {
            using var client = new SmtpClient();
            var sslOptions = settings.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            logger.LogDebug("Connecting to SMTP {Host}:{Port}", settings.Host, settings.Port);
            await client.ConnectAsync(settings.Host, settings.Port, sslOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Error notification sent to {To} for pair '{Pair}'", settings.To, pairName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send error notification email for pair '{Pair}'", pairName);
        }
    }

    private MimeMessage BuildMessage(string pairName, IReadOnlyList<string> errors)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(settings.From) ? settings.Username : settings.From));
        message.To.Add(MailboxAddress.Parse(settings.To));
        message.Subject = $"[ImapSync] Błędy synchronizacji: {pairName} — {DateTime.Now:yyyy-MM-dd HH:mm}";

        var body = new StringBuilder();
        body.AppendLine($"Podczas synchronizacji pary skrzynek <b>{pairName}</b> wystąpiły błędy:");
        body.AppendLine("<br><br>");
        body.AppendLine("<ul>");
        foreach (var error in errors)
        {
            body.AppendLine($"  <li>{System.Net.WebUtility.HtmlEncode(error)}</li>");
        }
        body.AppendLine("</ul>");
        body.AppendLine("<br>");
        body.AppendLine($"<small>Czas: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Aplikacja: ImapSync</small>");

        message.Body = new TextPart("html") { Text = body.ToString() };
        return message;
    }
}
