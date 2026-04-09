using WeixinBotSample.Services;

namespace WeixinBotSample.Tests;

public sealed class FixedGreetingServiceTests
{
    [Fact]
    public void GetGreeting_WhenSeedIsEmpty_ReturnsPrimaryGreeting()
    {
        var service = new FixedGreetingService();

        var result = service.GetGreeting(string.Empty);

        Assert.Equal(service.PrimaryGreeting, result);
    }

    [Fact]
    public void GetGreeting_WhenSeedMatchesSameInput_ReturnsStableGreeting()
    {
        var service = new FixedGreetingService();

        var first = service.GetGreeting("你好，微信");
        var second = service.GetGreeting("你好，微信");

        Assert.Equal(first, second);
        Assert.Contains(first, service.GetAvailableGreetings());
    }
}
