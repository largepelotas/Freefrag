using OpenPrefirePrac;
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
        string name = "Quinn", int hsKills = 4, DateTime? at = null) =>
        new(0, steam, name, "de_mirage", route, mode, diff, 2, false, ms, 5, 0, 5, hsKills,
            at ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

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
}
