using MimeKit;

namespace ImapSync.Core.Interfaces;

public interface IImapConnection : IAsyncDisposable
{
    Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default);

    Task<bool> FolderExistsAsync(string folderName, CancellationToken cancellationToken = default);

    Task EnsureFolderExistsAsync(string folderName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string MessageId, uint Uid)>> GetAllMessageIdsAsync(string folderName, string accountName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string MessageId, uint Uid)>> GetMessagesSinceAsync(string folderName, string accountName, DateTime since, CancellationToken cancellationToken = default);

    Task<MimeMessage?> FetchMessageAsync(string folderName, uint uid, CancellationToken cancellationToken = default);

    Task<bool> MessageExistsAsync(string folderName, string messageId, CancellationToken cancellationToken = default);

    Task AppendMessageAsync(string folderName, MimeMessage message, CancellationToken cancellationToken = default);
}
