using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed class JsonStateStore(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _stateFilePath = Path.Combine(environment.ContentRootPath, "App_Data", "weixin-demo-state.json");

    public async Task<WeixinDemoState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectory();
            if (!File.Exists(_stateFilePath))
            {
                return new WeixinDemoState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath, Encoding.UTF8, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new WeixinDemoState();
            }

            return JsonSerializer.Deserialize<WeixinDemoState>(json, SerializerOptions) ?? new WeixinDemoState();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(WeixinDemoState state, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(state, SerializerOptions);
            await File.WriteAllTextAsync(_stateFilePath, json, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
