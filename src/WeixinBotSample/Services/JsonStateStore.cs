using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed class JsonStateStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly SemaphoreSlim _flushSignal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _pendingLock = new();
    private readonly string _stateFilePath;
    private readonly Task _writerTask;

    private WeixinDemoState? _pendingState;
    private long _pendingVersion;
    private long _writtenVersion;
    private bool _disposed;

    public JsonStateStore(IWebHostEnvironment environment)
    {
        _stateFilePath = Path.Combine(environment.ContentRootPath, "App_Data", "weixin-demo-state.json");
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public async Task<WeixinDemoState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
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
            _fileLock.Release();
        }
    }

    public Task SaveAsync(WeixinDemoState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_pendingLock)
        {
            ThrowIfDisposed();
            _pendingState = state.Clone();
            _pendingVersion++;
        }

        _flushSignal.Release();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _flushSignal.Release();

        try
        {
            await _writerTask;
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
        _flushSignal.Dispose();
        _fileLock.Dispose();
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await _flushSignal.WaitAsync(_shutdown.Token);
                await FlushPendingWithDebounceAsync(_shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            await FlushPendingWithDebounceAsync(CancellationToken.None, skipDelay: true);
        }
    }

    private async Task FlushPendingWithDebounceAsync(CancellationToken cancellationToken, bool skipDelay = false)
    {
        while (true)
        {
            if (!skipDelay)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            WeixinDemoState? snapshot;
            long version;
            lock (_pendingLock)
            {
                snapshot = _pendingState;
                version = _pendingVersion;
            }

            if (snapshot is null || version <= _writtenVersion)
            {
                return;
            }

            await WriteStateAsync(snapshot, cancellationToken);

            lock (_pendingLock)
            {
                _writtenVersion = version;
                if (_pendingVersion <= _writtenVersion)
                {
                    return;
                }
            }

            skipDelay = false;
        }
    }

    private async Task WriteStateAsync(WeixinDemoState state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var tempFilePath = $"{_stateFilePath}.tmp";

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectory();
            await File.WriteAllTextAsync(tempFilePath, json, Encoding.UTF8, cancellationToken);
            File.Move(tempFilePath, _stateFilePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
            TryDeleteTempFile(tempFilePath);
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(JsonStateStore));
        }
    }

    private static void TryDeleteTempFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
        }
    }
}
