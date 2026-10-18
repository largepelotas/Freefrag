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
}
