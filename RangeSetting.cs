namespace Freefrag;

public static class RangeSetting
{
    // Rejects instead of clamping: a typo such as "bothealth 5000" should print usage, not quietly become 1000.
    public static int? Parse(string? token, int min, int max) =>
        int.TryParse(token, out var value) && value >= min && value <= max ? value : null;
}

public static class BotHealthSetting
{
    public const int Min = 1;
    public const int Max = 1000;
    public const int Default = 100;

    public static int? Parse(string? token) => RangeSetting.Parse(token, Min, Max);
}

public static class BotArmorSetting
{
    public const int Min = 0;
    public const int Max = 100;
    public const int Default = 100;

    public static int? Parse(string? token) => RangeSetting.Parse(token, Min, Max);
}

public static class CountdownSetting
{
    public const int Min = 0;
    public const int Max = 5;
    public const int Default = 3;

    public static int? Parse(string? token) => RangeSetting.Parse(token, Min, Max);
}
