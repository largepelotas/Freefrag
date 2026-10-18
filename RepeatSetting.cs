namespace Freefrag;

public static class RepeatSetting
{
    public const int Off = 0;
    public const int Infinite = -1;

    public static int? Parse(string token)
    {
        if (token.Equals("off", StringComparison.OrdinalIgnoreCase))
            return Off;
        if (token.Equals("inf", StringComparison.OrdinalIgnoreCase))
            return Infinite;
        return int.TryParse(token, out var value) && value > 0 ? value : null;
    }

    public static bool ShouldRestart(ref int remaining)
    {
        if (remaining == Infinite)
            return true;
        if (remaining > 0)
        {
            remaining--;
            return true;
        }
        return false;
    }

    public static string DisplayName(int remaining) => remaining switch
    {
        Off => "off",
        Infinite => "infinite",
        _ => remaining.ToString()
    };
}
