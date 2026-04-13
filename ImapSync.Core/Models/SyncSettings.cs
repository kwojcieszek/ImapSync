using System.ComponentModel.DataAnnotations;

namespace ImapSync.Core.Models;

public class SyncSettings
{
    public const string SectionName = "SyncSettings";

    [Required(ErrorMessage = "State file path is required")]
    public string StateFilePath { get; set; } = "sync-state.json";

    public List<MailboxPair> MailboxPairs { get; set; } = [];

    public SmtpSettings? ErrorNotification { get; set; }
}
