using System.Globalization;
using Freefrag;
using Xunit;

public class SwingSpecTests
{
    private static string[] Tokens(string line) => line.Split(new[] { ' ', ',', '\t' });

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Freefrag.csproj")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(parts.Prepend(dir!).ToArray());
    }

    private static string MakeRouteDir(string map, string practice, IEnumerable<string> lines)
    {
        var dir = Path.Combine(Path.GetTempPath(), "freefrag-swing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "maps", map));
        File.WriteAllLines(Path.Combine(dir, "maps", map, practice + ".txt"), lines);
        return dir;
    }

    private static readonly string[] Header = { "", "2 1.0", "10 20 30 1 2 0" };

    // Prevents: a peek spec being read with the wrong columns, so the bot runs to the wrong point.
    [Fact]
    public void ParsesPeekWithoutTrigger()
    {
        var ok = SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 10.5 -20 30"), 7, out var spec, out var error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new SwingSpec(10.5f, -20f, 30f, SwingSpec.NoTrigger), spec);
    }

    // Prevents: the per-line trigger distance being ignored in favour of the config default.
    [Fact]
    public void ParsesPeekWithTrigger()
    {
        var ok = SwingSpec.TryParse(Tokens("1 2 3 4 5 6 True peek 1 2 3 trigger 450"), 7, out var spec, out _);
        Assert.True(ok);
        Assert.Equal(450f, spec.Trigger);
    }

    // Prevents: a route author typing a trigger outside 100..3000 and the route being rejected or the bot never releasing.
    [Fact]
    public void TriggerIsClamped()
    {
        Assert.True(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 3 trigger 50"), 7, out var low, out _));
        Assert.Equal(100f, low.Trigger);
        Assert.True(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 3 trigger 9000"), 7, out var high, out _));
        Assert.Equal(3000f, high.Trigger);
    }

    // Prevents: an ordinary static bot line (or its comment) being reported as a malformed swinger.
    [Fact]
    public void NoPeekTokenIsNotAnError()
    {
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False # comment"), 7, out _, out var error));
        Assert.Null(error);
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False"), 7, out _, out error));
        Assert.Null(error);
        // A peek inside a comment is only a comment.
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False # peek 1 2 3"), 7, out _, out error));
        Assert.Null(error);
    }

    // Prevents: "peek 1 2" being half-parsed into a swinger that runs to garbage coordinates.
    [Fact]
    public void MalformedPeekIsAnError()
    {
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2"), 7, out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("peek", error);
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 x"), 7, out _, out error));
        Assert.Contains("peek", error);
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 3 trigger"), 7, out _, out error));
        Assert.Contains("peek", error);
        Assert.False(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 NaN"), 7, out _, out error));
        Assert.Contains("peek", error);
    }

    // Prevents: the trailing "# left side" comment being read as part of the spec.
    [Fact]
    public void CommentAfterPeekIsIgnored()
    {
        Assert.True(SwingSpec.TryParse(Tokens("1 2 3 4 5 6 False peek 1 2 3 # left side"), 7, out var spec, out var error));
        Assert.Null(error);
        Assert.Equal(new SwingSpec(1f, 2f, 3f, SwingSpec.NoTrigger), spec);
    }

    // Prevents: a German-locale server writing "1,5" coordinates that split into extra tokens on the next load.
    [Fact]
    public void FormatRoundTrips()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var plain = new SwingSpec(1.5f, -2.25f, 3f, SwingSpec.NoTrigger);
            Assert.Equal("peek 1.500000 -2.250000 3.000000", SwingSpec.Format(plain));
            var withTrigger = new SwingSpec(1.5f, -2.25f, 3f, 450f);
            Assert.Equal("peek 1.500000 -2.250000 3.000000 trigger 450", SwingSpec.Format(withTrigger));

            foreach (var spec in new[] { plain, withTrigger })
            {
                var line = "0 0 0 0 0 0 False " + SwingSpec.Format(spec);
                Assert.True(SwingSpec.TryParse(Tokens(line), 7, out var back, out _));
                Assert.Equal(spec, back);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    // Prevents: the new parser changing how any route without a peek token is read (shipped routes are the contract).
    [Fact]
    public void ParserKeepsExistingRoutesIdentical()
    {
        var shipped = File.ReadAllLines(RepoFile("maps", "de_dust2", "a_long.txt"));
        var dir = MakeRouteDir("de_test", "copy", shipped);
        try
        {
            var practice = new PrefirePractice(dir, "de_test", "copy");

            Assert.Equal(38, practice.Targets.Count);
            Assert.All(practice.Targets, t => Assert.False(t.IsSwinger));
            Assert.All(practice.Targets, t => Assert.Null(t.Peek));
            Assert.Equal(646.371460f, practice.Targets[0].Position.X);
            Assert.Equal(1180.628906f, practice.Targets[0].Position.Y);
            Assert.False(practice.Targets[0].IsCrouching);
            Assert.Equal(306.032684f, practice.Targets[^1].Position.X);
            Assert.Equal(1921.448242f, practice.Targets[^1].Position.Y);
            Assert.Equal(40.576813f, practice.Targets[^1].Rotation.Y);
            Assert.Equal(6, practice.NumBots);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Prevents: a peek line loading as a static bot, so the swing never happens.
    [Fact]
    public void ParserReadsSwingerLine()
    {
        var dir = MakeRouteDir("de_test", "mine", Header.Concat(new[]
        {
            "100 200 300 4 5 0 False",
            "101 201 301 6 7 0 True peek 500 600 700 trigger 450 # comment",
        }));
        try
        {
            var practice = new PrefirePractice(dir, "de_test", "mine");

            Assert.Equal(2, practice.Targets.Count);
            Assert.False(practice.Targets[0].IsSwinger);
            var swinger = practice.Targets[1];
            Assert.True(swinger.IsSwinger);
            Assert.True(swinger.IsCrouching);
            Assert.Equal(101f, swinger.Position.X);
            Assert.Equal(new SwingSpec(500f, 600f, 700f, 450f), swinger.Peek);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Prevents: a typo in one peek spec crashing the whole route load or leaving a half-parsed swinger.
    [Fact]
    public void MalformedPeekFallsBackToStaticWithWarning()
    {
        var dir = MakeRouteDir("de_test", "bad", Header.Concat(new[]
        {
            "100 200 300 4 5 0 False peek 1 2",
            "101 201 301 6 7 0 True",
        }));
        var oldOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var practice = new PrefirePractice(dir, "de_test", "bad");

            Assert.Equal(2, practice.Targets.Count);
            Assert.False(practice.Targets[0].IsSwinger);
            Assert.Null(practice.Targets[0].Peek);
            Assert.Contains("de_test/bad line 4", captured.ToString());
            Assert.Contains("static", captured.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Directory.Delete(dir, true);
        }
    }
}
