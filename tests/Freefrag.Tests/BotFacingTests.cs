using Freefrag;
using Xunit;

public class BotFacingTests
{
    [Fact]
    public void DueXIsYawZeroPitchZero()
    {
        var (pitch, yaw) = BotFacing.AnglesTo(0, 0, 0, 100, 0, 0);
        Assert.Equal(0f, yaw, precision: 3);
        Assert.Equal(0f, pitch, precision: 3);
    }

    [Fact]
    public void DueYIsYawNinety()
    {
        var (_, yaw) = BotFacing.AnglesTo(0, 0, 0, 0, 50, 0);
        Assert.Equal(90f, yaw, precision: 3);
    }

    [Fact]
    public void DueNegativeXIsYaw180InMagnitude()
    {
        var (_, yaw) = BotFacing.AnglesTo(0, 0, 0, -100, 0, 0);
        Assert.Equal(180f, Math.Abs(yaw), precision: 3);
    }

    [Fact]
    public void TargetAboveGivesNegativePitch()
    {
        var (pitch, _) = BotFacing.AnglesTo(0, 0, 0, 100, 0, 100);
        Assert.Equal(-45f, pitch, precision: 3);
    }

    [Fact]
    public void TargetBelowGivesPositivePitch()
    {
        var (pitch, _) = BotFacing.AnglesTo(0, 0, 0, 100, 0, -100);
        Assert.Equal(45f, pitch, precision: 3);
    }

    [Fact]
    public void StraightUpIsMinusNinetyPitch()
    {
        var (pitch, _) = BotFacing.AnglesTo(0, 0, 0, 0, 0, 100);
        Assert.Equal(-90f, pitch, precision: 3);
    }

    [Fact]
    public void CoincidentPointsGiveZero()
    {
        var (pitch, yaw) = BotFacing.AnglesTo(5, 6, 7, 5, 6, 7);
        Assert.Equal(0f, pitch, precision: 3);
        Assert.Equal(0f, yaw, precision: 3);
    }
}
