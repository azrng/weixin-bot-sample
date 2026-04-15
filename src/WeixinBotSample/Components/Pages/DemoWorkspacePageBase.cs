using Microsoft.AspNetCore.Components;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase : ComponentBase, IAsyncDisposable
{
    [Inject]
    protected WeixinBotDemoService DemoService { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    protected readonly CancellationTokenSource RefreshCancellation = new();
    protected WeixinDemoState? State;
    protected AutoFillPromptState? ActiveAutoFillPrompt;
    protected DemoConfiguration ConfigurationModel = new();
    protected PushMessageRequest PushRequest = new();
    protected MediaUploadRequest MediaRequest = new();
    protected string LastSuggestedExternalChatId = string.Empty;
    protected string LastSuggestedContextToken = string.Empty;
    protected string SelectedMediaName = string.Empty;
    protected long SelectedMediaSize;
    protected string SelectedMediaContentType = string.Empty;
    protected string SelectedMediaTempPath = string.Empty;
    protected bool IsLoading = true;
    protected bool IsSaving;
    protected bool IsBinding;
    protected bool IsRuntimeWorking;
    protected bool IsPushing;
    protected bool IsCheckingConnection;
    protected bool IsSendingMedia;
    protected bool IsRunningChecklist;
    protected bool IsRunningAllChecklist;
    protected string ActiveChecklistCode = string.Empty;
    protected bool ConfigurationDirty;
    protected string PageError = string.Empty;
    protected string PushValidationMessage = string.Empty;
    protected string MediaValidationMessage = string.Empty;
    protected string DismissedLoadError = string.Empty;
    protected string SaveButtonText => IsSaving ? "保存中..." : "保存配置";

    private int _pendingReload;

    protected override async Task OnInitializedAsync()
    {
        DemoService.StateChanged += OnDemoStateChanged;
        await LoadStateAsync(true);
    }

    public async ValueTask DisposeAsync()
    {
        DemoService.StateChanged -= OnDemoStateChanged;
        if (!RefreshCancellation.IsCancellationRequested)
        {
            await RefreshCancellation.CancelAsync();
        }

        ClearSelectedMediaFile();
        RefreshCancellation.Dispose();
    }

    private void OnDemoStateChanged(object? sender, EventArgs args)
    {
        if (RefreshCancellation.IsCancellationRequested ||
            Interlocked.Exchange(ref _pendingReload, 1) == 1)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            try
            {
                await LoadStateAsync(!ConfigurationDirty);
            }
            finally
            {
                Interlocked.Exchange(ref _pendingReload, 0);
            }
        });
    }

    protected async Task LoadStateAsync(bool overwriteConfiguration)
    {
        try
        {
            var state = await DemoService.GetStateAsync(RefreshCancellation.Token);
            State = state;

            if (overwriteConfiguration)
            {
                ConfigurationModel = state.Configuration.Clone();
                ConfigurationDirty = false;
            }

            ApplyPushRequestDefaults(state);
            await HandlePendingAutoFillAsync(state);
            if (!string.IsNullOrWhiteSpace(PushRequest.ExternalChatId) &&
                !string.IsNullOrWhiteSpace(PushRequest.ContextToken) &&
                !string.IsNullOrWhiteSpace(PushRequest.Content))
            {
                PushValidationMessage = string.Empty;
            }

            if (!string.Equals(DismissedLoadError, state.LoadError, StringComparison.Ordinal))
            {
                DismissedLoadError = string.Empty;
            }

            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PageError = exception.Message;
            IsLoading = false;
        }
    }

    protected void MarkConfigurationDirty()
    {
        ConfigurationDirty = true;
    }

    protected async Task ExecuteBusyAsync(Func<Task> action, Action begin, Action end, bool overwriteConfiguration)
    {
        ClearFloatingError();
        begin();
        try
        {
            await action();
            await LoadStateAsync(overwriteConfiguration);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PageError = exception.Message;
            await LoadStateAsync(overwriteConfiguration);
        }
        finally
        {
            end();
        }
    }
}
