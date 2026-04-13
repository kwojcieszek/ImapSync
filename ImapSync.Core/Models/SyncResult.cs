namespace ImapSync.Core.Models;

public class SyncResult
{
    public string MailboxPairName { get; set; } = string.Empty;
    public int MessagesChecked { get; set; }
    public int MessagesCopied { get; set; }
    public int MessagesSkipped { get; set; }
    public int FoldersCreated { get; set; }
    public List<string> Errors { get; set; } = [];
    public bool Success => Errors.Count == 0;
}
