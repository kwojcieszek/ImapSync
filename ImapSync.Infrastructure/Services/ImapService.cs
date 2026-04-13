using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace ImapSync.Infrastructure.Services;

public class ImapService(ILogger<ImapConnection> connectionLogger) : IImapService
{
    public async Task<IImapConnection> OpenConnectionAsync(
        ImapCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var client = new ImapClient
        {
            Timeout = 30000,
            ServerCertificateValidationCallback = (s, c, h, e) => true
        };

        var sslOptions = credentials.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(credentials.Host, credentials.Port, sslOptions, cancellationToken);
        await client.AuthenticateAsync(credentials.Username, credentials.Password, cancellationToken);

        return new ImapConnection(client, credentials, sslOptions, connectionLogger);
    }
}
