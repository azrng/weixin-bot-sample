using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private sealed class WeixinPollingClient(HttpClient httpClient, Models.DemoConfiguration configuration)
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
            Stream encryptedStream,
            long encryptedLength,
            string contentType,
            CancellationToken cancellationToken)
        {
            var uploadUrl = $"https://novac2c.cdn.weixin.qq.com/c2c/upload?encrypted_query_param={Uri.EscapeDataString(uploadParam)}&filekey={Uri.EscapeDataString(fileKey)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            request.Content = new StreamContent(encryptedStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            request.Content.Headers.ContentLength = encryptedLength;

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

            return new UploadMediaResult(downloadParam, uploadUrl, encryptedLength, contentType, rawText, response.StatusCode);
        }

        public async Task DownloadEncryptedMediaAsync(string downloadParam, Stream destination, CancellationToken cancellationToken)
        {
            var downloadUrl = $"https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param={Uri.EscapeDataString(downloadParam)}";
            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var rawText = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(rawText)
                    ? $"CDN 下载返回状态码 {(int)response.StatusCode}"
                    : rawText);
            }

            await using var encryptedStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await encryptedStream.CopyToAsync(destination, cancellationToken);
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
}
