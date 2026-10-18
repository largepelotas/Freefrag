using Freefrag;
using Xunit;

public class BotOwnershipPolicyTests
{
    [Fact]
    public void ActivatedStartsSweepAndHoldsQuota()
    {
        Assert.Equal(OwnershipAction.StartSweep | OwnershipAction.HoldQuotaZero, BotOwnershipPolicy.ForTransition(ModeTransition.Activated));
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
        Assert.False(BotOwnershipPolicy.ForTransition(ModeTransition.Deactivated).HasFlag(OwnershipAction.HoldQuotaZero));
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
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    [InlineData(true, 10, true)]
    [InlineData(false, 0, false)]
    [InlineData(false, 10, false)]
    public void QuotaNeedsResetOnlyWhenActiveAndNonZero(bool active, int quota, bool expected)
        => Assert.Equal(expected, BotOwnershipPolicy.QuotaNeedsReset(active, quota));
}
