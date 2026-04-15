using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    private static string BuildWechatUin()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var value = BitConverter.ToUInt32(buffer);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString()));
    }
}
