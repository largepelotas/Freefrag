using System.Globalization;
using Freefrag;
using Xunit;

public class ScoreFormatTests
{
    [Fact]
    public void SecondsUsesInvariantCulture()
    {
        // Prevents a comma-decimal server locale from printing "12,34s" in chat and logs.
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("12.34s", ScoreFormat.Seconds(12340));
            Assert.Equal("0.05s", ScoreFormat.Seconds(50));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void SettingsTagMapsDifficultyAndHeadshotFlag()
    {
        // Prevents the tag from showing the internal HealingMethod instead of the user-facing df number.
        Assert.Equal("full/df3/AK47/hs", ScoreFormat.SettingsTag(1, 3, 2, true));
        Assert.Equal("rand/df6/Random", ScoreFormat.SettingsTag(0, 0, 0, false));
    }

    [Fact]
    public void FinishLineNewPersonalBestWithPrevious()
    {
        // Prevents a beaten record from omitting the time that was beaten.
        var r = new RunResult(12340, 5, 4, 0);
        Assert.Equal("Route done in 12.34s, 0 deaths, 80% HS (4/5). New personal best (was 13.10s).", ScoreFormat.FinishLine(r, 13100));
    }

    [Fact]
    public void FinishLineFirstEverRun()
    {
        // Prevents a first run from printing a bogus "was" time.
        var r = new RunResult(12340, 5, 4, 0);
        Assert.Equal("Route done in 12.34s, 0 deaths, 80% HS (4/5). New personal best.", ScoreFormat.FinishLine(r, null));
    }

    [Fact]
    public void FinishLineSlowerRunShowsBestAndSingularDeath()
    {
        // Prevents "1 deaths" and a missing personal-best reminder on a slower run.
        var r = new RunResult(12340, 5, 4, 1);
        Assert.Equal("Route done in 12.34s, 1 death, 80% HS (4/5). Personal best: 11.90s.", ScoreFormat.FinishLine(r, 11900));
    }

    [Fact]
    public void TopLineIncludesRankNameAndSettings()
    {
        // Prevents leaderboard rows from hiding the settings that make times comparable.
        var row = new ScoreRow(1, 76561198000000000UL, "Quinn", "de_mirage", "silo_to_mini", 1, 3, 2, true,
            11900, 5, 0, 5, 4, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("#1 Quinn 11.90s 0d 80%HS [full/df3/AK47/hs]", ScoreFormat.TopLine(1, row));
    }

    [Fact]
    public void StatsLineSingularRun()
    {
        // Prevents "1 runs" in the stats list.
        Assert.Equal("Attack A site from MINI: best 11.90s, 1 run", ScoreFormat.StatsLine("Attack A site from MINI", 11900, 1));
        Assert.Equal("Attack A site from MINI: best 11.90s, 7 runs", ScoreFormat.StatsLine("Attack A site from MINI", 11900, 7));
    }

    [Fact]
    public void TopLineTagsCompRunsOnly()
    {
        // Prevents comp times from looking identical to casual ones in the shared leaderboard.
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var casual = new ScoreRow(1, 76561198000000000UL, "Quinn", "de_mirage", "silo_to_mini", 1, 3, 2, false, 11900, 5, 0, 5, 4, at);
        var comp = casual with { Comp = true };
        Assert.Equal("#1 Quinn 11.90s 0d 80%HS [full/df3/AK47]", ScoreFormat.TopLine(1, casual));
        Assert.Equal("#2 Quinn 11.90s 0d 80%HS [full/df3/AK47] [comp]", ScoreFormat.TopLine(2, comp));
    }
}
