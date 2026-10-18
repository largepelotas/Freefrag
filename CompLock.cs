namespace Freefrag;

/// <summary>Settings that comp mode pins so that comp times are comparable between players.</summary>
public enum LockedSetting
{
    BotsFire,
    BotHealth,
    BotArmor,
    Countdown,
    Repeat,
    Jitter
}

public static class CompLock
{
    // hs, xray, difficulty, mode and weapon stay changeable in comp: each is already recorded on the score row.
    public static bool Refuses(bool comp, LockedSetting setting) => comp;
}
