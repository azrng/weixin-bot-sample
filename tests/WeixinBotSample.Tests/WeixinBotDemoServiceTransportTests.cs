using System.Reflection;
using WeixinBotSample.Services;

namespace WeixinBotSample.Tests;

public sealed class WeixinBotDemoServiceTransportTests
{
    private static readonly Type ServiceType = typeof(WeixinBotDemoService);
    private static readonly Type EnvelopeType = ServiceType.GetNestedType("WeixinInboundMessageEnvelope", BindingFlags.NonPublic)!;
    private static readonly Type ItemType = ServiceType.GetNestedType("WeixinMessageItem", BindingFlags.NonPublic)!;
    private static readonly Type TextItemType = ServiceType.GetNestedType("WeixinTextItem", BindingFlags.NonPublic)!;
    private static readonly MethodInfo TryBuildInboundTextMessageMethod = ServiceType.GetMethod("TryBuildInboundTextMessage", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo IsTypingTicketUnsupportedMethod = ServiceType.GetMethod("IsTypingTicketUnsupported", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ShouldResetConversationContextMethod = ServiceType.GetMethod("ShouldResetConversationContext", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void TryBuildInboundTextMessage_WhenWeChatUsesMessageTypeOne_StillBuildsReplyContext()
    {
        var envelope = CreateEnvelope(messageType: 1, text: "你好，机器人", contextToken: "ctx-demo-001");

        var parameters = new object?[] { envelope, null, string.Empty };

        var succeeded = (bool)TryBuildInboundTextMessageMethod.Invoke(null, parameters)!;

        Assert.True(succeeded);
        Assert.Equal(string.Empty, parameters[2]);

        Assert.NotNull(parameters[1]);
        var inboundMessage = parameters[1]!;
        var inboundType = inboundMessage.GetType();
        Assert.Equal("wx-user-001", inboundType.GetProperty("ExternalChatId")!.GetValue(inboundMessage));
        Assert.Equal("ctx-demo-001", inboundType.GetProperty("ContextToken")!.GetValue(inboundMessage));
        Assert.Equal("你好，机器人", inboundType.GetProperty("Text")!.GetValue(inboundMessage));
    }

    [Fact]
    public void TryBuildInboundTextMessage_WhenMessageDoesNotContainText_SkipsMessage()
    {
        var envelope = CreateEnvelope(messageType: 3, text: string.Empty, contextToken: "ctx-demo-002");

        var parameters = new object?[] { envelope, null, string.Empty };

        var succeeded = (bool)TryBuildInboundTextMessageMethod.Invoke(null, parameters)!;

        Assert.False(succeeded);
        Assert.Null(parameters[1]);
        Assert.Equal("message_type=3，消息中没有文本内容", parameters[2]);
    }

    [Theory]
    [InlineData("GetTypingTicket rpc failed", true)]
    [InlineData("{\"errmsg\":\"GetTypingTicket rpc failed\"}", true)]
    [InlineData("微信 getconfig 返回异常：-14", false)]
    [InlineData("", false)]
    public void IsTypingTicketUnsupported_ShouldDetectKnownTypingFallbackMessage(string message, bool expected)
    {
        var actual = (bool)IsTypingTicketUnsupportedMethod.Invoke(null, [message])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldResetConversationContext_WhenBindingSameAccount_ShouldKeepCachedContext()
    {
        var previous = new Models.DemoConfiguration
        {
            AccountId = "bot-account-001",
            UserId = "bot-user-001@im.bot",
            LastExternalChatId = "wx-user-001",
            LastContextToken = "ctx-001",
        };
        var current = new Models.DemoConfiguration
        {
            AccountId = "bot-account-001",
            UserId = "bot-user-001@im.bot",
        };

        var actual = (bool)ShouldResetConversationContextMethod.Invoke(null, [previous, current])!;

        Assert.False(actual);
    }

    [Fact]
    public void ShouldResetConversationContext_WhenBindingDifferentAccount_ShouldClearCachedContext()
    {
        var previous = new Models.DemoConfiguration
        {
            AccountId = "bot-account-001",
            UserId = "bot-user-001@im.bot",
        };
        var current = new Models.DemoConfiguration
        {
            AccountId = "bot-account-002",
            UserId = "bot-user-002@im.bot",
        };

        var actual = (bool)ShouldResetConversationContextMethod.Invoke(null, [previous, current])!;

        Assert.True(actual);
    }

    private static object CreateEnvelope(int messageType, string text, string contextToken)
    {
        var envelope = Activator.CreateInstance(EnvelopeType)!;
        EnvelopeType.GetProperty("ClientId")!.SetValue(envelope, "client-demo-001");
        EnvelopeType.GetProperty("MessageId")!.SetValue(envelope, 10001L);
        EnvelopeType.GetProperty("FromUserId")!.SetValue(envelope, "wx-user-001");
        EnvelopeType.GetProperty("MessageType")!.SetValue(envelope, messageType);
        EnvelopeType.GetProperty("CreateTimeMilliseconds")!.SetValue(envelope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        EnvelopeType.GetProperty("ContextToken")!.SetValue(envelope, contextToken);
        EnvelopeType.GetProperty("ItemList")!.SetValue(envelope, CreateItemList(text));
        return envelope;
    }

    private static object CreateItemList(string text)
    {
        var itemList = Activator.CreateInstance(typeof(List<>).MakeGenericType(ItemType))!;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var textItem = Activator.CreateInstance(TextItemType)!;
            TextItemType.GetProperty("Text")!.SetValue(textItem, text);

            var item = Activator.CreateInstance(ItemType)!;
            ItemType.GetProperty("Type")!.SetValue(item, 1);
            ItemType.GetProperty("TextItem")!.SetValue(item, textItem);
            itemList.GetType().GetMethod("Add")!.Invoke(itemList, [item]);
        }

        return itemList;
    }
}
