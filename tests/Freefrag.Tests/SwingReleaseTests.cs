using Freefrag;
using Xunit;

public class SwingReleaseTests
{
    private static SwingState State(bool landed = true, bool released = false, bool frozen = false, int releasedTick = 0) => new()
    {
        Peek = new SwingSpec(100f, 0f, 0f, SwingSpec.NoTrigger),
        Trigger = 500f,
        HideX = 0f, HideY = 0f, HideZ = 0f,
        Landed = landed, Released = released, Frozen = frozen, ReleasedTick = releasedTick,
    };

    [Fact]
    public void EffectiveTriggerPrefersTheLineAndClamps()
    {
        // Prevents a route line's own trigger being ignored, or an out-of-range value reaching the release test.
        Assert.Equal(600f, SwingRelease.EffectiveTrigger(SwingSpec.NoTrigger, 600));
        Assert.Equal(450f, SwingRelease.EffectiveTrigger(450f, 600));
        Assert.Equal(100f, SwingRelease.EffectiveTrigger(50f, 600));
        Assert.Equal(3000f, SwingRelease.EffectiveTrigger(SwingSpec.NoTrigger, 9000));
    }

    [Fact]
    public void ReleaseWaitsForLanding()
    {
        // Prevents a swinger spawned with the owner already in range from running while still in mid-air.
        Assert.False(SwingRelease.ShouldRelease(State(landed: false), 100f, 0f, 0f));
        Assert.True(SwingRelease.ShouldRelease(State(landed: true), 100f, 0f, 0f));
    }

    [Fact]
    public void ReleaseUsesThreeDimensionalDistance()
    {
        // Prevents a bot being released by a player standing on a different floor directly above its spot.
        Assert.False(SwingRelease.ShouldRelease(State(), 0f, 0f, 600f));
        Assert.True(SwingRelease.ShouldRelease(State(), 0f, 0f, 400f));
    }

    [Fact]
    public void ReleasedOnceStaysReleased()
    {
        // Prevents a bot being released (and its ReleasedTick reset) every tick the owner stays in range.
        Assert.False(SwingRelease.ShouldRelease(State(released: true), 100f, 0f, 0f));
    }

    [Fact]
    public void ArrivedWithinThirtyUnitsOfThePeekPoint()
    {
        // Prevents a bot being frozen before it is released, frozen twice, or stopped far short of its peek point.
        Assert.False(SwingRelease.Arrived(State(), 100f, 0f, 0f, 10));
        Assert.True(SwingRelease.Arrived(State(released: true), 129f, 0f, 0f, 10));
        Assert.False(SwingRelease.Arrived(State(released: true), 131f, 0f, 0f, 10));
        Assert.False(SwingRelease.Arrived(State(released: true, frozen: true), 100f, 0f, 0f, 10));
    }

    [Fact]
    public void ArrivedAfterTimeCap()
    {
        // Prevents a swinger whose nav path never touches its peek point from running forever.
        var s = State(released: true, releasedTick: 1000);
        Assert.False(SwingRelease.Arrived(s, 140f, 0f, 0f, 1127));
        Assert.False(SwingRelease.TimedOut(s, 1127));
        Assert.True(SwingRelease.Arrived(s, 140f, 0f, 0f, 1128));
        Assert.True(SwingRelease.TimedOut(s, 1128));
        // An unreleased or already frozen swinger never times out.
        Assert.False(SwingRelease.TimedOut(State(), 5000));
        Assert.False(SwingRelease.TimedOut(State(released: true, frozen: true, releasedTick: 0), 5000));
    }

    [Fact]
    public void ClearDropsStates()
    {
        // Prevents swing state surviving a route exit, which would keep excluding a reused bot slot from the aim lock.
        var swingers = new Dictionary<int, SwingState>
        {
            [3] = State(released: true),
            [4] = State(released: true, frozen: true),
            [5] = State(),
        };
        var running = SwingRelease.TakeStates(swingers);
        Assert.Empty(swingers);
        Assert.Equal(new[] { 3 }, running);
    }
}
