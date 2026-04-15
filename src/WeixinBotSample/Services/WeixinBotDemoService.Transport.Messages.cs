using System.Text.Json.Serialization;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private static bool TryBuildInboundTextMessage(WeixinInboundMessageEnvelope message, out WeixinInboundTextMessage inboundMessage, out string skipReason)
    {
        inboundMessage = default!;
        skipReason = string.Empty;

        var textItem = message.ItemList.FirstOrDefault(item => item.Type == 1)?.TextItem?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(textItem))
        {
            skipReason = $"message_type={message.MessageType}，消息中没有文本内容";
            return false;
        }

        var messageId = message.MessageId?.ToString() ??
                        message.ClientId ??
                        Guid.NewGuid().ToString("N");
        var createdAt = message.CreateTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(message.CreateTimeMilliseconds)
            : DateTimeOffset.UtcNow;

        inboundMessage = new WeixinInboundTextMessage(
            ExternalChatId: message.FromUserId.Trim(),
            ExternalUserId: message.FromUserId.Trim(),
            SenderName: string.IsNullOrWhiteSpace(message.FromUserId) ? "微信用户" : message.FromUserId.Trim(),
            ChatName: string.IsNullOrWhiteSpace(message.FromUserId) ? "微信会话" : message.FromUserId.Trim(),
            MessageId: messageId,
            Text: textItem,
            ContextToken: message.ContextToken?.Trim() ?? string.Empty,
            CreatedAt: createdAt);
        return true;
    }

    private static bool TryBuildInboundMediaRecord(WeixinInboundMessageEnvelope message, out MediaTransferRecord record, out string skipReason)
    {
        record = default!;
        skipReason = string.Empty;

        var messageId = message.MessageId?.ToString() ??
                        message.ClientId ??
                        Guid.NewGuid().ToString("N");
        var createdAt = message.CreateTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(message.CreateTimeMilliseconds)
            : DateTimeOffset.UtcNow;

        var item = message.ItemList.FirstOrDefault(entry => entry.Type is >= 2 and <= 5);
        if (item is null)
        {
            skipReason = $"message_type={message.MessageType}，消息中没有可识别的媒体内容";
            return false;
        }

        record = new MediaTransferRecord
        {
            Id = messageId,
            MessageId = messageId,
            ClientId = message.ClientId?.Trim() ?? string.Empty,
            ExternalChatId = message.FromUserId.Trim(),
            ExternalUserId = message.FromUserId.Trim(),
            SenderName = string.IsNullOrWhiteSpace(message.FromUserId) ? "微信用户" : message.FromUserId.Trim(),
            ContextToken = message.ContextToken?.Trim() ?? string.Empty,
            Direction = "Inbound",
            CreatedAt = createdAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            TransferStatus = MediaTransferStatus.Received,
            StatusMessage = "已收到媒体消息，可执行下载解密。",
        };

        switch (item.Type)
        {
            case 2 when item.ImageItem is not null:
                record.MediaType = MediaMessageType.Image;
                record.FileName = "图片消息";
                record.Media = item.ImageItem.Media?.Trim() ?? string.Empty;
                record.ThumbMedia = item.ImageItem.ThumbMedia?.Trim() ?? string.Empty;
                record.AesKey = item.ImageItem.GetAesKey();
                record.DownloadParam = record.Media;
                record.FileSize = item.ImageItem.Length;
                record.Md5 = item.ImageItem.Md5?.Trim() ?? string.Empty;
                return true;
            case 3 when item.VoiceItem is not null:
                record.MediaType = MediaMessageType.Voice;
                record.FileName = "语音消息";
                record.Media = item.VoiceItem.Media?.Trim() ?? string.Empty;
                record.AesKey = item.VoiceItem.GetAesKey();
                record.DownloadParam = record.Media;
                record.FileSize = item.VoiceItem.Length;
                record.Md5 = item.VoiceItem.Md5?.Trim() ?? string.Empty;
                record.AsrText = item.VoiceItem.Text?.Trim() ?? string.Empty;
                record.EncodeType = item.VoiceItem.EncodeType;
                record.PlayTimeMilliseconds = item.VoiceItem.PlayTime;
                return true;
            case 4 when item.FileItem is not null:
                record.MediaType = MediaMessageType.File;
                record.FileName = string.IsNullOrWhiteSpace(item.FileItem.FileName) ? "文件消息" : item.FileItem.FileName.Trim();
                record.Media = item.FileItem.Media?.Trim() ?? string.Empty;
                record.AesKey = item.FileItem.GetAesKey();
                record.DownloadParam = record.Media;
                record.FileSize = item.FileItem.Length;
                record.Md5 = item.FileItem.Md5?.Trim() ?? string.Empty;
                return true;
            case 5 when item.VideoItem is not null:
                record.MediaType = MediaMessageType.Video;
                record.FileName = "视频消息";
                record.Media = item.VideoItem.Media?.Trim() ?? string.Empty;
                record.ThumbMedia = item.VideoItem.ThumbMedia?.Trim() ?? string.Empty;
                record.AesKey = item.VideoItem.GetAesKey();
                record.DownloadParam = record.Media;
                record.FileSize = item.VideoItem.Length;
                record.Md5 = item.VideoItem.Md5?.Trim() ?? string.Empty;
                record.VideoSize = item.VideoItem.VideoSize;
                return true;
            default:
                skipReason = $"消息包含媒体 item type={item.Type}，但当前结构未能解析";
                return false;
        }
    }

    private sealed record WeixinInboundTextMessage(
        string ExternalChatId,
        string ExternalUserId,
        string SenderName,
        string ChatName,
        string MessageId,
        string Text,
        string ContextToken,
        DateTimeOffset CreatedAt);

    private sealed class WeixinInboundMessageEnvelope
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("message_id")]
        public long? MessageId { get; set; }

        [JsonPropertyName("from_user_id")]
        public string FromUserId { get; set; } = string.Empty;

        [JsonPropertyName("message_type")]
        public int MessageType { get; set; }

        [JsonPropertyName("create_time_ms")]
        public long CreateTimeMilliseconds { get; set; }

        [JsonPropertyName("context_token")]
        public string? ContextToken { get; set; }

        [JsonPropertyName("item_list")]
        public List<WeixinMessageItem> ItemList { get; set; } = [];
    }

    private sealed class WeixinMessageItem
    {
        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("text_item")]
        public WeixinTextItem? TextItem { get; set; }

        [JsonPropertyName("image_item")]
        public WeixinImageItem? ImageItem { get; set; }

        [JsonPropertyName("voice_item")]
        public WeixinVoiceItem? VoiceItem { get; set; }

        [JsonPropertyName("file_item")]
        public WeixinFileItem? FileItem { get; set; }

        [JsonPropertyName("video_item")]
        public WeixinVideoItem? VideoItem { get; set; }
    }

    private sealed class WeixinTextItem
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private abstract class WeixinEncryptedMediaItem
    {
        [JsonPropertyName("media")]
        public string? Media { get; set; }

        [JsonPropertyName("aeskey")]
        public string? AesKey { get; set; }

        [JsonPropertyName("aes_key")]
        public string? AlternateAesKey { get; set; }

        [JsonPropertyName("md5")]
        public string? Md5 { get; set; }

        [JsonPropertyName("len")]
        public long Length { get; set; }

        public string GetAesKey()
        {
            return string.IsNullOrWhiteSpace(AesKey) ? AlternateAesKey?.Trim() ?? string.Empty : AesKey.Trim();
        }
    }

    private sealed class WeixinImageItem : WeixinEncryptedMediaItem
    {
        [JsonPropertyName("thumb_media")]
        public string? ThumbMedia { get; set; }
    }

    private sealed class WeixinVoiceItem : WeixinEncryptedMediaItem
    {
        [JsonPropertyName("encode_type")]
        public int EncodeType { get; set; }

        [JsonPropertyName("playtime")]
        public int PlayTime { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class WeixinFileItem : WeixinEncryptedMediaItem
    {
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private sealed class WeixinVideoItem : WeixinEncryptedMediaItem
    {
        [JsonPropertyName("thumb_media")]
        public string? ThumbMedia { get; set; }

        [JsonPropertyName("video_size")]
        public long VideoSize { get; set; }
    }
}
