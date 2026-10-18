using Freefrag;
using Xunit;

public class RouteEditorTests
{
    private static readonly string[] Profile =
    {
        "a_short mid_to_b",
        "6 0.65",
        "210.6 -297.0 15.0 0.3 53.5 0.0",
        "646.3 1180.6 0.8 -0.05 -90.5 0.0 False",
        "860.0 790.0 0.4 0.17 179.4 0.0 True # comment",
        "1 1 1 0 0 0 False",
        "2 2 2 0 0 0 False",
        "3 3 3 0 0 0 True",
        "4 4 4 0 0 0 False",
        "-1665.2 670.1 40.3",
        "-1655.8 1080.9 40.1",
        "",
    };

    private static readonly RouteEditor.DraftBot[] OneBot =
    {
        new(1.5f, -2.25f, 3f, 0.5f, 90f, true),
    };

    // Prevents: a German-locale server writing "1,500000" so the profile no longer parses.
    [Fact]
    public void FormatBotLineUsesF6AndInvariantCulture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("1.500000 -2.250000 3.000000 0.500000 90.000000 0.000000 True",
                RouteEditor.FormatBotLine(new RouteEditor.DraftBot(1.5f, -2.25f, 3f, 0.5f, 90f, true)));
            Assert.EndsWith(" False", RouteEditor.FormatBotLine(new RouteEditor.DraftBot(0, 0, 0, 0, 0, false)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    // Prevents: new bots landing after the guiding-line joints, the comment and header lines being
    // disturbed, or a spurious clamp of 6 when 39 targets exist.
    [Fact]
    public void InsertBotsGoesAfterLastTargetBeforeFirstJoint()
    {
        var result = RouteEditor.InsertBots(Profile, OneBot);

        Assert.Equal(new[]
        {
            "a_short mid_to_b",
            "6 0.65",
            "210.6 -297.0 15.0 0.3 53.5 0.0",
            "646.3 1180.6 0.8 -0.05 -90.5 0.0 False",
            "860.0 790.0 0.4 0.17 179.4 0.0 True # comment",
            "1 1 1 0 0 0 False",
            "2 2 2 0 0 0 False",
            "3 3 3 0 0 0 True",
            "4 4 4 0 0 0 False",
            "1.500000 -2.250000 3.000000 0.500000 90.000000 0.000000 True",
            "-1665.2 670.1 40.3",
            "-1655.8 1080.9 40.1",
            "",
        }, result);
    }

    // Prevents: a 3-bots-in-play route with one target waiting forever for bots that do not exist.
    [Fact]
    public void InsertBotsIntoEmptyRouteAppendsAfterLine3AndClampsNumBots()
    {
        var empty = RouteEditor.NewProfile(3, 1f, 2f, 3f, 4f, 5f);

        var one = RouteEditor.InsertBots(empty, OneBot);
        Assert.Equal("1 1.000000", one[1]);
        Assert.Equal(4, one.Count);
        Assert.Equal(empty[2], one[2]);
        Assert.Equal("1.500000 -2.250000 3.000000 0.500000 90.000000 0.000000 True", one[3]);

        var three = RouteEditor.InsertBots(empty, new[] { OneBot[0], OneBot[0], OneBot[0] });
        Assert.Equal("3 1.000000", three[1]);
        Assert.Equal(6, three.Count);
    }

    // Prevents: blank trailing lines ending up between the old targets and the new bots.
    [Fact]
    public void InsertBotsWithoutGuidingLineAppendsAtEndDroppingTrailingBlanks()
    {
        var lines = new[]
        {
            "",
            "2 1.0",
            "0 0 0 0 0 0",
            "1 1 1 0 0 0 False",
            "",
            "   ",
        };

        var result = RouteEditor.InsertBots(lines, OneBot);

        Assert.Equal(new[]
        {
            "",
            "2 1.0",
            "0 0 0 0 0 0",
            "1 1 1 0 0 0 False",
            "1.500000 -2.250000 3.000000 0.500000 90.000000 0.000000 True",
        }, result);
    }

    [Fact]
    public void ClampNumBotsNeverExceedsTargets()
    {
        Assert.Equal(6, RouteEditor.ClampNumBots(6, 39));
        Assert.Equal(1, RouteEditor.ClampNumBots(3, 1));
        Assert.Equal(0, RouteEditor.ClampNumBots(0, 5));
    }

    [Fact]
    public void NewProfileHasExactlyThreeLinesInDocumentedFormat()
    {
        var lines = RouteEditor.NewProfile(3, 10.5f, -20f, 30.25f, 1f, -90f);

        Assert.Equal(new[]
        {
            "",
            "3 1.000000",
            "10.500000 -20.000000 30.250000 1.000000 -90.000000 0.000000",
        }, lines);
    }

    // Prevents: a route name such as "../x" escaping the maps directory.
    [Fact]
    public void IsValidRouteNameAcceptsOnlySafeNames()
    {
        Assert.True(RouteEditor.IsValidRouteName("a_long"));
        Assert.True(RouteEditor.IsValidRouteName("B-site-2"));
        Assert.True(RouteEditor.IsValidRouteName(new string('a', 40)));
        Assert.False(RouteEditor.IsValidRouteName(""));
        Assert.False(RouteEditor.IsValidRouteName("a b"));
        Assert.False(RouteEditor.IsValidRouteName("../x"));
        Assert.False(RouteEditor.IsValidRouteName(new string('a', 41)));
        Assert.False(RouteEditor.IsValidRouteName("a.txt"));
        Assert.False(RouteEditor.IsValidRouteName("a\n"));
    }

    // Prevents: the editor writing a profile the real parser rejects or misreads.
    // The generated empty first line parses to no incompatible routes.
    [Fact]
    public void ParserReadsNewProfilePlusInsertedBots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "freefrag-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "maps", "de_test"));
        try
        {
            var lines = RouteEditor.InsertBots(RouteEditor.NewProfile(3, 10f, 20f, 30f, 1f, 2f), new[]
            {
                new RouteEditor.DraftBot(100f, 200f, 300f, 4f, 5f, false),
                new RouteEditor.DraftBot(101f, 201f, 301f, 6f, 7f, true),
            });
            File.WriteAllLines(Path.Combine(dir, "maps", "de_test", "mine.txt"), lines);

            var practice = new PrefirePractice(dir, "de_test", "mine");

            Assert.Equal(2, practice.Targets.Count);
            Assert.Equal(2, practice.NumBots);
            Assert.Equal(1f, practice.SpawnRatio);
            Assert.Equal(10f, practice.Player.Position.X);
            Assert.Equal(30f, practice.Player.Position.Z);
            Assert.Equal(2f, practice.Player.Rotation.Y);
            Assert.True(practice.Targets[1].IsCrouching);
            Assert.Empty(practice.IncompatiblePractices);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
