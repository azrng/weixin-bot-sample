using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using QRCoder;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService(
    IHttpClientFactory httpClientFactory,
    JsonStateStore stateStore,
    FixedGreetingService fixedGreetingService,
    IWebHostEnvironment environment,
    ILogger<WeixinBotDemoService> logger) : IAsyncDisposable
{
    public event EventHandler? StateChanged;

    private bool _disposed;

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _bindingCancellation?.Cancel();
        _pollingCancellation?.Cancel();

        var tasks = new[] { _bindingTask, _pollingTask }
            .Where(static task => task is not null)
            .Cast<Task>()
            .ToArray();

        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _bindingCancellation?.Dispose();
        _pollingCancellation?.Dispose();
        _gate.Dispose();
    }
}
