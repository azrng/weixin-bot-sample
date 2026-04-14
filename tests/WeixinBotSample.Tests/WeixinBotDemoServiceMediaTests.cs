using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Tests;

public sealed class WeixinBotDemoServiceMediaTests
{
    private static readonly Type ServiceType = typeof(WeixinBotDemoService);
    private static readonly Type PollingClientType = ServiceType.GetNestedType("WeixinPollingClient", BindingFlags.NonPublic)!;
    private static readonly Type EnvelopeType = ServiceType.GetNestedType("WeixinInboundMessageEnvelope", BindingFlags.NonPublic)!;
    private static readonly Type ItemType = ServiceType.GetNestedType("WeixinMessageItem", BindingFlags.NonPublic)!;
    private static readonly Type ImageItemType = ServiceType.GetNestedType("WeixinImageItem", BindingFlags.NonPublic)!;
    private static readonly Type VoiceItemType = ServiceType.GetNestedType("WeixinVoiceItem", BindingFlags.NonPublic)!;
    private static readonly MethodInfo EncryptMethod = ServiceType.GetMethod("EncryptAesEcb", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo DecryptMethod = ServiceType.GetMethod("DecryptAesEcb", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo TryBuildInboundMediaRecordMethod = ServiceType.GetMethod("TryBuildInboundMediaRecord", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo BuildMediaPayloadMethod = ServiceType.GetMethod("BuildMediaPayload", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void EncryptAesEcb_AndDecryptAesEcb_ShouldRoundTrip()
    {
        var plainBytes = Encoding.UTF8.GetBytes("demo-media-payload");
        var key = Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray();

        var encrypted = (byte[])EncryptMethod.Invoke(null, [plainBytes, key])!;
        var decrypted = (byte[])DecryptMethod.Invoke(null, [encrypted, key])!;

        Assert.NotEqual(Convert.ToBase64String(plainBytes), Convert.ToBase64String(encrypted));
        Assert.Equal(plainBytes, decrypted);
    }

    [Fact]
    public void EncryptAesEcb_WhenPayloadIsNotBlockAligned_ShouldProducePaddedCipherLength()
    {
        var plainBytes = Encoding.UTF8.GetBytes("1234567890ABCDEFG");
        var key = Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray();

        var encrypted = (byte[])EncryptMethod.Invoke(null, [plainBytes, key])!;

        Assert.Equal(17, plainBytes.Length);
        Assert.Equal(32, encrypted.Length);
    }

    [Fact]
    public async Task SendMediaMessageAsync_ShouldBuildMediaPayload()
    {
        var handler = new RecordingHandler();
        handler.Enqueue("""{"errcode":0}""");
        var client = CreatePollingClient(handler, CreateConfiguration());
        var mediaItem = new
        {
            type = 4,
            file_item = new
            {
                media = "download-param-001",
                aeskey = "aes-key-001",
                aes_key = "aes-key-001",
                file_name = "demo.txt",
                md5 = "abc123",
                len = 128,
            },
        };

        await InvokeAsync(client, "SendMediaMessageAsync", "wx-user-001", "ctx-001", mediaItem, CancellationToken.None);

        Assert.Single(handler.Requests);
        using var json = JsonDocument.Parse(handler.Requests[0].Content);
        var item = json.RootElement.GetProperty("msg").GetProperty("item_list")[0];
        Assert.Equal(4, item.GetProperty("type").GetInt32());
        Assert.Equal("download-param-001", item.GetProperty("file_item").GetProperty("media").GetString());
        Assert.Equal("aes-key-001", item.GetProperty("file_item").GetProperty("aeskey").GetString());
        Assert.Equal("aes-key-001", item.GetProperty("file_item").GetProperty("aes_key").GetString());
        Assert.Equal("demo.txt", item.GetProperty("file_item").GetProperty("file_name").GetString());
    }

    [Fact]
    public async Task GetUploadUrlAsync_ShouldSendExpectedPayloadAndHeaders()
    {
        var handler = new RecordingHandler();
        handler.Enqueue("""{"errcode":0,"upload_param":"upload-param-001"}""");
        var client = CreatePollingClient(handler, CreateConfiguration());

        await InvokeAsync(client, "GetUploadUrlAsync", "demo-file-key", "demo-md5", 2048L, CancellationToken.None);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.EndsWith("/ilink/bot/getuploadurl", request.Url, StringComparison.Ordinal);
        Assert.Equal("ilink_bot_token", request.Headers["AuthorizationType"]);
        Assert.Equal("route-demo", request.Headers["SKRouteTag"]);
        Assert.StartsWith("Bearer ", request.Headers["Authorization"], StringComparison.Ordinal);

        using var json = JsonDocument.Parse(request.Content);
        Assert.Equal("demo-file-key", json.RootElement.GetProperty("filekey").GetString());
        Assert.Equal("demo-md5", json.RootElement.GetProperty("md5").GetString());
        Assert.Equal(2048L, json.RootElement.GetProperty("len").GetInt64());
        Assert.Equal("1.0.3", json.RootElement.GetProperty("base_info").GetProperty("channel_version").GetString());
    }

    [Fact]
    public void BuildMediaPayload_ShouldUseEncryptedLengthAndBothAesKeyAliases()
    {
        var request = new MediaUploadRequest
        {
            MediaType = MediaMessageType.File,
            FileName = "demo.txt",
        };
        var record = new MediaTransferRecord
        {
            Media = "download-param-001",
            AesKey = "aes-key-001",
            Md5 = "abc123",
            FileName = "demo.txt",
            FileSize = 17,
            EncryptedFileSize = 32,
        };

        var payload = BuildMediaPayloadMethod.Invoke(null, [request, record])!;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var fileItem = json.RootElement.GetProperty("file_item");

        Assert.Equal(32, fileItem.GetProperty("len").GetInt32());
        Assert.Equal("aes-key-001", fileItem.GetProperty("aeskey").GetString());
        Assert.Equal("aes-key-001", fileItem.GetProperty("aes_key").GetString());
    }

    [Fact]
    public void TryBuildInboundMediaRecord_WhenVoiceContainsAsrText_ShouldCaptureVoiceMetadata()
    {
        var envelope = Activator.CreateInstance(EnvelopeType)!;
        EnvelopeType.GetProperty("ClientId")!.SetValue(envelope, "client-media-001");
        EnvelopeType.GetProperty("MessageId")!.SetValue(envelope, 20001L);
        EnvelopeType.GetProperty("FromUserId")!.SetValue(envelope, "wx-user-voice");
        EnvelopeType.GetProperty("MessageType")!.SetValue(envelope, 3);
        EnvelopeType.GetProperty("CreateTimeMilliseconds")!.SetValue(envelope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        EnvelopeType.GetProperty("ContextToken")!.SetValue(envelope, "ctx-voice-001");
        EnvelopeType.GetProperty("ItemList")!.SetValue(envelope, CreateVoiceItemList());

        var parameters = new object?[] { envelope, null, string.Empty };
        var succeeded = (bool)TryBuildInboundMediaRecordMethod.Invoke(null, parameters)!;

        Assert.True(succeeded);
        Assert.Equal(string.Empty, parameters[2]);
        var record = Assert.IsType<MediaTransferRecord>(parameters[1]);
        Assert.Equal(MediaMessageType.Voice, record.MediaType);
        Assert.Equal("协议已转写的语音文本", record.AsrText);
        Assert.Equal("voice-media-001", record.Media);
        Assert.Equal("voice-aes-001", record.AesKey);
    }

    [Fact]
    public void TryBuildInboundMediaRecord_WhenImageUsesAlternateAesKey_ShouldCaptureThumbAndAesKey()
    {
        var envelope = Activator.CreateInstance(EnvelopeType)!;
        EnvelopeType.GetProperty("ClientId")!.SetValue(envelope, "client-image-001");
        EnvelopeType.GetProperty("MessageId")!.SetValue(envelope, 30001L);
        EnvelopeType.GetProperty("FromUserId")!.SetValue(envelope, "wx-user-image");
        EnvelopeType.GetProperty("MessageType")!.SetValue(envelope, 3);
        EnvelopeType.GetProperty("CreateTimeMilliseconds")!.SetValue(envelope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        EnvelopeType.GetProperty("ContextToken")!.SetValue(envelope, "ctx-image-001");
        EnvelopeType.GetProperty("ItemList")!.SetValue(envelope, CreateImageItemList());

        var parameters = new object?[] { envelope, null, string.Empty };
        var succeeded = (bool)TryBuildInboundMediaRecordMethod.Invoke(null, parameters)!;

        Assert.True(succeeded);
        var record = Assert.IsType<MediaTransferRecord>(parameters[1]);
        Assert.Equal(MediaMessageType.Image, record.MediaType);
        Assert.Equal("image-media-001", record.Media);
        Assert.Equal("image-thumb-001", record.ThumbMedia);
        Assert.Equal("image-alt-aes-001", record.AesKey);
        Assert.Equal("image-md5-001", record.Md5);
    }

    private static DemoConfiguration CreateConfiguration()
    {
        return new DemoConfiguration
        {
            BaseUrl = "https://ilinkai.weixin.qq.com",
            ChannelVersion = "1.0.3",
            RouteTag = "route-demo",
            Token = "test-token",
            UserId = "bot-user@im.bot",
        };
    }

    private static object CreatePollingClient(HttpMessageHandler handler, DemoConfiguration configuration)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ilinkai.weixin.qq.com"),
        };

        return Activator.CreateInstance(
                   PollingClientType,
                   BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                   binder: null,
                   args: [httpClient, configuration],
                   culture: null)
               ?? throw new InvalidOperationException("Failed to create WeixinPollingClient.");
    }

    private static async Task<object?> InvokeAsync(object target, string methodName, params object?[] args)
    {
        var method = PollingClientType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task)method.Invoke(target, args)!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static object CreateVoiceItemList()
    {
        var itemList = Activator.CreateInstance(typeof(List<>).MakeGenericType(ItemType))!;
        var voiceItem = Activator.CreateInstance(VoiceItemType)!;
        VoiceItemType.GetProperty("Media")!.SetValue(voiceItem, "voice-media-001");
        VoiceItemType.GetProperty("AesKey")!.SetValue(voiceItem, "voice-aes-001");
        VoiceItemType.GetProperty("Md5")!.SetValue(voiceItem, "voice-md5-001");
        VoiceItemType.GetProperty("Length")!.SetValue(voiceItem, 2048L);
        VoiceItemType.GetProperty("EncodeType")!.SetValue(voiceItem, 7);
        VoiceItemType.GetProperty("PlayTime")!.SetValue(voiceItem, 3500);
        VoiceItemType.GetProperty("Text")!.SetValue(voiceItem, "协议已转写的语音文本");

        var item = Activator.CreateInstance(ItemType)!;
        ItemType.GetProperty("Type")!.SetValue(item, 3);
        ItemType.GetProperty("VoiceItem")!.SetValue(item, voiceItem);
        itemList.GetType().GetMethod("Add")!.Invoke(itemList, [item]);
        return itemList;
    }

    private static object CreateImageItemList()
    {
        var itemList = Activator.CreateInstance(typeof(List<>).MakeGenericType(ItemType))!;
        var imageItem = Activator.CreateInstance(ImageItemType)!;
        ImageItemType.GetProperty("Media")!.SetValue(imageItem, "image-media-001");
        ImageItemType.GetProperty("AlternateAesKey")!.SetValue(imageItem, "image-alt-aes-001");
        ImageItemType.GetProperty("Md5")!.SetValue(imageItem, "image-md5-001");
        ImageItemType.GetProperty("Length")!.SetValue(imageItem, 4096L);
        ImageItemType.GetProperty("ThumbMedia")!.SetValue(imageItem, "image-thumb-001");

        var item = Activator.CreateInstance(ItemType)!;
        ItemType.GetProperty("Type")!.SetValue(item, 2);
        ItemType.GetProperty("ImageItem")!.SetValue(item, imageItem);
        itemList.GetType().GetMethod("Add")!.Invoke(itemList, [item]);
        return itemList;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                static header => header.Key,
                static header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                headers));

            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed record CapturedRequest(string Method, string Url, string Content, IReadOnlyDictionary<string, string> Headers);
}
