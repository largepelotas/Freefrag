using Freefrag;
using Xunit;

public class GameModeListTests
{
    private const string Sample = """
    {
      "GameModes": {
        "Style": "center",
        "Default": { "Name": "Casual", "Config": "casual.cfg" },
        "List": [
          { "Name": "Prefire", "Config": "prefire.cfg", "DefaultMap": "de_dust2" },
          { "Name": "Gun Game", "Config": "gungame.cfg", "Maps": { "de_dust2": "1" } },
          { "Name": "Retakes", "Config": "sub/retakes-2.cfg" }
        ]
      },
      "Other": { "List": [ { "Name": "Nope", "Config": "nope.cfg" } ] }
    }
    """;

    private static IReadOnlyList<GameMode> SampleModes() => GameModeList.Parse(Sample);

    [Fact]
    public void ParseKeepsFileOrderAndIgnoresExtraKeys()
    {
        // Prevents the menu from reordering the admin's list, or choking on keys GameModeManager owns.
        var modes = SampleModes();

        Assert.Equal(new[] { "Prefire", "Gun Game", "Retakes" }, modes.Select(m => m.Name));
        Assert.Equal(new[] { "prefire.cfg", "gungame.cfg", "sub/retakes-2.cfg" }, modes.Select(m => m.Config));
    }

    [Fact]
    public void ParseSkipsEntriesMissingNameOrConfig()
    {
        // Prevents a half-written entry from showing as a blank menu row that does nothing.
        var modes = GameModeList.Parse("""
        { "GameModes": { "List": [
          { "Name": "A" },
          { "Config": "b.cfg" },
          { "Name": "  ", "Config": "c.cfg" },
          { "Name": "D", "Config": "" },
          { "Name": 5, "Config": "e.cfg" },
          { "Name": "Ok", "Config": "ok.cfg" }
        ] } }
        """);

        Assert.Equal(new[] { "Ok" }, modes.Select(m => m.Name));
    }

    [Fact]
    public void ParseSkipsUnsafeConfig()
    {
        // Prevents a hostile or typo'd config value from reaching the server console.
        var modes = GameModeList.Parse("""
        { "GameModes": { "List": [
          { "Name": "Evil", "Config": "a; quit" },
          { "Name": "Up", "Config": "../x.cfg" },
          { "Name": "Good", "Config": "good.cfg" }
        ] } }
        """);

        Assert.Equal(new[] { "Good" }, modes.Select(m => m.Name));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "GameModes": {} }""")]
    [InlineData("""{ "GameModes": { "List": null } }""")]
    [InlineData("""{ "GameModes": [] }""")]
    [InlineData("[]")]
    public void ParseReturnsEmptyWhenSectionOrListIsAbsent(string json)
    {
        // Prevents a config without a mode list from crashing the menu instead of showing "none found".
        Assert.Empty(GameModeList.Parse(json));
    }

    [Fact]
    public void ParsePropertyNamesAreCaseInsensitive()
    {
        // Prevents "gamemodes"/"list"/"name" spelling differences from hiding every mode.
        var modes = GameModeList.Parse("""{ "gamemodes": { "LIST": [ { "NAME": "X", "config": "x.cfg" } ] } }""");

        var mode = Assert.Single(modes);
        Assert.Equal(new GameMode("X", "x.cfg"), mode);
    }

    [Fact]
    public void ParseLetsMalformedJsonPropagate()
    {
        // The caller logs this; swallowing it here would hide a broken file behind "no modes".
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => GameModeList.Parse("{ not json"));
    }

    [Theory]
    [InlineData("prefire.cfg", true)]
    [InlineData("sub/mode-1.cfg", true)]
    [InlineData("under_score.cfg", true)]
    [InlineData("", false)]
    [InlineData("../x.cfg", false)]
    [InlineData("a/../x.cfg", false)]
    [InlineData("a; quit", false)]
    [InlineData("a;quit", false)]
    [InlineData("a b.cfg", false)]
    [InlineData("/abs.cfg", false)]
    [InlineData("x\".cfg", false)]
    [InlineData("a\tb.cfg", false)]
    public void IsSafeConfigOnlyAllowsPlainRelativePaths(string config, bool expected)
    {
        // Prevents command injection through the exec line.
        Assert.Equal(expected, GameModeList.IsSafeConfig(config));
    }

    [Fact]
    public void SwitchCommandExecsTheConfig()
    {
        // Prevents the switch from drifting from what the admin types by rcon.
        Assert.Equal("exec prefire.cfg", GameModeList.SwitchCommand(new GameMode("Prefire", "prefire.cfg")));
    }

    [Theory]
    [InlineData("Prefire", "Prefire")]
    [InlineData("prefire", "Prefire")]
    [InlineData("PREFIRE", "Prefire")]
    [InlineData("prefire.cfg", "Prefire")]
    [InlineData("gungame", "Gun Game")]
    [InlineData("gungame.cfg", "Gun Game")]
    [InlineData("Gun Game", "Gun Game")]
    [InlineData("gun game", "Gun Game")]
    [InlineData("sub/retakes-2", "Retakes")]
    [InlineData("  prefire ", "Prefire")]
    public void FindMatchesNameOrConfigLoosely(string query, string expectedName)
    {
        // Prevents "!freefrag modes prefire" from failing because of case, spaces or a missing .cfg.
        var found = GameModeList.Find(SampleModes(), query);

        Assert.NotNull(found);
        Assert.Equal(expectedName, found!.Name);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("")]
    [InlineData("   ")]
    public void FindReturnsNullOnMiss(string query)
    {
        // Prevents a typo from switching the server to some other mode.
        Assert.Null(GameModeList.Find(SampleModes(), query));
    }

    [Fact]
    public void FindPrefersNameOverConfigMatch()
    {
        // Prevents a config named like another mode from shadowing that mode's own name.
        var modes = new[] { new GameMode("A", "b.cfg"), new GameMode("b", "a.cfg") };

        Assert.Equal("b", GameModeList.Find(modes, "b")!.Name);
    }

    private static string Expected(string baseDir) =>
        Path.Combine(Path.GetFullPath(baseDir), "configs", "plugins", "GameModeManager", "GameModeManager.json");

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void FindConfigPathWalksUpFromTheModuleDirectory(int levelsUp)
    {
        // Prevents the detection from failing for either install location (plugins/Freefrag or plugins/disabled/Freefrag).
        var module = Path.GetFullPath(Path.Combine("srv", "css", "plugins", "disabled", "Freefrag"));
        var dir = module;
        for (var i = 0; i < levelsUp; i++) dir = Path.GetDirectoryName(dir)!;
        var expected = Expected(dir);

        var found = GameModeList.FindConfigPath(module, path => path == expected);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindConfigPathReturnsNullBeyondFourLevels()
    {
        // Prevents an unbounded walk up the filesystem picking up some unrelated config.
        var module = Path.GetFullPath(Path.Combine("a", "b", "c", "d", "e", "f", "Freefrag"));
        var dir = module;
        for (var i = 0; i < 5; i++) dir = Path.GetDirectoryName(dir)!;
        var tooFar = Expected(dir);

        Assert.Null(GameModeList.FindConfigPath(module, path => path == tooFar));
        Assert.Null(GameModeList.FindConfigPath(module, _ => false));
    }

    [Fact]
    public void FindConfigPathPrefersTheNearestMatch()
    {
        // Prevents a stray outer config from beating the one next to the plugin.
        var module = Path.GetFullPath(Path.Combine("srv", "css", "plugins", "Freefrag"));
        var near = Expected(Path.GetDirectoryName(Path.GetDirectoryName(module))!);
        var far = Expected(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(module)))!);

        Assert.Equal(near, GameModeList.FindConfigPath(module, p => p == near || p == far));
    }
}
