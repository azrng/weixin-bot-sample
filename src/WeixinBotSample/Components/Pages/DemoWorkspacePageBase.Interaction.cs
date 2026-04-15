using WeixinBotSample.Models;

namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase
{
    protected void ApplyPushRequestDefaults(WeixinDemoState state)
    {
        var currentContact = state.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));
        var suggestedExternalChatId = currentContact?.ExternalChatId ?? state.Configuration.LastExternalChatId;
        var suggestedContextToken = currentContact?.LatestContextToken ?? state.Configuration.LastContextToken;

        if (ShouldApplySuggestedValue(PushRequest.ExternalChatId, LastSuggestedExternalChatId))
        {
            PushRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(PushRequest.ContextToken, LastSuggestedContextToken))
        {
            PushRequest.ContextToken = suggestedContextToken;
        }

        if (ShouldApplySuggestedValue(MediaRequest.ExternalChatId, LastSuggestedExternalChatId))
        {
            MediaRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(MediaRequest.ContextToken, LastSuggestedContextToken))
        {
            MediaRequest.ContextToken = suggestedContextToken;
        }

        LastSuggestedExternalChatId = suggestedExternalChatId;
        LastSuggestedContextToken = suggestedContextToken;
    }

    protected static bool ShouldApplySuggestedValue(string currentValue, string lastSuggestedValue)
    {
        return string.IsNullOrWhiteSpace(currentValue) ||
               string.Equals(currentValue.Trim(), lastSuggestedValue, StringComparison.Ordinal);
    }

    private async Task HandlePendingAutoFillAsync(WeixinDemoState state)
    {
        if (state.PendingAutoFill?.HasTarget != true)
        {
            return;
        }

        var currentPath = GetCurrentPath();
        if (!string.Equals(currentPath, "/messages", StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo("/messages");
            return;
        }

        ActiveAutoFillPrompt = state.PendingAutoFill.Clone();
        await DemoService.ClearPendingAutoFillAsync(RefreshCancellation.Token);
    }

    private string GetCurrentPath()
    {
        var relativePath = Navigation.ToBaseRelativePath(Navigation.Uri);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "/";
        }

        var cleanPath = relativePath.Split('?', '#')[0].Trim('/');
        return string.IsNullOrWhiteSpace(cleanPath) ? "/" : $"/{cleanPath}";
    }
}
