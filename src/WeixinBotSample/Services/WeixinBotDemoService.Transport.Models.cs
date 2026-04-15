using System.Text.Json.Serialization;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
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
}
