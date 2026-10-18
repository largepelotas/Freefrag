using OpenPrefirePrac;
using Xunit;

public class ProfileRewriteTests
{
    private static readonly string[] Profile =
    {
        "a_short mid_to_b",
        "6 0.65",
        "210.6 -297.0 15.0 0.3 53.5 0.0",
        "646.3 1180.6 0.8 -0.05 -90.5 0.0 False",
        "860.0 790.0 0.4 0.17 179.4 0.0 True # comment",
        "-1665.2 670.1 40.3",
        "-1655.8 1080.9 40.1",
    };

    [Fact]
    public void ReplacesExistingGuidingLineAndKeepsEverythingElse()
    {
        var points = new[] { (1.5f, 2.5f, 3.5f), (4f, 5f, 6f), (7f, 8f, 9.25f) };

        var result = PrefirePractice.RewriteProfileWithGuidingLine(Profile, points);

        Assert.Equal(new[]
        {
            "a_short mid_to_b",
            "6 0.65",
            "210.6 -297.0 15.0 0.3 53.5 0.0",
            "646.3 1180.6 0.8 -0.05 -90.5 0.0 False",
            "860.0 790.0 0.4 0.17 179.4 0.0 True # comment",
            "1.500000 2.500000 3.500000",
            "4.000000 5.000000 6.000000",
            "7.000000 8.000000 9.250000",
        }, result);
    }

    [Fact]
    public void FormatsWithInvariantCultureRegardlessOfThreadCulture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var result = PrefirePractice.RewriteProfileWithGuidingLine(new[] { "a", "6 0.65", "0 0 0 0 0 0" }, new[] { (1.5f, -2.25f, 1000.125f) });
            Assert.Equal("1.500000 -2.250000 1000.125000", result[^1]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void IsGuidingLinePointAcceptsOnlyThreeNumbers()
    {
        Assert.True(PrefirePractice.IsGuidingLinePoint("-1665.2 670.1 40.3"));
        Assert.True(PrefirePractice.IsGuidingLinePoint("1,2,3"));
        Assert.False(PrefirePractice.IsGuidingLinePoint("6 0.65"));
        Assert.False(PrefirePractice.IsGuidingLinePoint("646.3 1180.6 0.8 -0.05 -90.5 0.0 False"));
        Assert.False(PrefirePractice.IsGuidingLinePoint("a b c"));
        Assert.False(PrefirePractice.IsGuidingLinePoint(""));
    }

    [Fact]
    public void FarEnoughUsesStraightLineDistance()
    {
        Assert.True(PrefirePractice.FarEnough((0, 0, 0), (0, 0, 60), 50f));
        Assert.True(PrefirePractice.FarEnough((0, 0, 0), (30, 30, 30), 50f));
        Assert.False(PrefirePractice.FarEnough((0, 0, 0), (30, 30, 0), 50f));
    }
}
