namespace Freefrag;

public enum LandingVerdict { Pending, Landed, TimedOut }

/// <summary>Why a <see cref="LandingWatch"/> stopped being Pending.</summary>
public enum LandingReason { None, Ground, Settled, Timeout }

/// <summary>
/// Tracks whether a freshly teleported bot has come to rest on the floor. Pure, tick-driven, no engine types.
/// </summary>
public sealed class LandingWatch
{
    public const int DefaultDeadlineTicks = 32; // 0.5 s at 64 tick

    // The bot was standing at its team spawn when it was teleported, so FL_ONGROUND can still be the
    // stale pre-teleport value until movement has simulated the pawn at its new position. Ground
    // reports inside this window are ignored so a bot teleported into the air is not frozen there.
    // Samples inside the window also never count toward the settle test, because a pawn teleported
    // with zero velocity looks motionless until the first simulated tick applies gravity.
    public const int DefaultSettleTicks = 2;

    // Bots never report FL_ONGROUND or a GroundEntity on the live server, so a pawn that has stopped
    // moving is the real landing signal: |vz| within VelocityEpsilon and a Z change below
    // ZEpsilon for this many consecutive ticks.
    public const int DefaultStableTicks = 3;
    public const float VelocityEpsilon = 1.0f;  // units/s
    public const float ZEpsilon = 0.05f;        // units per tick

    private readonly int _startTick;
    private readonly int _deadlineTicks;
    private readonly int _settleTicks;
    private readonly int _stableTicks;
    private LandingVerdict _verdict = LandingVerdict.Pending;
    private LandingReason _reason = LandingReason.None;
    private float _previousZ = float.NaN;
    private int _stableCount;

    /// <summary>Z of the teleport target; only used to make the timeout log informative.</summary>
    public float TargetZ { get; init; }
    /// <summary>Route and target index the watched bot was placed for; only used in log lines.</summary>
    public string RouteName { get; init; } = "";
    public int TargetIndex { get; init; } = -1;

    /// <summary>
    /// True when the pawn never moved from its first sample: no vertical speed in any sample and the
    /// current Z equals the first one. On the live server that means the engine never simulated the
    /// pawn at all (a bot off the nav mesh), so it sits exactly where the teleport put it.
    /// </summary>
    public bool NeverMoved(float currentZ) => MaxAbsVz == 0f && !float.IsNaN(FirstZ) && FirstZ == currentZ;

    /// <summary>How the watch finished; None while Pending.</summary>
    public LandingReason Reason => _reason;

    // Diagnostics for the log line printed when the watch ends: did the pawn move at all after the teleport?
    /// <summary>Z of the first sample, NaN before any.</summary>
    public float FirstZ { get; private set; } = float.NaN;
    /// <summary>Largest |vz| seen in any sample (NaN samples ignored).</summary>
    public float MaxAbsVz { get; private set; }
    /// <summary>Ticks between the start tick and the last Tick call.</summary>
    public int TicksTaken { get; private set; }
    /// <summary>Z of every sample, in order, capped at <see cref="ZTraceCap"/> entries.</summary>
    public IReadOnlyList<float> ZTrace => _zTrace;
    public const int ZTraceCap = 40;
    private readonly List<float> _zTrace = new();

    public LandingWatch(int startTick, int deadlineTicks = DefaultDeadlineTicks, int settleTicks = DefaultSettleTicks, int stableTicks = DefaultStableTicks)
    {
        if (deadlineTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(deadlineTicks), "Deadline must be positive.");
        if (settleTicks < 0 || settleTicks >= deadlineTicks)
            throw new ArgumentOutOfRangeException(nameof(settleTicks), "Settle window must be non-negative and shorter than the deadline.");
        if (stableTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(stableTicks), "Stable tick count must be positive.");
        _startTick = startTick;
        _deadlineTicks = deadlineTicks;
        _settleTicks = settleTicks;
        _stableTicks = stableTicks;
    }

    /// <summary>
    /// Ground-only form with no motion data, so the settle test can never fire. Same as
    /// <see cref="Tick(int, float, float, bool)"/> with NaN z and vz.
    /// </summary>
    public LandingVerdict Tick(bool grounded, int tick) => Tick(tick, float.NaN, float.NaN, grounded);

    /// <summary>
    /// Call once per tick with the pawn's Z position, vertical velocity and ground indicators.
    /// Landed (Reason Ground) as soon as grounded is true once the settle window has passed
    /// (grounded at the deadline tick still counts); Landed (Reason Settled) once vz is within
    /// <see cref="VelocityEpsilon"/> of zero and Z moved less than <see cref="ZEpsilon"/> for the
    /// required number of consecutive ticks. The first sample only seeds the previous Z. TimedOut
    /// (Reason Timeout) once tick >= startTick + deadlineTicks with neither. After a non-Pending
    /// verdict, keeps returning that verdict.
    /// </summary>
    public LandingVerdict Tick(int tick, float z, float vz, bool grounded)
    {
        if (_verdict != LandingVerdict.Pending)
            return _verdict;

        TicksTaken = tick - _startTick;
        if (float.IsNaN(FirstZ)) FirstZ = z;
        if (_zTrace.Count < ZTraceCap) _zTrace.Add(z);
        if (!float.IsNaN(vz) && MathF.Abs(vz) > MaxAbsVz) MaxAbsVz = MathF.Abs(vz);

        bool pastSettleWindow = tick >= _startTick + _settleTicks;

        if (grounded && pastSettleWindow)
        {
            return Finish(LandingVerdict.Landed, LandingReason.Ground);
        }

        if (pastSettleWindow)
        {
            // NaN comparisons are false, so missing data counts as moving. The first sample has no
            // previous Z (NaN) and therefore only seeds the baseline.
            bool stable = MathF.Abs(vz) <= VelocityEpsilon && MathF.Abs(z - _previousZ) < ZEpsilon;
            _stableCount = stable ? _stableCount + 1 : 0;
            if (_stableCount >= _stableTicks)
                return Finish(LandingVerdict.Landed, LandingReason.Settled);
        }
        _previousZ = z;

        if (tick >= _startTick + _deadlineTicks)
            return Finish(LandingVerdict.TimedOut, LandingReason.Timeout);

        return _verdict;
    }

    private LandingVerdict Finish(LandingVerdict verdict, LandingReason reason)
    {
        _verdict = verdict;
        _reason = reason;
        return verdict;
    }
}
