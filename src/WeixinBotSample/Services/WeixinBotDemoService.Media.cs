using System.Security.Cryptography;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    public async Task SendMediaMessageAsync(
        MediaUploadRequest request,
        byte[] fileContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fileContent);
        await EnsureInitializedAsync(cancellationToken);

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

        if (string.IsNullOrWhiteSpace(configuration.Token))
        {
            throw new InvalidOperationException("请先完成微信绑定，再发送媒体消息。");
        }

        if (string.IsNullOrWhiteSpace(request.ExternalChatId) ||
            string.IsNullOrWhiteSpace(request.ContextToken) ||
            string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new InvalidOperationException("媒体消息需要填写联系人、ContextToken 和文件名。");
        }

        if (fileContent.Length == 0)
        {
            throw new InvalidOperationException("媒体文件内容为空，无法发送。");
        }

        var record = new MediaTransferRecord
        {
            ExternalChatId = request.ExternalChatId.Trim(),
            ExternalUserId = request.ExternalChatId.Trim(),
            SenderName = request.ExternalChatId.Trim(),
            ContextToken = request.ContextToken.Trim(),
            MediaType = request.MediaType,
            Direction = "Outbound",
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType.Trim(),
            FileSize = fileContent.Length,
            TransferStatus = MediaTransferStatus.Preparing,
            StatusMessage = "准备发送媒体消息。",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await PersistMediaRecordAsync(record, cancellationToken);

        var client = new WeixinPollingClient(CreateClient(), configuration);

        try
        {
            configuration = await EnsureTypingTicketAsync(client, configuration, cancellationToken);
            await TrySendTypingAsync(client, configuration, cancellationToken);

            record.Md5 = Convert.ToHexStringLower(MD5.HashData(fileContent));
            record.FileKey = BuildFileKey(request.FileName);
            var aesKeyBytes = RandomNumberGenerator.GetBytes(16);
            var aesKey = Convert.ToBase64String(aesKeyBytes);
            var encryptedBytes = EncryptAesEcb(fileContent, aesKeyBytes);
            var encryptedLength = encryptedBytes.Length;

            record.AesKey = aesKey;
            record.TransferStatus = MediaTransferStatus.Encrypting;
            record.StatusMessage = $"已完成 AES 加密，准备申请上传参数。明文 {FormatByteLength(fileContent.Length)} / 密文 {FormatByteLength(encryptedLength)}。";
            await PersistMediaRecordAsync(record, cancellationToken);

            var uploadInfo = await client.GetUploadUrlAsync(record.FileKey, record.Md5, encryptedLength, cancellationToken);
            record.TransferStatus = MediaTransferStatus.Uploading;
            record.StatusMessage = $"已拿到上传参数，开始上传加密媒体。上传长度 {FormatByteLength(encryptedLength)}。";
            await PersistMediaRecordAsync(record, cancellationToken);

            var uploadResult = await client.UploadEncryptedMediaAsync(uploadInfo.UploadParam, record.FileKey, encryptedBytes, request.ContentType, cancellationToken);

            record.DownloadParam = uploadResult.DownloadParam;
            record.Media = uploadResult.DownloadParam;
            record.ThumbMedia = request.MediaType is MediaMessageType.Image or MediaMessageType.Video
                ? uploadResult.DownloadParam
                : string.Empty;
            record.TransferStatus = MediaTransferStatus.Sending;
            record.StatusMessage = "上传成功，开始发送媒体消息结构。";
            await PersistMediaRecordAsync(record, cancellationToken);

            var sendResult = await client.SendMediaMessageAsync(
                record.ExternalChatId,
                record.ContextToken,
                BuildMediaPayload(request, record),
                cancellationToken);

            record.ClientId = sendResult.ClientId;
            record.ResponseSummary = TruncateSingleLine(sendResult.RawText, 280);
            record.TransferStatus = MediaTransferStatus.Sent;
            record.StatusMessage = "媒体消息发送成功。";
            await PersistMediaRecordAsync(record, cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                _state.Configuration.LastExternalChatId = request.ExternalChatId.Trim();
                _state.Configuration.LastContextToken = request.ContextToken.Trim();
                UpsertKnownContactNoLock(new WeixinMessageRecord
                {
                    ExternalChatId = request.ExternalChatId.Trim(),
                    ExternalUserId = request.ExternalChatId.Trim(),
                    SenderName = request.ExternalChatId.Trim(),
                    ChatName = request.ExternalChatId.Trim(),
                    ContextToken = request.ContextToken.Trim(),
                    Text = $"[{GetMediaTypeLabel(request.MediaType)}] {record.FileName}".Trim(),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await PersistStateNoLockAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
            await RecordBackgroundLogAsync("Success", $"媒体消息发送成功：{request.MediaType} / {record.FileName}");
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            record.TransferStatus = MediaTransferStatus.Failed;
            record.StatusMessage = "会话已过期，请重新扫码绑定。";
            record.ResponseSummary = TruncateSingleLine(exception.Message, 280);
            await PersistMediaRecordAsync(record, cancellationToken);
            await MarkSessionExpiredAsync("会话已过期，请重新扫码绑定。", CancellationToken.None);
            throw new InvalidOperationException("会话已过期，请重新扫码绑定。", exception);
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -2)
        {
            record.TransferStatus = MediaTransferStatus.Failed;
            record.StatusMessage = "媒体上传参数校验失败。";
            record.ResponseSummary = TruncateSingleLine(exception.Message, 280);
            await PersistMediaRecordAsync(record, cancellationToken);
            await RecordBackgroundLogAsync("Error", $"媒体消息发送失败：{exception.Message}");
            throw new InvalidOperationException(
                "微信 getuploadurl 参数校验失败（errcode=-2）。已按 AES 加密后的密文长度申请上传参数，如仍失败，请检查该账号的媒体能力、文件大小限制或文件类型是否受支持。",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            record.TransferStatus = MediaTransferStatus.Failed;
            record.StatusMessage = exception.Message;
            record.ResponseSummary = TruncateSingleLine(exception.Message, 280);
            await PersistMediaRecordAsync(record, cancellationToken);
            await RecordBackgroundLogAsync("Error", $"媒体消息发送失败：{exception.Message}");
            throw;
        }
    }

    public async Task DownloadMediaAsync(string recordId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recordId))
        {
            throw new InvalidOperationException("缺少媒体记录标识。");
        }

        DemoConfiguration configuration;
        MediaTransferRecord record;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            configuration = _state.Configuration.Clone();
            record = _state.MediaRecords.FirstOrDefault(item => string.Equals(item.Id, recordId, StringComparison.Ordinal))?.Clone()
                     ?? throw new InvalidOperationException("未找到指定的媒体记录。");
        }
        finally
        {
            _gate.Release();
        }

        if (string.IsNullOrWhiteSpace(record.DownloadParam) || string.IsNullOrWhiteSpace(record.AesKey))
        {
            throw new InvalidOperationException("该媒体记录缺少下载参数或 AES Key，暂时无法下载。");
        }

        var client = new WeixinPollingClient(CreateClient(), configuration);
        record.TransferStatus = MediaTransferStatus.Downloading;
        record.StatusMessage = "开始下载并解密媒体内容。";
        await PersistMediaRecordAsync(record, cancellationToken);

        try
        {
            var encrypted = await client.DownloadEncryptedMediaAsync(record.DownloadParam, cancellationToken);
            var plainBytes = DecryptAesEcb(encrypted, Convert.FromBase64String(record.AesKey));
            var cacheDirectory = EnsureDirectory(Path.Combine(environment.ContentRootPath, "App_Data", "media-cache"));
            var safeFileName = CreateSafeCacheFileName(record);
            var fullPath = Path.Combine(cacheDirectory, safeFileName);
            await File.WriteAllBytesAsync(fullPath, plainBytes, cancellationToken);

            record.LocalCachePath = fullPath;
            record.TransferStatus = MediaTransferStatus.Downloaded;
            record.StatusMessage = "媒体下载并解密成功。";
            await PersistMediaRecordAsync(record, cancellationToken);
            await RecordBackgroundLogAsync("Success", $"媒体已下载到缓存：{record.FileName}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            record.TransferStatus = MediaTransferStatus.Failed;
            record.StatusMessage = exception.Message;
            await PersistMediaRecordAsync(record, cancellationToken);
            await RecordBackgroundLogAsync("Error", $"媒体下载失败：{exception.Message}");
            throw;
        }
    }

    private async Task PersistInboundMediaRecordAsync(MediaTransferRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            record.UpdatedAt = DateTimeOffset.UtcNow;
            UpsertMediaRecordNoLock(record);

            if (!string.IsNullOrWhiteSpace(record.ExternalChatId))
            {
                var contact = new WeixinMessageRecord
                {
                    ExternalChatId = record.ExternalChatId,
                    ExternalUserId = string.IsNullOrWhiteSpace(record.ExternalUserId) ? record.ExternalChatId : record.ExternalUserId,
                    SenderName = record.SenderName,
                    ChatName = record.SenderName,
                    ContextToken = record.ContextToken,
                    Text = $"[{GetMediaTypeLabel(record.MediaType)}] {record.FileName}".Trim(),
                    CreatedAt = record.CreatedAt,
                };
                UpsertKnownContactNoLock(contact);
            }

            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static object BuildMediaPayload(MediaUploadRequest request, MediaTransferRecord record)
    {
        return request.MediaType switch
        {
            MediaMessageType.Image => new
            {
                type = 2,
                image_item = new
                {
                    media = record.Media,
                    thumb_media = record.ThumbMedia,
                    aeskey = record.AesKey,
                    md5 = record.Md5,
                    len = record.FileSize,
                    file_name = record.FileName,
                },
            },
            MediaMessageType.Voice => new
            {
                type = 3,
                voice_item = new
                {
                    media = record.Media,
                    aeskey = record.AesKey,
                    md5 = record.Md5,
                    len = record.FileSize,
                    encode_type = request.EncodeType,
                    playtime = request.PlayTimeMilliseconds,
                    text = request.Description,
                },
            },
            MediaMessageType.Video => new
            {
                type = 5,
                video_item = new
                {
                    media = record.Media,
                    thumb_media = record.ThumbMedia,
                    aeskey = record.AesKey,
                    md5 = record.Md5,
                    len = record.FileSize,
                    file_name = record.FileName,
                    video_size = record.FileSize,
                },
            },
            _ => new
            {
                type = 4,
                file_item = new
                {
                    media = record.Media,
                    aeskey = record.AesKey,
                    file_name = record.FileName,
                    md5 = record.Md5,
                    len = record.FileSize,
                },
            },
        };
    }

    private static byte[] EncryptAesEcb(byte[] plainBytes, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var transform = aes.CreateEncryptor();
        return transform.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    }

    private static byte[] DecryptAesEcb(byte[] cipherBytes, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var transform = aes.CreateDecryptor();
        return transform.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    private static string BuildFileKey(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateSafeCacheFileName(MediaTransferRecord record)
    {
        var originalName = string.IsNullOrWhiteSpace(record.FileName)
            ? $"{record.MediaType.ToString().ToLowerInvariant()}-{record.Id}"
            : record.FileName;
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(originalName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return $"{record.CreatedAt:yyyyMMddHHmmss}-{cleaned}";
    }

    private static string FormatByteLength(long length)
    {
        return $"{length} B";
    }

    private static string GetMediaTypeLabel(MediaMessageType mediaType)
    {
        return mediaType switch
        {
            MediaMessageType.Image => "图片",
            MediaMessageType.Voice => "语音",
            MediaMessageType.Video => "视频",
            _ => "文件",
        };
    }
}
