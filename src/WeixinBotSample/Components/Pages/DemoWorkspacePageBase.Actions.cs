using Microsoft.AspNetCore.Components.Forms;
using WeixinBotSample.Models;

namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase
{
    protected async Task SaveConfigurationAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SaveConfigurationAsync(ConfigurationModel, RefreshCancellation.Token),
            () => IsSaving = true,
            () => IsSaving = false,
            overwriteConfiguration: true);
    }

    protected async Task BindWeChatAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(false, RefreshCancellation.Token),
            () => IsBinding = true,
            () => IsBinding = false,
            overwriteConfiguration: false);
    }

    protected async Task RefreshQrCodeAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(true, RefreshCancellation.Token),
            () => IsBinding = true,
            () => IsBinding = false,
            overwriteConfiguration: false);
    }

    protected async Task StartListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartListeningAsync(RefreshCancellation.Token),
            () => IsRuntimeWorking = true,
            () => IsRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    protected async Task StopListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StopListeningAsync(RefreshCancellation.Token),
            () => IsRuntimeWorking = true,
            () => IsRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    protected async Task SendPushAsync()
    {
        ClearFloatingError();
        if (!TryValidatePushRequest(out var message))
        {
            PushValidationMessage = message;
            return;
        }

        PushValidationMessage = string.Empty;
        await ExecuteBusyAsync(
            () => DemoService.SendPushMessageAsync(PushRequest, RefreshCancellation.Token),
            () => IsPushing = true,
            () => IsPushing = false,
            overwriteConfiguration: true);
    }

    protected async Task ValidateConnectionAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.ValidateConnectionAsync(RefreshCancellation.Token),
            () => IsCheckingConnection = true,
            () => IsCheckingConnection = false,
            overwriteConfiguration: true);
    }

    protected async Task OnMediaFileChanged(InputFileChangeEventArgs args)
    {
        ClearFloatingError();
        ClearMediaValidationMessage();
        var file = args.File;
        if (file is null)
        {
            ClearSelectedMediaFile();
            return;
        }

        const long maxAllowedSize = 20 * 1024 * 1024;
        ClearSelectedMediaFile();

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "weixin-bot-sample", "media-upload-cache");
        Directory.CreateDirectory(cacheDirectory);
        var tempFilePath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(file.Name)}");

        await using var stream = file.OpenReadStream(maxAllowedSize, RefreshCancellation.Token);
        await using var target = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await stream.CopyToAsync(target, RefreshCancellation.Token);

        SelectedMediaTempPath = tempFilePath;
        SelectedMediaName = file.Name;
        SelectedMediaSize = file.Size;
        SelectedMediaContentType = file.ContentType;
        MediaRequest.FileName = file.Name;
        MediaRequest.ContentType = file.ContentType;
    }

    protected async Task SendMediaAsync()
    {
        ClearFloatingError();
        if (!TryValidateMediaRequest(out var message))
        {
            MediaValidationMessage = message;
            return;
        }

        MediaValidationMessage = string.Empty;
        NormalizeMediaRequestForSend();

        MediaRequest.FileName = string.IsNullOrWhiteSpace(MediaRequest.FileName) ? SelectedMediaName : MediaRequest.FileName;
        MediaRequest.ContentType = string.IsNullOrWhiteSpace(MediaRequest.ContentType) ? SelectedMediaContentType : MediaRequest.ContentType;

        await ExecuteBusyAsync(
            () => DemoService.SendMediaMessageAsync(MediaRequest.Clone(), SelectedMediaTempPath, RefreshCancellation.Token),
            () => IsSendingMedia = true,
            () => IsSendingMedia = false,
            overwriteConfiguration: true);
    }

    protected async Task DownloadMediaAsync(string recordId)
    {
        await ExecuteBusyAsync(
            () => DemoService.DownloadMediaAsync(recordId, RefreshCancellation.Token),
            () => IsSendingMedia = true,
            () => IsSendingMedia = false,
            overwriteConfiguration: true);
    }

    protected async Task RunChecklistAsync(string code)
    {
        await ExecuteBusyAsync(
            () => DemoService.RunChecklistAsync(code, RefreshCancellation.Token),
            () =>
            {
                IsRunningChecklist = true;
                ActiveChecklistCode = code;
            },
            () =>
            {
                IsRunningChecklist = false;
                ActiveChecklistCode = string.Empty;
            },
            overwriteConfiguration: true);
    }

    protected async Task RunAllChecklistAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.RunAllChecklistAsync(RefreshCancellation.Token),
            () => IsRunningAllChecklist = true,
            () => IsRunningAllChecklist = false,
            overwriteConfiguration: true);
    }

    protected void UseKnownContact(KnownContactSession contact)
    {
        PushRequest.ExternalChatId = contact.ExternalChatId;
        PushRequest.ContextToken = contact.LatestContextToken;
        MediaRequest.ExternalChatId = contact.ExternalChatId;
        MediaRequest.ContextToken = contact.LatestContextToken;
        PushValidationMessage = string.Empty;
        MediaValidationMessage = string.Empty;
    }

    private void ClearSelectedMediaFile()
    {
        var previousTempPath = SelectedMediaTempPath;

        SelectedMediaTempPath = string.Empty;
        SelectedMediaName = string.Empty;
        SelectedMediaSize = 0;
        SelectedMediaContentType = string.Empty;
        MediaRequest.FileName = string.Empty;
        MediaRequest.ContentType = string.Empty;

        try
        {
            if (!string.IsNullOrWhiteSpace(previousTempPath) && File.Exists(previousTempPath))
            {
                File.Delete(previousTempPath);
            }
        }
        catch
        {
        }
    }
}
