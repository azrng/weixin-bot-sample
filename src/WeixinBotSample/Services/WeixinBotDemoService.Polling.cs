using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private async Task RunPollingLoopAsync(DemoConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = new WeixinPollingClient(CreateClient(), configuration);
        var syncBuffer = string.Empty;
        var delay = DefaultPollDelayMilliseconds;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pollResult = await client.GetUpdatesAsync(syncBuffer, delay + 5000, cancellationToken);
                var response = pollResult.Response;

                if (!string.IsNullOrWhiteSpace(response.GetUpdatesBuffer))
                {
                    syncBuffer = response.GetUpdatesBuffer;
                }

                if (response.LongPollingTimeoutMilliseconds > 0)
                {
                    delay = response.LongPollingTimeoutMilliseconds;
                }

                if ((response.ReturnCode ?? 0) != 0 || (response.ErrorCode ?? 0) > 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? $"微信 getupdates 返回异常：{response.ErrorCode ?? response.ReturnCode}"
                        : response.ErrorMessage);
                }

                foreach (var message in response.Messages)
                {
                    if (!TryBuildInboundTextMessage(message, out var inbound, out var skipReason))
                    {
                        await RecordBackgroundLogAsync("Warning", $"忽略入站消息：{skipReason}");
                        continue;
                    }

                    await HandleInboundMessageAsync(client, inbound, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Polling loop failed.");
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                _state.Configuration.RuntimeStatus = ChannelRuntimeStatus.Error;
                _state.Configuration.RuntimeError = exception.Message;
                _state.Configuration.RuntimeStoppedAt = DateTimeOffset.UtcNow;
                RecordLogNoLock("Error", $"监听失败：{exception.Message}");
                await PersistStateNoLockAsync(CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task HandleInboundMessageAsync(
        WeixinPollingClient client,
        WeixinInboundTextMessage inbound,
        CancellationToken cancellationToken)
    {
        var replyText = fixedGreetingService.GetGreeting(inbound.Text);
        var messageRecord = new WeixinMessageRecord
        {
            MessageId = inbound.MessageId,
            ExternalChatId = inbound.ExternalChatId,
            ExternalUserId = inbound.ExternalUserId,
            SenderName = inbound.SenderName,
            ChatName = inbound.ChatName,
            Text = inbound.Text,
            ContextToken = inbound.ContextToken,
            CreatedAt = inbound.CreatedAt,
            ReplyText = replyText,
        };

        if (string.IsNullOrWhiteSpace(inbound.ContextToken))
        {
            messageRecord.ReplySucceeded = false;
            messageRecord.ReplyStatus = "缺少 context_token，未执行回复。";
            await PersistInboundRecordAsync(messageRecord, cancellationToken);
            await RecordBackgroundLogAsync("Warning", $"收到消息 {inbound.MessageId}，但缺少 context_token。");
            return;
        }

        try
        {
            var sendResult = await client.SendTextMessageAsync(
                inbound.ExternalChatId,
                inbound.ContextToken,
                replyText,
                cancellationToken);

            messageRecord.ReplySucceeded = true;
            messageRecord.ReplyStatus = $"已回复祝福语，状态码 {(int)sendResult.StatusCode}。";
            await PersistInboundRecordAsync(messageRecord, cancellationToken);
            await RecordBackgroundLogAsync("Success", $"收到 {inbound.SenderName} 的消息并成功回发祝福语。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            messageRecord.ReplySucceeded = false;
            messageRecord.ReplyStatus = $"回复失败：{exception.Message}";
            await PersistInboundRecordAsync(messageRecord, cancellationToken);
            await RecordBackgroundLogAsync("Error", $"回复消息失败：{exception.Message}");
        }
    }
}
