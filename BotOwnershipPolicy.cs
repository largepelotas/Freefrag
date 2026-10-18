namespace Freefrag;

[Flags]
public enum OwnershipAction
{
    None = 0,
    StartSweep = 1,
    HoldQuotaZero = 2,
    StopSweep = 4,
    ReleaseOwnedBots = 8,
}

/// <summary>
/// When Freefrag owns the server's bots. It owns them only while Prefire is active: in every other mode the mode's own
/// bots and bot_quota are none of its business. Pure: the plugin does the engine work.
/// </summary>
public static class BotOwnershipPolicy
{
    /// <summary>What the plugin must do when the mode gate flips.</summary>
    public static OwnershipAction ForTransition(ModeTransition transition) => transition switch
    {
        ModeTransition.Activated => OwnershipAction.StartSweep | OwnershipAction.HoldQuotaZero,
        ModeTransition.Deactivated => OwnershipAction.StopSweep | OwnershipAction.ReleaseOwnedBots,
        _ => OwnershipAction.None,
    };

    /// <summary>The stray-bot sweep, kicking unclaimed bots at join, and quota enforcement run only while active.</summary>
    public static bool SweepAllowed(bool active) => active;

    /// <summary>Unloading kicks every bot only while active; a dormant unload leaves the mode's bots alone.</summary>
    public static bool KickAllOnUnload(bool active) => active;

    /// <summary>The engine (a map-start cfg) put the quota back: only an active plugin forces it to 0 again.</summary>
    public static bool QuotaNeedsReset(bool active, int currentQuota) => active && currentQuota != 0;
}
