namespace Freefrag;

public enum LandingVerdict { Pending, Landed, TimedOut }

/// <summary>
/// Tracks whether a freshly teleported bot has touched the ground. Pure, tick-driven, no engine types.
/// </summary>
public sealed class LandingWatch
{
    public const int DefaultDeadlineTicks = 32; // 0.5 s at 64 tick

    // The bot was standing at its team spawn when it was teleported, so FL_ONGROUND can still be the
    // stale pre-teleport value until movement has simulated the pawn at its new position. Ground
    // reports inside this window are ignored so a bot teleported into the air is not frozen there.
    public const int DefaultSettleTicks = 2;

    private readonly int _startTick;
    private readonly int _deadlineTicks;
    private readonly int _settleTicks;
    private LandingVerdict _verdict = LandingVerdict.Pending;

    /// <summary>Z of the teleport target; only used to make the timeout log informative.</summary>
    public float TargetZ { get; init; }

    public LandingWatch(int startTick, int deadlineTicks = DefaultDeadlineTicks, int settleTicks = DefaultSettleTicks)
    {
        if (deadlineTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(deadlineTicks), "Deadline must be positive.");
        if (settleTicks < 0 || settleTicks >= deadlineTicks)
            throw new ArgumentOutOfRangeException(nameof(settleTicks), "Settle window must be non-negative and shorter than the deadline.");
        _startTick = startTick;
        _deadlineTicks = deadlineTicks;
        _settleTicks = settleTicks;
    }

    /// <summary>
    /// Call once per tick. Landed as soon as grounded is true once the settle window has passed
    /// (grounded at the deadline tick still counts as Landed). TimedOut once
    /// tick >= startTick + deadlineTicks and never grounded. After a non-Pending verdict, keeps
    /// returning that verdict.
    /// </summary>
    public LandingVerdict Tick(bool grounded, int tick)
    {
        if (_verdict != LandingVerdict.Pending)
            return _verdict;

        if (grounded && tick >= _startTick + _settleTicks)
            _verdict = LandingVerdict.Landed;
        else if (tick >= _startTick + _deadlineTicks)
            _verdict = LandingVerdict.TimedOut;

        return _verdict;
    }
}
