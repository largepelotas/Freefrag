using Freefrag;
using Xunit;

public class LandingWatchTests
{
    [Fact]
    public void PendingWhileAirborneBeforeDeadline()
    {
        var w = new LandingWatch(100, 32);
        for (int t = 100; t < 132; t++)
            Assert.Equal(LandingVerdict.Pending, w.Tick(false, t));
    }

    [Fact]
    public void LandedOnFirstGroundedTickAfterSettleWindow()
    {
        var w = new LandingWatch(100, 32, settleTicks: 2);
        Assert.Equal(LandingVerdict.Pending, w.Tick(false, 100));
        Assert.Equal(LandingVerdict.Pending, w.Tick(false, 101));
        Assert.Equal(LandingVerdict.Landed, w.Tick(true, 102));
    }

    [Fact]
    public void StaleGroundFlagInsideSettleWindowIsIgnored()
    {
        // The pawn still reports the ground it stood on before the teleport.
        var w = new LandingWatch(100, 32, settleTicks: 2);
        Assert.Equal(LandingVerdict.Pending, w.Tick(true, 100));
        Assert.Equal(LandingVerdict.Pending, w.Tick(true, 101));
        Assert.Equal(LandingVerdict.Pending, w.Tick(false, 102));
        Assert.Equal(LandingVerdict.Landed, w.Tick(true, 110));
    }

    [Fact]
    public void ZeroSettleWindowLandsImmediately()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        Assert.Equal(LandingVerdict.Landed, w.Tick(true, 100));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    [InlineData(40)]
    public void SettleWindowMustBeShorterThanDeadline(int settle)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandingWatch(0, 32, settle));
    }

    [Fact]
    public void GroundedExactlyAtDeadlineTickIsLanded()
    {
        var w = new LandingWatch(100, 32);
        Assert.Equal(LandingVerdict.Landed, w.Tick(true, 132));
    }

    [Fact]
    public void NeverGroundedTimesOutAtDeadlineNotEarlier()
    {
        var w = new LandingWatch(100, 32);
        Assert.Equal(LandingVerdict.Pending, w.Tick(false, 131));
        Assert.Equal(LandingVerdict.TimedOut, w.Tick(false, 132));
    }

    [Fact]
    public void LandedVerdictIsSticky()
    {
        var w = new LandingWatch(100, 32);
        Assert.Equal(LandingVerdict.Landed, w.Tick(true, 105));
        Assert.Equal(LandingVerdict.Landed, w.Tick(false, 106));
        Assert.Equal(LandingVerdict.Landed, w.Tick(false, 500));
    }

    [Fact]
    public void TimedOutVerdictIsSticky()
    {
        var w = new LandingWatch(100, 32);
        Assert.Equal(LandingVerdict.TimedOut, w.Tick(false, 140));
        Assert.Equal(LandingVerdict.TimedOut, w.Tick(true, 141));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveDeadlineThrows(int deadline)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandingWatch(0, deadline));
    }

    // Settle test: bots never report ground, so rest on the floor is detected from Z and vz.

    [Fact]
    public void SettlesAfterThreeStableTicksWithZeroVelocity()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        Assert.Equal(LandingVerdict.Pending, w.Tick(100, 64.0f, 0f, false)); // seeds the previous Z
        Assert.Equal(LandingVerdict.Pending, w.Tick(101, 64.0f, 0f, false)); // stable 1
        Assert.Equal(LandingVerdict.Pending, w.Tick(102, 64.0f, 0f, false)); // stable 2
        Assert.Equal(LandingVerdict.Landed, w.Tick(103, 64.0f, 0f, false));  // stable 3
        Assert.Equal(LandingReason.Settled, w.Reason);
    }

    [Fact]
    public void DoesNotSettleWhileZIsChanging()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        float z = 80f;
        for (int t = 100; t < 132; t++)
        {
            z -= 0.5f; // falling slowly but vz is reported as zero
            Assert.Equal(LandingVerdict.Pending, w.Tick(t, z, 0f, false));
        }
    }

    [Fact]
    public void DoesNotSettleWhileVelocityIsNonZero()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        for (int t = 100; t < 132; t++)
            Assert.Equal(LandingVerdict.Pending, w.Tick(t, 64.0f, -3.0f, false));
    }

    [Fact]
    public void VelocityWithinOneUnitPerSecondCountsAsStill()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(100, 64.0f, 0.9f, false);
        w.Tick(101, 64.0f, -0.9f, false);
        w.Tick(102, 64.0f, 1.0f, false);
        Assert.Equal(LandingVerdict.Landed, w.Tick(103, 64.0f, -1.0f, false));
        Assert.Equal(LandingReason.Settled, w.Reason);
    }

    [Fact]
    public void ZChangeBelowThresholdCountsAsStable()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(100, 64.00f, 0f, false);
        w.Tick(101, 64.04f, 0f, false);
        w.Tick(102, 64.00f, 0f, false);
        Assert.Equal(LandingVerdict.Landed, w.Tick(103, 64.04f, 0f, false));
    }

    [Fact]
    public void ZChangeAtThresholdIsNotStable()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(100, 64.0f, 0f, false);
        Assert.Equal(LandingVerdict.Pending, w.Tick(101, 64.0f + 0.06f, 0f, false));
    }

    [Fact]
    public void MovementResetsTheStableCount()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(100, 64.0f, 0f, false);
        w.Tick(101, 64.0f, 0f, false); // stable 1
        w.Tick(102, 64.0f, 0f, false); // stable 2
        Assert.Equal(LandingVerdict.Pending, w.Tick(103, 63.0f, -50f, false)); // moves: reset
        Assert.Equal(LandingVerdict.Pending, w.Tick(104, 63.0f, 0f, false));   // stable 1
        Assert.Equal(LandingVerdict.Pending, w.Tick(105, 63.0f, 0f, false));   // stable 2
        Assert.Equal(LandingVerdict.Landed, w.Tick(106, 63.0f, 0f, false));    // stable 3
    }

    [Fact]
    public void SingleStableTickThenMovementResetsTheCount()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(100, 64.0f, 0f, false);
        w.Tick(101, 64.0f, 0f, false);   // stable 1
        w.Tick(102, 60.0f, -80f, false); // reset
        w.Tick(103, 60.0f, 0f, false);   // stable 1
        Assert.Equal(LandingVerdict.Pending, w.Tick(104, 60.0f, 0f, false)); // stable 2 only
    }

    [Fact]
    public void StillSamplesInsideSettleWindowDoNotCount()
    {
        // A pawn teleported with zero velocity looks motionless before gravity is applied.
        var w = new LandingWatch(100, 32, settleTicks: 2);
        Assert.Equal(LandingVerdict.Pending, w.Tick(100, 80.0f, 0f, false));
        Assert.Equal(LandingVerdict.Pending, w.Tick(101, 80.0f, 0f, false));
        Assert.Equal(LandingVerdict.Pending, w.Tick(102, 80.0f, 0f, false)); // stable 1
        Assert.Equal(LandingVerdict.Pending, w.Tick(103, 80.0f, 0f, false)); // stable 2
        Assert.Equal(LandingVerdict.Landed, w.Tick(104, 80.0f, 0f, false));  // stable 3
    }

    [Fact]
    public void StableTickCountIsConfigurable()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0, stableTicks: 1);
        w.Tick(100, 64.0f, 0f, false);
        Assert.Equal(LandingVerdict.Landed, w.Tick(101, 64.0f, 0f, false));
    }

    [Fact]
    public void NonPositiveStableTicksThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandingWatch(0, 32, 2, 0));
    }

    [Fact]
    public void GroundFlagAcceptsImmediatelyWithMotionData()
    {
        var w = new LandingWatch(100, 32, settleTicks: 2);
        Assert.Equal(LandingVerdict.Landed, w.Tick(102, 70.0f, -200f, true));
        Assert.Equal(LandingReason.Ground, w.Reason);
    }

    [Fact]
    public void GroundFlagInsideSettleWindowIsStillIgnoredWithMotionData()
    {
        var w = new LandingWatch(100, 32, settleTicks: 2);
        Assert.Equal(LandingVerdict.Pending, w.Tick(101, 70.0f, 0f, true));
        Assert.Equal(LandingReason.None, w.Reason);
    }

    [Fact]
    public void GroundTakesPrecedenceOverSettledWhenBothHold()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0, stableTicks: 1);
        w.Tick(100, 64.0f, 0f, false);
        Assert.Equal(LandingVerdict.Landed, w.Tick(101, 64.0f, 0f, true));
        Assert.Equal(LandingReason.Ground, w.Reason);
    }

    [Fact]
    public void TimeoutStillFiresAtDeadlineAndReportsTimeout()
    {
        var w = new LandingWatch(100, 32);
        float z = 200f;
        for (int t = 100; t < 132; t++)
        {
            z -= 5f;
            Assert.Equal(LandingVerdict.Pending, w.Tick(t, z, -300f, false));
            Assert.Equal(LandingReason.None, w.Reason);
        }
        Assert.Equal(LandingVerdict.TimedOut, w.Tick(132, z - 5f, -300f, false));
        Assert.Equal(LandingReason.Timeout, w.Reason);
    }

    [Fact]
    public void SettlingOnTheDeadlineTickBeatsTimeout()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0);
        w.Tick(129, 64.0f, 0f, false);
        w.Tick(130, 64.0f, 0f, false);
        w.Tick(131, 64.0f, 0f, false);
        Assert.Equal(LandingVerdict.Landed, w.Tick(132, 64.0f, 0f, false));
        Assert.Equal(LandingReason.Settled, w.Reason);
    }

    [Fact]
    public void GroundOnlyTickNeverSettlesAndTimesOut()
    {
        var w = new LandingWatch(100, 32);
        for (int t = 100; t < 132; t++)
            Assert.Equal(LandingVerdict.Pending, w.Tick(false, t));
        Assert.Equal(LandingVerdict.TimedOut, w.Tick(false, 132));
        Assert.Equal(LandingReason.Timeout, w.Reason);
    }

    [Fact]
    public void ReasonIsNoneWhilePendingAndStickyAfterwards()
    {
        var w = new LandingWatch(100, 32, settleTicks: 0, stableTicks: 1);
        Assert.Equal(LandingReason.None, w.Reason);
        w.Tick(100, 64.0f, 0f, false);
        w.Tick(101, 64.0f, 0f, false);
        Assert.Equal(LandingReason.Settled, w.Reason);
        w.Tick(500, 0f, -999f, true);
        Assert.Equal(LandingReason.Settled, w.Reason);
    }
}

public class LandingWatchDiagnosticsTests
{
    [Fact]
    public void RecordsFirstZMaxFallSpeedAndTicksTaken()
    {
        // The diagnostics line printed when a watch ends needs to show whether the pawn moved at all
        // after the teleport: first sampled Z, the largest |vz| seen, and how many ticks the watch ran.
        var w = new LandingWatch(100, 32, settleTicks: 2, stableTicks: 3);
        w.Tick(100, 10f, 0f, false);
        w.Tick(101, 10f, -12.5f, false);
        w.Tick(102, 9f, -25f, false);
        w.Tick(103, 8.5f, -0.5f, false);
        w.Tick(104, 8.5f, 0f, false);
        w.Tick(105, 8.5f, 0f, false);
        var verdict = w.Tick(106, 8.5f, 0f, false);

        Assert.Equal(LandingVerdict.Landed, verdict);
        Assert.Equal(10f, w.FirstZ);
        Assert.Equal(25f, w.MaxAbsVz);
        Assert.Equal(6, w.TicksTaken);
        Assert.Equal(new[] { 10f, 10f, 9f, 8.5f, 8.5f, 8.5f, 8.5f }, w.ZTrace);
    }

    [Fact]
    public void HoldingTheSettleTestOpenRunsToTheDeadline()
    {
        // Diagnostic hold: a stable-tick requirement longer than the deadline means the watch always
        // times out, so the trace shows what the pawn does over the whole window before it is frozen.
        var w = new LandingWatch(100, 32, settleTicks: 2, stableTicks: 40);
        LandingVerdict v = LandingVerdict.Pending;
        for (int t = 100; t <= 132; t++)
            v = w.Tick(t, 5f, 0f, false);
        Assert.Equal(LandingVerdict.TimedOut, v);
        Assert.Equal(33, w.ZTrace.Count);
    }

    [Fact]
    public void NeverMovedOnlyWhenNoSampleShowedMotion()
    {
        // A bot the engine never simulated: same Z every tick, no vertical speed.
        var still = new LandingWatch(100, 32, settleTicks: 2, stableTicks: 3);
        for (int t = 100; t <= 104; t++) still.Tick(t, 81.03f, 0f, false);
        Assert.True(still.NeverMoved(81.03f));

        // Lifted out of the floor on the first simulated tick, then at rest: it moved.
        var lifted = new LandingWatch(100, 32, settleTicks: 2, stableTicks: 3);
        lifted.Tick(100, 160.03f, 0f, false);
        for (int t = 101; t <= 104; t++) lifted.Tick(t, 161.03f, 0f, false);
        Assert.False(lifted.NeverMoved(161.03f));

        // Fell and landed back at the start height: speed was seen, so it moved.
        var fell = new LandingWatch(100, 32, settleTicks: 2, stableTicks: 3);
        fell.Tick(100, 10f, 0f, false);
        fell.Tick(101, 9.8f, -12.5f, false);
        for (int t = 102; t <= 105; t++) fell.Tick(t, 10f, 0f, false);
        Assert.False(fell.NeverMoved(10f));

        Assert.False(new LandingWatch(100, 32).NeverMoved(5f));
    }

    [Fact]
    public void FirstZIsNaNBeforeAnySample()
    {
        var w = new LandingWatch(100, 32);
        Assert.True(float.IsNaN(w.FirstZ));
        Assert.Equal(0f, w.MaxAbsVz);
        Assert.Equal(0, w.TicksTaken);
    }
}
