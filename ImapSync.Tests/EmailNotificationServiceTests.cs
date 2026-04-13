using ImapSync.Core.Models;
using ImapSync.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImapSync.Tests;

public class EmailNotificationServiceTests
{
    [Fact]
    public async Task SendErrorReport_NoHostConfigured_DoesNotThrow()
    {
        var settings = new SmtpSettings(); // Host is empty
        var sut = new EmailNotificationService(settings, NullLogger<EmailNotificationService>.Instance);

        // Should complete gracefully without throwing
        await sut.SendErrorReportAsync("TestPair", ["Some error"]);
    }

    [Fact]
    public async Task SendErrorReport_NoToConfigured_DoesNotThrow()
    {
        var settings = new SmtpSettings { Host = "smtp.example.com", Port = 587 }; // To is empty
        var sut = new EmailNotificationService(settings, NullLogger<EmailNotificationService>.Instance);

        await sut.SendErrorReportAsync("TestPair", ["Some error"]);
    }

    [Fact]
    public async Task SendErrorReport_CancelledToken_DoesNotThrow()
    {
        var settings = new SmtpSettings(); // No host — will bail out before connecting
        var sut = new EmailNotificationService(settings, NullLogger<EmailNotificationService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await sut.SendErrorReportAsync("TestPair", ["Error"], cts.Token);
    }
}
