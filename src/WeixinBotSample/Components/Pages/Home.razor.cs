using Microsoft.AspNetCore.Components;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Components.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    [Inject]
    private WeixinBotDemoService DemoService { get; set; } = default!;

    private readonly CancellationTokenSource _refreshCancellation = new();
    private WeixinDemoState? _state;
    private DemoConfiguration _configurationModel = new();
    private PushMessageRequest _pushRequest = new();
    private string _lastSuggestedExternalChatId = string.Empty;
    private string _lastSuggestedContextToken = string.Empty;
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _isBinding;
    private bool _isRuntimeWorking;
    private bool _isPushing;
    private bool _configurationDirty;
    private string _pageError = string.Empty;
    private string _saveButtonText => _isSaving ? "保存中..." : "保存配置";

    protected override async Task OnInitializedAsync()
    {
        await LoadStateAsync(true);
        _ = Task.Run(RefreshLoopAsync);
    }

    private async Task RefreshLoopAsync()
    {
        while (!_refreshCancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _refreshCancellation.Token);
                await InvokeAsync(() => LoadStateAsync(!_configurationDirty));
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task LoadStateAsync(bool overwriteConfiguration)
    {
        try
        {
            var state = await DemoService.GetStateAsync(_refreshCancellation.Token);
            _state = state;

            if (overwriteConfiguration)
            {
                _configurationModel = state.Configuration.Clone();
                _configurationDirty = false;
            }

            ApplyPushRequestDefaults(state);

            _isLoading = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _pageError = exception.Message;
            _isLoading = false;
        }
    }

    private void MarkConfigurationDirty()
    {
        _configurationDirty = true;
    }

    private async Task SaveConfigurationAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SaveConfigurationAsync(_configurationModel, _refreshCancellation.Token),
            () => _isSaving = true,
            () => _isSaving = false,
            overwriteConfiguration: true);
    }

    private async Task BindWeChatAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(false, _refreshCancellation.Token),
            () => _isBinding = true,
            () => _isBinding = false,
            overwriteConfiguration: false);
    }

    private async Task RefreshQrCodeAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(true, _refreshCancellation.Token),
            () => _isBinding = true,
            () => _isBinding = false,
            overwriteConfiguration: false);
    }

    private async Task StartListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartListeningAsync(_refreshCancellation.Token),
            () => _isRuntimeWorking = true,
            () => _isRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    private async Task StopListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StopListeningAsync(_refreshCancellation.Token),
            () => _isRuntimeWorking = true,
            () => _isRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    private async Task SendPushAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SendPushMessageAsync(_pushRequest, _refreshCancellation.Token),
            () => _isPushing = true,
            () => _isPushing = false,
            overwriteConfiguration: true);
    }

    private async Task ExecuteBusyAsync(Func<Task> action, Action begin, Action end, bool overwriteConfiguration)
    {
        _pageError = string.Empty;
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
            _pageError = exception.Message;
            await LoadStateAsync(overwriteConfiguration);
        }
        finally
        {
            end();
        }
    }

    private string GetRuntimeStatusText()
    {
        return _state?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "监听运行中",
            ChannelRuntimeStatus.Error => "监听异常",
            _ => "监听已停止",
        };
    }

    private string GetRuntimeStatusClass()
    {
        return _state?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "status-chip--running",
            ChannelRuntimeStatus.Error => "status-chip--error",
            _ => "status-chip--stopped",
        };
    }

    private string GetBindingStatusText()
    {
        return _state?.Configuration.IsBound == true ? "已绑定微信 Bot" : "尚未绑定";
    }

    private string GetBindingStatusClass()
    {
        return _state?.Configuration.IsBound == true
            ? "status-chip--bound"
            : "status-chip--pending";
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    private static string DisplayOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private void ApplyPushRequestDefaults(WeixinDemoState state)
    {
        var currentMessage = state.Messages.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.ContextToken));
        var suggestedExternalChatId = currentMessage?.ExternalChatId ?? state.Configuration.LastExternalChatId;
        var suggestedContextToken = currentMessage?.ContextToken ?? state.Configuration.LastContextToken;

        if (ShouldApplySuggestedValue(_pushRequest.ExternalChatId, _lastSuggestedExternalChatId))
        {
            _pushRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(_pushRequest.ContextToken, _lastSuggestedContextToken))
        {
            _pushRequest.ContextToken = suggestedContextToken;
        }

        _lastSuggestedExternalChatId = suggestedExternalChatId;
        _lastSuggestedContextToken = suggestedContextToken;
    }

    private string GetPushTargetHint()
    {
        var currentMessage = _state?.Messages.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.ContextToken));

        if (currentMessage is null)
        {
            return "默认带入最近一次可回复的微信会话。";
        }

        var senderName = DisplayOrFallback(currentMessage.SenderName, currentMessage.ExternalUserId);
        return $"默认带入当前微信用户：{senderName} / {currentMessage.ExternalChatId}";
    }

    private static bool ShouldApplySuggestedValue(string currentValue, string lastSuggestedValue)
    {
        return string.IsNullOrWhiteSpace(currentValue) ||
               string.Equals(currentValue.Trim(), lastSuggestedValue, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_refreshCancellation.IsCancellationRequested)
        {
            await _refreshCancellation.CancelAsync();
        }

        _refreshCancellation.Dispose();
    }
}
