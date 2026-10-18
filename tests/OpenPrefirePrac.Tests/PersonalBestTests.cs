using OpenPrefirePrac;
using Xunit;

public class PersonalBestTests
{
    [Fact]
    public void FirstRunIsAlwaysBetter()
    {
        // Prevents the very first run on a route from being reported as "not a personal best".
        Assert.True(PersonalBest.IsBetter(30000, null));
    }

    [Fact]
    public void EqualTimeIsNotBetter()
    {
        // Prevents a tied time from announcing a new personal best.
        Assert.False(PersonalBest.IsBetter(12340, 12340));
    }

    [Fact]
    public void FasterIsBetterSlowerIsNot()
    {
        // Prevents the comparison from being inverted (lower time is better).
        Assert.True(PersonalBest.IsBetter(12000, 12340));
        Assert.False(PersonalBest.IsBetter(13000, 12340));
    }
}
