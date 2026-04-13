namespace ImapSync.Core.Interfaces;

public interface IEmailNotificationService
{
    Task SendErrorReportAsync(string pairName, IReadOnlyList<string> errors, CancellationToken cancellationToken = default);
}
