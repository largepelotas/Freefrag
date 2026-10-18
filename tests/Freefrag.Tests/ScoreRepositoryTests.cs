using Freefrag;
using Xunit;

public class ScoreRepositoryTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();
    private readonly ScoreRepository _repo;

    public ScoreRepositoryTests()
    {
        _repo = new ScoreRepository(_path);
        _repo.EnsureCreated();
    }

    public void Dispose()
    {
        ScoreRepository.ReleaseAll();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static ScoreRow Row(ulong steam = 1, string route = "silo_to_mini", int mode = 1, int diff = 3, int ms = 12000,
        string name = "Quinn", int hsKills = 4, DateTime? at = null, bool comp = false) =>
        new(0, steam, name, "de_mirage", route, mode, diff, 2, false, ms, 5, 0, 5, hsKills,
            at ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), comp);

    [Fact]
    public void EnsureCreatedIsIdempotent()
    {
        // Prevents plugin reload from failing on an existing scores table.
        _repo.EnsureCreated();
        _repo.EnsureCreated();
        _repo.Insert(Row());
        _repo.EnsureCreated();
        Assert.Single(_repo.Top("de_mirage", "silo_to_mini", 10));
    }

    [Fact]
    public void InsertReturnsIncreasingIdsAndRoundTripsFields()
    {
        // Prevents columns from being swapped between Insert and Top.
        var a = _repo.Insert(Row(steam: 76561198000000001UL, ms: 12345, name: "Alice", hsKills: 3));
        var b = _repo.Insert(Row(ms: 20000));
        Assert.True(b > a);

        var top = _repo.Top("de_mirage", "silo_to_mini", 10);
        var first = top[0];
        Assert.Equal(a, first.Id);
        Assert.Equal(76561198000000001UL, first.SteamId);
        Assert.Equal("Alice", first.PlayerName);
        Assert.Equal(12345, first.ElapsedMs);
        Assert.Equal(3, first.HeadshotKills);
        Assert.Equal(1, first.TrainingMode);
        Assert.Equal(3, first.Difficulty);
        Assert.Equal(DateTimeKind.Utc, first.FinishedAtUtc.Kind);
    }

    [Fact]
    public void BestForOnlyMatchesSameModeAndDifficulty()
    {
        // Prevents an easy-difficulty time from being compared against a hard-difficulty personal best.
        _repo.Insert(Row(ms: 15000));
        _repo.Insert(Row(ms: 12000));
        _repo.Insert(Row(ms: 9000, diff: 5));
        _repo.Insert(Row(ms: 8000, mode: 0));
        _repo.Insert(Row(ms: 7000, steam: 2));

        Assert.Equal(12000, _repo.BestFor(1, "de_mirage", "silo_to_mini", 1, 3));
        Assert.Equal(9000, _repo.BestFor(1, "de_mirage", "silo_to_mini", 1, 5));
        Assert.Null(_repo.BestFor(1, "de_mirage", "silo_to_mini", 1, 0));
        Assert.Null(_repo.BestFor(1, "de_mirage", "other_route", 1, 3));
    }

    [Fact]
    public void TopIsAscendingAndLimited()
    {
        // Prevents the leaderboard from listing slow runs first or ignoring the row limit.
        _repo.Insert(Row(ms: 15000, name: "C"));
        _repo.Insert(Row(ms: 11000, name: "A"));
        _repo.Insert(Row(ms: 13000, name: "B"));
        _repo.Insert(Row(ms: 9000, route: "elsewhere", name: "X"));

        var top = _repo.Top("de_mirage", "silo_to_mini", 2);

        Assert.Equal(new[] { "A", "B" }, top.Select(r => r.PlayerName).ToArray());
    }

    [Fact]
    public void TopBreaksTiesByEarlierFinish()
    {
        // Prevents a later tie from outranking the player who set the time first.
        _repo.Insert(Row(ms: 11000, name: "Late", at: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        _repo.Insert(Row(ms: 11000, name: "Early", at: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var top = _repo.Top("de_mirage", "silo_to_mini", 5);

        Assert.Equal("Early", top[0].PlayerName);
    }

    [Fact]
    public void PlayerStatsGroupPerRouteWithCounts()
    {
        // Prevents the stats list from mixing routes, other players, or miscounting runs.
        _repo.Insert(Row(ms: 15000));
        _repo.Insert(Row(ms: 12000));
        _repo.Insert(Row(ms: 20000, route: "a_route"));
        _repo.Insert(Row(ms: 5000, steam: 2));

        var stats = _repo.PlayerStats(1, "de_mirage");

        Assert.Equal(2, stats.Count);
        Assert.Equal(("a_route", 20000, 1), stats[0]);
        Assert.Equal(("silo_to_mini", 12000, 2), stats[1]);
        Assert.Empty(_repo.PlayerStats(1, "de_dust2"));
    }

    [Fact]
    public void ReleaseAllAllowsDeletingTheDatabaseFile()
    {
        // Prevents plugin unload from leaving scores.db locked so a redeploy cannot replace it.
        _repo.Insert(Row());
        ScoreRepository.ReleaseAll();
        File.Delete(_path);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void InsertRoundTripsTheCompFlag()
    {
        // Prevents a comp run from coming back as a casual run (or the reverse) from the leaderboard.
        _repo.Insert(Row(ms: 11000, name: "Casual"));
        _repo.Insert(Row(ms: 12000, name: "Comp", comp: true));

        var top = _repo.Top("de_mirage", "silo_to_mini", 10);

        Assert.False(top.Single(r => r.PlayerName == "Casual").Comp);
        Assert.True(top.Single(r => r.PlayerName == "Comp").Comp);
    }

    [Fact]
    public void TopCompOnlyFiltersByTheFlag()
    {
        // Prevents "!freefrag top comp" from listing casual runs, and the default listing from hiding comp runs.
        _repo.Insert(Row(ms: 9000, name: "FastCasual"));
        _repo.Insert(Row(ms: 12000, name: "CompA", comp: true));
        _repo.Insert(Row(ms: 13000, name: "CompB", comp: true));

        Assert.Equal(new[] { "FastCasual", "CompA", "CompB" }, _repo.Top("de_mirage", "silo_to_mini", 10).Select(r => r.PlayerName).ToArray());
        Assert.Equal(new[] { "FastCasual", "CompA", "CompB" }, _repo.Top("de_mirage", "silo_to_mini", 10, null).Select(r => r.PlayerName).ToArray());
        Assert.Equal(new[] { "CompA", "CompB" }, _repo.Top("de_mirage", "silo_to_mini", 10, true).Select(r => r.PlayerName).ToArray());
        Assert.Equal(new[] { "FastCasual" }, _repo.Top("de_mirage", "silo_to_mini", 10, false).Select(r => r.PlayerName).ToArray());
    }

    [Fact]
    public void EnsureCreatedMigratesADatabaseFromBeforeTheCompColumn()
    {
        // Prevents the plugin from failing to load (or losing old times) on a server whose scores.db predates comp mode.
        var legacyPath = Path.GetTempFileName();
        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id INTEGER NOT NULL,
    player_name TEXT NOT NULL,
    map TEXT NOT NULL,
    route TEXT NOT NULL,
    mode INTEGER NOT NULL,
    difficulty INTEGER NOT NULL,
    bot_weapon INTEGER NOT NULL,
    headshot_only INTEGER NOT NULL,
    elapsed_ms INTEGER NOT NULL,
    bots INTEGER NOT NULL,
    deaths INTEGER NOT NULL,
    kills INTEGER NOT NULL,
    hs_kills INTEGER NOT NULL,
    finished_at TEXT NOT NULL
);
INSERT INTO runs (steam_id, player_name, map, route, mode, difficulty, bot_weapon, headshot_only, elapsed_ms, bots, deaths, kills, hs_kills, finished_at)
VALUES (1, 'Old', 'de_mirage', 'silo_to_mini', 1, 3, 2, 0, 15000, 5, 0, 5, 3, '2025-01-01T00:00:00.0000000Z');";
                cmd.ExecuteNonQuery();
            }
            ScoreRepository.ReleaseAll();

            var repo = new ScoreRepository(legacyPath);
            repo.EnsureCreated();
            repo.EnsureCreated();

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(runs);";
                var columns = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(1));
                Assert.Contains(columns, c => string.Equals(c, "Comp", StringComparison.OrdinalIgnoreCase));
            }

            repo.Insert(Row(ms: 11000, name: "New", comp: true));
            var all = repo.Top("de_mirage", "silo_to_mini", 10);
            Assert.Equal(new[] { "New", "Old" }, all.Select(r => r.PlayerName).ToArray());
            Assert.False(all.Single(r => r.PlayerName == "Old").Comp);
            Assert.Equal(new[] { "New" }, repo.Top("de_mirage", "silo_to_mini", 10, true).Select(r => r.PlayerName).ToArray());
        }
        finally
        {
            ScoreRepository.ReleaseAll();
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }
}
