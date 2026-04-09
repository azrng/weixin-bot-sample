namespace WeixinBotSample.Services;

public sealed class FixedGreetingService
{
    private static readonly string[] Greetings =
    [
        "祝您今天顺顺利利，万事如意。",
        "感谢您的消息，祝您生活愉快、事事顺心。",
        "收到啦，愿您今天心情明朗、工作顺利。",
        "祝您平安喜乐，接下来一切都很顺。",
    ];

    public string PrimaryGreeting => Greetings[0];

    public IReadOnlyList<string> GetAvailableGreetings() => Greetings;

    public string GetGreeting(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return Greetings[0];
        }

        var hash = 17;
        foreach (var character in seed.Trim())
        {
            hash = (hash * 31) + character;
        }

        var index = Math.Abs(hash) % Greetings.Length;
        return Greetings[index];
    }
}
