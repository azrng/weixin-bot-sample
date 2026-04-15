using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Tests;

public sealed class JsonStateStoreTests
{
    [Fact]
    public async Task SaveAsync_WhenCalledRepeatedly_ShouldPersistLatestSnapshot()
    {
        var rootPath = CreateTempRootPath();

        await using var store = new JsonStateStore(new TestWebHostEnvironment(rootPath));
        await store.SaveAsync(BuildState("bot-account-001"));
        await store.SaveAsync(BuildState("bot-account-002"));

        var persisted = await WaitForStateFileAsync(rootPath);

        Assert.Contains("\"accountId\": \"bot-account-002\"", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("\"accountId\": \"bot-account-001\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_ShouldFlushPendingSnapshot()
    {
        var rootPath = CreateTempRootPath();
        var store = new JsonStateStore(new TestWebHostEnvironment(rootPath));

        await store.SaveAsync(BuildState("bot-account-dispose"));
        await store.DisposeAsync();

        var persisted = await File.ReadAllTextAsync(GetStateFilePath(rootPath));
        Assert.Contains("\"accountId\": \"bot-account-dispose\"", persisted, StringComparison.Ordinal);
    }

    private static WeixinDemoState BuildState(string accountId)
    {
        return new WeixinDemoState
        {
            Configuration = new DemoConfiguration
            {
                AccountId = accountId,
                UserId = $"{accountId}@im.bot",
                Token = "demo-token",
            },
        };
    }

    private static string CreateTempRootPath()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "weixin-bot-sample-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    private static string GetStateFilePath(string rootPath)
    {
        return Path.Combine(rootPath, "App_Data", "weixin-demo-state.json");
    }

    private static async Task<string> WaitForStateFileAsync(string rootPath)
    {
        var stateFilePath = GetStateFilePath(rootPath);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (File.Exists(stateFilePath))
            {
                var content = await File.ReadAllTextAsync(stateFilePath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("State file was not flushed in time.");
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "WeixinBotSample.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
