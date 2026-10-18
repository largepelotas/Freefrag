using Freefrag;
using Xunit;

public class BotOwnershipPolicyTests
{
    [Fact]
    public void ActivatedStartsSweepAndHoldsQuota()
    {
        Assert.Equal(OwnershipAction.StartSweep | OwnershipAction.HoldQuotaAtTarget, BotOwnershipPolicy.ForTransition(ModeTransition.Activated));
    }

    [Fact]
    public void DeactivatedStopsSweepAndReleasesOwnedBots()
    {
        Assert.Equal(OwnershipAction.StopSweep | OwnershipAction.ReleaseOwnedBots, BotOwnershipPolicy.ForTransition(ModeTransition.Deactivated));
    }

    [Fact]
    public void NoTransitionDoesNothing()
    {
        Assert.Equal(OwnershipAction.None, BotOwnershipPolicy.ForTransition(ModeTransition.None));
    }

    [Fact]
    public void DeactivatedNeverHoldsTheQuota()
    {
        Assert.False(BotOwnershipPolicy.ForTransition(ModeTransition.Deactivated).HasFlag(OwnershipAction.HoldQuotaAtTarget));
        Assert.False(BotOwnershipPolicy.ForTransition(ModeTransition.Deactivated).HasFlag(OwnershipAction.StartSweep));
    }

    [Fact]
    public void ActivatedNeverReleasesOwnedBots()
    {
        Assert.False(BotOwnershipPolicy.ForTransition(ModeTransition.Activated).HasFlag(OwnershipAction.ReleaseOwnedBots));
        Assert.False(BotOwnershipPolicy.ForTransition(ModeTransition.Activated).HasFlag(OwnershipAction.StopSweep));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SweepAllowedOnlyWhileActive(bool active, bool expected) => Assert.Equal(expected, BotOwnershipPolicy.SweepAllowed(active));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void KickAllOnUnloadOnlyWhileActive(bool active, bool expected) => Assert.Equal(expected, BotOwnershipPolicy.KickAllOnUnload(active));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 5)]
    [InlineData(0, 3, 3)]
    [InlineData(4, 2, 6)]
    [InlineData(-1, -2, 0)]
    public void QuotaTargetIsOwnedPlusPending(int owned, int pending, int expected)
        => Assert.Equal(expected, BotOwnershipPolicy.QuotaTarget(owned, pending));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(5, 3)]
    public void QuotaTargetIsNeverZeroWhileBotsAreOwnedOrPending(int owned, int pending)
        => Assert.True(BotOwnershipPolicy.QuotaTarget(owned, pending) > 0);

    [Fact]
    public void KickingAnOwnedBotLowersTheTargetByOne()
        => Assert.Equal(BotOwnershipPolicy.QuotaTarget(5, 2) - 1, BotOwnershipPolicy.QuotaTarget(4, 2));

    [Theory]
    [InlineData(true, 6, 0, true)]
    [InlineData(true, 0, 6, true)]
    [InlineData(true, 6, 6, false)]
    [InlineData(true, 0, 0, false)]
    [InlineData(false, 6, 0, false)]
    [InlineData(false, 10, 3, false)]
    public void QuotaNeedsSyncOnlyWhenActiveAndOffTarget(bool active, int quota, int target, bool expected)
        => Assert.Equal(expected, BotOwnershipPolicy.QuotaNeedsSync(active, quota, target));
}
