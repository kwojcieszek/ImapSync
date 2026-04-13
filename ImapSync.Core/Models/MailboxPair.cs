using System.ComponentModel.DataAnnotations;

namespace ImapSync.Core.Models;

public class MailboxPair
{
    [Required(ErrorMessage = "Mailbox pair name is required")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "IntervalMinutes must be at least 1")]
    public int IntervalMinutes { get; set; } = 5;

    [Required(ErrorMessage = "Source credentials are required")]
    public ImapCredentials Source { get; set; } = new();

    [Required(ErrorMessage = "At least one destination is required")]
    [MinLength(1, ErrorMessage = "At least one destination is required")]
    public List<ImapCredentials> Destinations { get; set; } = [];
}
