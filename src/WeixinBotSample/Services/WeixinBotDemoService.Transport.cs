using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private static async Task<WeixinQrCodeResponse> FetchQrCodeAsync(HttpClient client, string baseUrl, string routeTag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{NormalizeBaseUrl(baseUrl)}/ilink/bot/get_bot_qrcode?bot_type={Uri.EscapeDataString(DefaultBotType)}");
        ApplyRouteTag(request.Headers, routeTag);
        using var response = await client.SendAsync(request, cancellationToken);
        var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<WeixinQrCodeResponse>(rawText, JsonOptions)
               ?? throw new InvalidOperationException("获取二维码返回为空。");
    }

    private static async Task<WeixinQrStatusResponse> PollQrStatusAsync(
        HttpClient client,
        string baseUrl,
        string qrCode,
        string routeTag,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(QrLongPollingTimeoutMilliseconds);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{NormalizeBaseUrl(baseUrl)}/ilink/bot/get_qrcode_status?qrcode={Uri.EscapeDataString(qrCode)}");
        request.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", "1");
        ApplyRouteTag(request.Headers, routeTag);

        try
        {
            using var response = await client.SendAsync(request, timeoutSource.Token);
            var rawText = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            response.EnsureSuccessStatusCode();
            return string.IsNullOrWhiteSpace(rawText)
                ? new WeixinQrStatusResponse { Status = "wait" }
                : JsonSerializer.Deserialize<WeixinQrStatusResponse>(rawText, JsonOptions) ?? new WeixinQrStatusResponse { Status = "wait" };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WeixinQrStatusResponse { Status = "wait" };
        }
    }

    private static async Task<string> NormalizeQrDisplayUrlAsync(HttpClient client, string qrUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrUrl))
        {
            return string.Empty;
        }

        if (qrUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return qrUrl;
        }

        if (!Uri.TryCreate(qrUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return CreateQrCodeDataUrl(qrUrl);
        }

        try
        {
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CreateQrCodeDataUrl(qrUrl);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return CreateQrCodeDataUrl(qrUrl);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return CreateQrCodeDataUrl(qrUrl);
        }
    }

    private static string CreateQrCodeDataUrl(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(payload.Trim(), QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        var bytes = qrCode.GetGraphic(20, drawQuietZones: true);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');

    private static void ApplyBotHeaders(HttpRequestMessage request, DemoConfiguration configuration)
    {
        request.Headers.TryAddWithoutValidation("AuthorizationType", "ilink_bot_token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
        request.Headers.TryAddWithoutValidation("X-WECHAT-UIN", BuildWechatUin());
        ApplyRouteTag(request.Headers, configuration.RouteTag);
    }

    private static void ApplyRouteTag(HttpRequestHeaders headers, string routeTag)
    {
        if (!string.IsNullOrWhiteSpace(routeTag))
        {
            headers.TryAddWithoutValidation("SKRouteTag", routeTag.Trim());
        }
    }

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

    private static string TruncateSingleLine(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}...";
    }

    private static (int? ErrorCode, string ErrorMessage) ParseApiError(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return (null, string.Empty);
        }

        try
        {
            var response = JsonSerializer.Deserialize<WeixinApiErrorResponse>(rawText, JsonOptions);
            var errorCode = response?.ErrorCode ?? response?.ReturnCode;
            var errorMessage = response?.ErrorMessage?.Trim() ?? string.Empty;
            return (errorCode, errorMessage);
        }
        catch
        {
            return (null, string.Empty);
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

    private sealed class WeixinPollingClient(HttpClient httpClient, DemoConfiguration configuration)
    {
        public async Task<GetUpdatesResult> GetUpdatesAsync(string syncBuffer, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/getupdates");
            var payload = JsonSerializer.Serialize(
                new
                {
                    get_updates_buf = syncBuffer ?? string.Empty,
                    base_info = new
                    {
                        channel_version = NormalizeChannelVersion(configuration.ChannelVersion),
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeoutMilliseconds);

            using var response = await httpClient.SendAsync(request, timeoutSource.Token);
            var rawText = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            response.EnsureSuccessStatusCode();

            var parsed = string.IsNullOrWhiteSpace(rawText)
                ? new GetUpdatesResponse()
                : JsonSerializer.Deserialize<GetUpdatesResponse>(rawText, JsonOptions) ?? new GetUpdatesResponse();

            var errorCode = parsed.ErrorCode ?? parsed.ReturnCode;
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(parsed.ErrorMessage)
                        ? $"微信 getupdates 返回异常：{errorCode}"
                        : parsed.ErrorMessage,
                    errorCode);
            }

            return new GetUpdatesResult(parsed, rawText, response.StatusCode);
        }

        public async Task<GetConfigResult> GetConfigAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/getconfig");
            var payload = JsonSerializer.Serialize(
                new
                {
                    ilink_user_id = configuration.UserId,
                    base_info = new
                    {
                        channel_version = NormalizeChannelVersion(configuration.ChannelVersion),
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"微信 getconfig 返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            var parsed = string.IsNullOrWhiteSpace(rawText)
                ? new GetConfigResponse()
                : JsonSerializer.Deserialize<GetConfigResponse>(rawText, JsonOptions) ?? new GetConfigResponse();
            var errorCode = parsed.ErrorCode ?? parsed.ReturnCode;
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(parsed.ErrorMessage)
                        ? $"微信 getconfig 返回异常：{errorCode}"
                        : parsed.ErrorMessage,
                    errorCode);
            }

            return new GetConfigResult(parsed.TypingTicket?.Trim() ?? string.Empty, rawText, response.StatusCode);
        }

        public async Task<GetUploadUrlResult> GetUploadUrlAsync(string fileKey, string md5, long length, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/getuploadurl");
            var payload = JsonSerializer.Serialize(
                new
                {
                    filekey = fileKey,
                    md5,
                    len = length,
                    base_info = new
                    {
                        channel_version = NormalizeChannelVersion(configuration.ChannelVersion),
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"微信 getuploadurl 返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            var parsed = string.IsNullOrWhiteSpace(rawText)
                ? new GetUploadUrlResponse()
                : JsonSerializer.Deserialize<GetUploadUrlResponse>(rawText, JsonOptions) ?? new GetUploadUrlResponse();
            var errorCode = parsed.ErrorCode ?? parsed.ReturnCode;
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(parsed.ErrorMessage)
                        ? $"微信 getuploadurl 返回异常：{errorCode}"
                        : parsed.ErrorMessage,
                    errorCode,
                    payload,
                    rawText);
            }

            if (string.IsNullOrWhiteSpace(parsed.UploadParam))
            {
                throw new InvalidOperationException("getuploadurl 未返回 upload_param。");
            }

            return new GetUploadUrlResult(parsed.UploadParam.Trim(), payload, rawText, response.StatusCode);
        }

        public async Task<UploadMediaResult> UploadEncryptedMediaAsync(
            string uploadParam,
            string fileKey,
            byte[] encryptedBytes,
            string contentType,
            CancellationToken cancellationToken)
        {
            var uploadUrl = $"https://novac2c.cdn.weixin.qq.com/c2c/upload?encrypted_query_param={Uri.EscapeDataString(uploadParam)}&filekey={Uri.EscapeDataString(fileKey)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            request.Content = new ByteArrayContent(encryptedBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"CDN 上传返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            if (!response.Headers.TryGetValues("x-encrypted-param", out var values))
            {
                throw new InvalidOperationException("CDN 上传成功，但响应头缺少 x-encrypted-param。");
            }

            var downloadParam = values.FirstOrDefault()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadParam))
            {
                throw new InvalidOperationException("CDN 上传成功，但 download_param 为空。");
            }

            return new UploadMediaResult(downloadParam, uploadUrl, encryptedBytes.LongLength, contentType, rawText, response.StatusCode);
        }

        public async Task<byte[]> DownloadEncryptedMediaAsync(string downloadParam, CancellationToken cancellationToken)
        {
            var downloadUrl = $"https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param={Uri.EscapeDataString(downloadParam)}";
            using var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"CDN 下载返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        public async Task SendTypingAsync(string ilinkUserId, string typingTicket, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/sendtyping");
            var payload = JsonSerializer.Serialize(
                new
                {
                    ilink_user_id = ilinkUserId,
                    typing_ticket = typingTicket,
                    status = 1,
                    base_info = new
                    {
                        channel_version = NormalizeChannelVersion(configuration.ChannelVersion),
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"微信 sendtyping 返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            var (errorCode, errorMessage) = ParseApiError(rawText);
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? $"微信 sendtyping 返回异常：{errorCode}"
                        : errorMessage,
                    errorCode);
            }
        }

        public async Task<SendMessageResult> SendTextMessageAsync(
            string toUserId,
            string contextToken,
            string text,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/sendmessage");
            var clientId = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..32];
            var payload = JsonSerializer.Serialize(
                new
                {
                    msg = new
                    {
                        from_user_id = string.Empty,
                        to_user_id = toUserId,
                        client_id = clientId,
                        message_type = 2,
                        message_state = 2,
                        item_list = new[]
                        {
                            new
                            {
                                type = 1,
                                text_item = new
                                {
                                    text,
                                },
                            },
                        },
                        context_token = contextToken,
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"微信 sendmessage 返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            var (errorCode, errorMessage) = ParseApiError(rawText);
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? $"微信 sendmessage 返回异常：{errorCode}"
                        : errorMessage,
                    errorCode);
            }

            return new SendMessageResult(clientId, payload, rawText, response.StatusCode);
        }

        public async Task<SendMessageResult> SendMediaMessageAsync(
            string toUserId,
            string contextToken,
            object mediaItem,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(configuration.BaseUrl)}/ilink/bot/sendmessage");
            var clientId = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..32];
            var payload = JsonSerializer.Serialize(
                new
                {
                    msg = new
                    {
                        from_user_id = string.Empty,
                        to_user_id = toUserId,
                        client_id = clientId,
                        message_type = 2,
                        message_state = 2,
                        item_list = new[] { mediaItem },
                        context_token = contextToken,
                    },
                },
                JsonOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ApplyBotHeaders(request, configuration);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"微信 sendmessage 返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            var (errorCode, errorMessage) = ParseApiError(rawText);
            if ((errorCode ?? 0) != 0)
            {
                throw new WeixinApiException(
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? $"微信 sendmessage 返回异常：{errorCode}"
                        : errorMessage,
                    errorCode);
            }

            return new SendMessageResult(clientId, payload, rawText, response.StatusCode);
        }

    }

    private static string BuildWechatUin()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var value = BitConverter.ToUInt32(buffer);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString()));
    }

    private sealed class WeixinQrCodeResponse
    {
        [JsonPropertyName("qrcode")]
        public string QrCode { get; set; } = string.Empty;

        [JsonPropertyName("qrcode_img_content")]
        public string QrCodeImageContent { get; set; } = string.Empty;
    }

    private sealed class WeixinQrStatusResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "wait";

        [JsonPropertyName("bot_token")]
        public string? Token { get; set; }

        [JsonPropertyName("ilink_bot_id")]
        public string? AccountId { get; set; }

        [JsonPropertyName("baseurl")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("ilink_user_id")]
        public string? UserId { get; set; }
    }

    private sealed class GetUpdatesResponse
    {
        [JsonPropertyName("ret")]
        public int? ReturnCode { get; set; }

        [JsonPropertyName("errcode")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("errmsg")]
        public string ErrorMessage { get; set; } = string.Empty;

        [JsonPropertyName("msgs")]
        public List<WeixinInboundMessageEnvelope> Messages { get; set; } = [];

        [JsonPropertyName("get_updates_buf")]
        public string GetUpdatesBuffer { get; set; } = string.Empty;

        [JsonPropertyName("longpolling_timeout_ms")]
        public int LongPollingTimeoutMilliseconds { get; set; }
    }

    private sealed record GetUpdatesResult(GetUpdatesResponse Response, string RawText, System.Net.HttpStatusCode StatusCode);

    private sealed record GetConfigResult(string TypingTicket, string RawText, System.Net.HttpStatusCode StatusCode);

    private sealed record GetUploadUrlResult(string UploadParam, string RequestPayload, string RawText, System.Net.HttpStatusCode StatusCode);

    private sealed record UploadMediaResult(string DownloadParam, string UploadUrl, long UploadedLength, string ContentType, string RawText, System.Net.HttpStatusCode StatusCode);

    private sealed record SendMessageResult(string ClientId, string RequestPayload, string RawText, System.Net.HttpStatusCode StatusCode);

    private class WeixinApiErrorResponse
    {
        [JsonPropertyName("ret")]
        public int? ReturnCode { get; set; }

        [JsonPropertyName("errcode")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("errmsg")]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    private sealed class GetConfigResponse : WeixinApiErrorResponse
    {
        [JsonPropertyName("typing_ticket")]
        public string? TypingTicket { get; set; }
    }

    private sealed class GetUploadUrlResponse : WeixinApiErrorResponse
    {
        [JsonPropertyName("upload_param")]
        public string UploadParam { get; set; } = string.Empty;
    }

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
