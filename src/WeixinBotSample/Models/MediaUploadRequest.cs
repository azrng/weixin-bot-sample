namespace WeixinBotSample.Models;

public sealed class MediaUploadRequest
{
    public string ExternalChatId { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public MediaMessageType MediaType { get; set; } = MediaMessageType.Image;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public int EncodeType { get; set; } = 7;

    public int PlayTimeMilliseconds { get; set; }

    public string Description { get; set; } = string.Empty;

    public MediaUploadRequest Clone()
    {
        return new MediaUploadRequest
        {
            ExternalChatId = ExternalChatId,
            ContextToken = ContextToken,
            MediaType = MediaType,
            FileName = FileName,
            ContentType = ContentType,
            EncodeType = EncodeType,
            PlayTimeMilliseconds = PlayTimeMilliseconds,
            Description = Description,
        };
    }
}
