using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private async Task RunPollingLoopAsync(DemoConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = new WeixinPollingClient(CreateClient(), configuration);
        var syncBuffer = configuration.SyncCursor;
        var delay = DefaultPollDelayMilliseconds;
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pollResult = await client.GetUpdatesAsync(syncBuffer, delay + 5000, cancellationToken);
                var response = pollResult.Response;
                consecutiveFailures = 0;

                if (!string.IsNullOrWhiteSpace(response.GetUpdatesBuffer))
                {
                    syncBuffer = response.GetUpdatesBuffer;
                    configuration.SyncCursor = syncBuffer;
                    await UpdateSyncCursorAsync(syncBuffer, cancellationToken);
                }

                if (response.LongPollingTimeoutMilliseconds > 0)
                {
                    delay = response.LongPollingTimeoutMilliseconds;
                }

                foreach (var message in response.Messages)
                {
                    if (TryBuildInboundTextMessage(message, out var inbound, out _))
                    {
                        await HandleInboundMessageAsync(client, inbound, cancellationToken);
                        continue;
                    }

                    if (TryBuildInboundMediaRecord(message, out var mediaRecord, out _))
                    {
                        await PersistInboundMediaRecordAsync(mediaRecord, cancellationToken);
                        await RecordBackgroundLogAsync("Info", $"收到{GetMediaTypeLabel(mediaRecord.MediaType)}消息：{DisplayFileName(mediaRecord)}。");
                        continue;
                    }

                    if (!TryBuildInboundTextMessage(message, out _, out var skipReason))
                    {
                        await RecordBackgroundLogAsync("Warning", $"忽略入站消息：{skipReason}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WeixinApiException exception) when (exception.ErrorCode == -14)
            {
                logger.LogWarning(exception, "Polling loop stopped because the iLink session expired.");
                await MarkSessionExpiredAsync($"会话已过期，请重新扫码绑定。{(string.IsNullOrWhiteSpace(exception.Message) ? string.Empty : $" 详情：{exception.Message}")}".Trim(), CancellationToken.None);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Polling loop encountered a recoverable error.");
                consecutiveFailures++;
                delay = DefaultPollDelayMilliseconds;
                var retryDelay = consecutiveFailures >= PollingBackoffFailureThreshold
                    ? PollingBackoffDelayMilliseconds
                    : PollingRetryDelayMilliseconds;
                var retryMode = consecutiveFailures >= PollingBackoffFailureThreshold
                    ? "已进入 30 秒退避后重试"
                    : "将短暂等待后重试";
                await RecordBackgroundLogAsync("Warning", $"长轮询失败（第 {consecutiveFailures} 次）：{exception.Message}，{retryMode}。");
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private async Task HandleInboundMessageAsync(
        WeixinPollingClient client,
        WeixinInboundTextMessage inbound,
        CancellationToken cancellationToken)
    {
        DemoConfiguration configuration;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            configuration = _state.Configuration.Clone();
        }
        finally
        {
            _gate.Release();
        }

        configuration = await EnsureTypingTicketAsync(client, configuration, cancellationToken);
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
            await TrySendTypingAsync(client, configuration, cancellationToken);
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
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            messageRecord.ReplySucceeded = false;
            messageRecord.ReplyStatus = "会话已过期，请重新扫码绑定。";
            await PersistInboundRecordAsync(messageRecord, cancellationToken);
            await MarkSessionExpiredAsync("会话已过期，请重新扫码绑定。", CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            messageRecord.ReplySucceeded = false;
            messageRecord.ReplyStatus = $"回复失败：{exception.Message}";
            await PersistInboundRecordAsync(messageRecord, cancellationToken);
            await RecordBackgroundLogAsync("Error", $"回复消息失败：{exception.Message}");
        }
    }

    private static string DisplayFileName(MediaTransferRecord record)
    {
        return string.IsNullOrWhiteSpace(record.FileName) ? "未命名媒体" : record.FileName;
    }
}
