namespace Freefrag;

/// <summary>Distance in units between the player and a swinging bot's hiding spot at which the bot is released.</summary>
public static class SwingTriggerSetting
{
    public const int Min = 100;
    public const int Max = 3000;
    public const int Default = 600;

    public static bool InRange(int value) => value >= Min && value <= Max;

    // Route lines clamp instead of rejecting: a typo in one line should not cost the whole route.
    public static float Clamp(float value) => Math.Clamp(value, Min, Max);
}
