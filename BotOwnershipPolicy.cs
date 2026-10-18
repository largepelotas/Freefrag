namespace Freefrag;

[Flags]
public enum OwnershipAction
{
    None = 0,
    StartSweep = 1,
    HoldQuotaAtTarget = 2,
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
        ModeTransition.Activated => OwnershipAction.StartSweep | OwnershipAction.HoldQuotaAtTarget,
        ModeTransition.Deactivated => OwnershipAction.StopSweep | OwnershipAction.ReleaseOwnedBots,
        _ => OwnershipAction.None,
    };

    /// <summary>The stray-bot sweep, kicking unclaimed bots at join, and quota enforcement run only while active.</summary>
    public static bool SweepAllowed(bool active) => active;

    /// <summary>Unloading kicks every bot only while active; a dormant unload leaves the mode's bots alone.</summary>
    public static bool KickAllOnUnload(bool active) => active;

    /// <summary>
    /// The bot_quota Freefrag must hold while active: the bots it owns plus the bots it has bot_add'ed that have not
    /// joined yet. In CS2 bot_add_* raises bot_quota by one and bot_kick lowers it by one, so the engine never adds or
    /// removes bots on its own as long as the quota equals this number. Never 0 while bots are owned or pending.
    /// </summary>
    public static int QuotaTarget(int owned, int pending) => Math.Max(0, owned) + Math.Max(0, pending);

    /// <summary>The engine (a map-start cfg, a kick) moved the quota off the target: only an active plugin sets it back.</summary>
    public static bool QuotaNeedsSync(bool active, int currentQuota, int target) => active && currentQuota != target;
}
