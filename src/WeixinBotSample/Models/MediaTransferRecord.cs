namespace WeixinBotSample.Models;

public sealed class MediaTransferRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string MessageId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ExternalChatId { get; set; } = string.Empty;

    public string ExternalUserId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public MediaMessageType MediaType { get; set; } = MediaMessageType.Image;

    public string Direction { get; set; } = "Outbound";

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public long EncryptedFileSize { get; set; }

    public string Md5 { get; set; } = string.Empty;

    public string FileKey { get; set; } = string.Empty;

    public string Media { get; set; } = string.Empty;

    public string ThumbMedia { get; set; } = string.Empty;

    public string AesKey { get; set; } = string.Empty;

    public string DownloadParam { get; set; } = string.Empty;

    public string AsrText { get; set; } = string.Empty;

    public int EncodeType { get; set; }

    public int PlayTimeMilliseconds { get; set; }

    public long VideoSize { get; set; }

    public string LocalCachePath { get; set; } = string.Empty;

    public string TraceFilePath { get; set; } = string.Empty;

    public MediaTransferStatus TransferStatus { get; set; } = MediaTransferStatus.Pending;

    public string StatusMessage { get; set; } = string.Empty;

    public string ResponseSummary { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public MediaTransferRecord Clone()
    {
        return new MediaTransferRecord
        {
            Id = Id,
            MessageId = MessageId,
            ClientId = ClientId,
            ExternalChatId = ExternalChatId,
            ExternalUserId = ExternalUserId,
            SenderName = SenderName,
            ContextToken = ContextToken,
            MediaType = MediaType,
            Direction = Direction,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            EncryptedFileSize = EncryptedFileSize,
            Md5 = Md5,
            FileKey = FileKey,
            Media = Media,
            ThumbMedia = ThumbMedia,
            AesKey = AesKey,
            DownloadParam = DownloadParam,
            AsrText = AsrText,
            EncodeType = EncodeType,
            PlayTimeMilliseconds = PlayTimeMilliseconds,
            VideoSize = VideoSize,
            LocalCachePath = LocalCachePath,
            TraceFilePath = TraceFilePath,
            TransferStatus = TransferStatus,
            StatusMessage = StatusMessage,
            ResponseSummary = ResponseSummary,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }
}
