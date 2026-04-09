namespace WeixinBotSample.Models;

public sealed class PushMessageRequest
{
    public string ExternalChatId { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public string Content { get; set; } = "这是一条来自演示页面的主动推送消息。";

    public PushMessageRequest Clone()
    {
        return new PushMessageRequest
        {
            ExternalChatId = ExternalChatId,
            ContextToken = ContextToken,
            Content = Content,
        };
    }
}
