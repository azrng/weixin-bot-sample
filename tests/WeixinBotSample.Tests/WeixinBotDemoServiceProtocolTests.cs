using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Tests;

public sealed class WeixinBotDemoServiceProtocolTests
{
    private static readonly Type ServiceType = typeof(WeixinBotDemoService);
    private static readonly Type PollingClientType = ServiceType.GetNestedType("WeixinPollingClient", BindingFlags.NonPublic)!;

    [Fact]
    public async Task GetUpdatesAsync_UsesPerRequestWechatUin_AndCarriesChannelVersion()
    {
        var handler = new RecordingHandler();
        handler.Enqueue("""{"ret":0,"msgs":[],"get_updates_buf":"buf-1","longpolling_timeout_ms":35000}""");
        handler.Enqueue("""{"ret":0,"msgs":[],"get_updates_buf":"buf-2","longpolling_timeout_ms":35000}""");
        var client = CreatePollingClient(handler, CreateConfiguration());

        await InvokeAsync(client, "GetUpdatesAsync", "buf-0", 5000, CancellationToken.None);
        await InvokeAsync(client, "GetUpdatesAsync", "buf-1", 5000, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);

        var first = handler.Requests[0];
        var second = handler.Requests[1];

        Assert.Equal("ilink_bot_token", first.GetHeader("AuthorizationType"));
        Assert.Equal("Bearer test-token", first.GetHeader("Authorization"));
        Assert.Equal("route-demo", first.GetHeader("SKRouteTag"));
        Assert.NotEmpty(first.GetHeader("X-WECHAT-UIN"));
        Assert.NotEqual(first.GetHeader("X-WECHAT-UIN"), second.GetHeader("X-WECHAT-UIN"));

        using var firstJson = JsonDocument.Parse(first.Content);
        Assert.Equal("buf-0", firstJson.RootElement.GetProperty("get_updates_buf").GetString());
        Assert.Equal("1.0.3", firstJson.RootElement.GetProperty("base_info").GetProperty("channel_version").GetString());
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsTypingTicket_AndCarriesChannelVersion()
    {
        var handler = new RecordingHandler();
        handler.Enqueue("""{"errcode":0,"typing_ticket":"typing-ticket-001"}""");
        var client = CreatePollingClient(handler, CreateConfiguration());

        var result = await InvokeAsync(client, "GetConfigAsync", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("typing-ticket-001", result!.GetType().GetProperty("TypingTicket")!.GetValue(result));
        Assert.Single(handler.Requests);

        var request = handler.Requests[0];
        using var json = JsonDocument.Parse(request.Content);
        Assert.Equal("bot-user@im.bot", json.RootElement.GetProperty("ilink_user_id").GetString());
        Assert.Equal("1.0.3", json.RootElement.GetProperty("base_info").GetProperty("channel_version").GetString());
    }

    [Fact]
    public async Task SendTextMessageAsync_WhenApiReturnsSessionExpired_ThrowsWeixinApiException()
    {
        var handler = new RecordingHandler();
        handler.Enqueue("""{"errcode":-14,"errmsg":"session expired"}""");
        var client = CreatePollingClient(handler, CreateConfiguration());

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await InvokeAsync(client, "SendTextMessageAsync", "wx-user-001", "ctx-001", "你好", CancellationToken.None));

        Assert.Equal("WeixinApiException", exception.GetType().Name);
        Assert.Equal(-14, exception.GetType().GetProperty("ErrorCode")!.GetValue(exception));

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        using var json = JsonDocument.Parse(request.Content);
        var msg = json.RootElement.GetProperty("msg");
        Assert.Equal("wx-user-001", msg.GetProperty("to_user_id").GetString());
        Assert.Equal("ctx-001", msg.GetProperty("context_token").GetString());
        Assert.Equal(2, msg.GetProperty("message_type").GetInt32());
        Assert.Equal(2, msg.GetProperty("message_state").GetInt32());
        Assert.Equal("你好", msg.GetProperty("item_list")[0].GetProperty("text_item").GetProperty("text").GetString());
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
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed record CapturedRequest(
        string Method,
        string Url,
        IReadOnlyDictionary<string, string> Headers,
        string Content)
    {
        public string GetHeader(string name) => Headers.TryGetValue(name, out var value) ? value : string.Empty;
    }
}
