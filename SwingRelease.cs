namespace Freefrag;

/// <summary>
/// Runtime state of one swinging bot: hidden and frozen at its route spot until the owner comes close, then
/// released to run under the behaviour tree, then frozen again at its peek point.
/// </summary>
public sealed class SwingState
{
    public SwingSpec Peek;
    public float Trigger;
    public float HideX, HideY, HideZ;
    public string RouteName = "";
    public int TargetIndex;
    public bool Landed;
    public bool Released;
    public int ReleasedTick;
    public bool Frozen;
}

/// <summary>Pure release and stop-line rules for swinging bots; the plugin supplies positions and the tick.</summary>
public static class SwingRelease
{
    public const float ArriveDistance = 30f;
    public const int MaxRunTicks = 128;     // 2 s at 64 tick

    /// <summary>The route line's trigger when it sets one, else the configured default; both clamped to the legal range.</summary>
    public static float EffectiveTrigger(float lineTrigger, int configDefault) =>
        SwingTriggerSetting.Clamp(lineTrigger != SwingSpec.NoTrigger ? lineTrigger : configDefault);

    private static float Distance(float ax, float ay, float az, float bx, float by, float bz)
    {
        var dx = ax - bx; var dy = ay - by; var dz = az - bz;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>True once the bot has landed and frozen at its spot, is not yet released, and the owner is within its trigger distance (3D).</summary>
    public static bool ShouldRelease(SwingState s, float ownerX, float ownerY, float ownerZ) =>
        s.Landed && !s.Released
        && Distance(ownerX, ownerY, ownerZ, s.HideX, s.HideY, s.HideZ) <= s.Trigger;

    /// <summary>True for a running (released, not yet frozen) bot that reached its peek point or has run for the full time cap.</summary>
    public static bool Arrived(SwingState s, float botX, float botY, float botZ, int tick) =>
        s.Released && !s.Frozen
        && (Distance(botX, botY, botZ, s.Peek.X, s.Peek.Y, s.Peek.Z) <= ArriveDistance || tick - s.ReleasedTick >= MaxRunTicks);

    /// <summary>True when a running bot has used up the time cap (it may still have reached the peek point on that tick; the caller checks).</summary>
    public static bool TimedOut(SwingState s, int tick) =>
        s.Released && !s.Frozen && tick - s.ReleasedTick >= MaxRunTicks;

    /// <summary>True when the bot is within <see cref="ArriveDistance"/> of its peek point.</summary>
    public static bool AtPeek(SwingState s, float botX, float botY, float botZ) =>
        Distance(botX, botY, botZ, s.Peek.X, s.Peek.Y, s.Peek.Z) <= ArriveDistance;

    /// <summary>
    /// Drops every state and returns the bot slots that were running (released, not frozen): those were excluded
    /// from the aim lock and need it back. The pure half of the plugin's ClearSwingers.
    /// </summary>
    public static List<int> TakeStates(Dictionary<int, SwingState> swingers)
    {
        var running = swingers.Where(kv => kv.Value.Released && !kv.Value.Frozen).Select(kv => kv.Key).ToList();
        swingers.Clear();
        return running;
    }
}
