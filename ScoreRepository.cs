using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Freefrag;

public sealed class ScoreRepository
{
    private readonly string _connectionString;

    public ScoreRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public void EnsureCreated()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // WAL + synchronous=NORMAL: inserts happen on the game thread, so avoid a full fsync per finished route.
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS runs (
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
    finished_at TEXT NOT NULL,
    Comp INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_runs_route ON runs(map, route, elapsed_ms);
CREATE INDEX IF NOT EXISTS idx_runs_player ON runs(steam_id, map);";
        cmd.ExecuteNonQuery();

        // A database created before comp mode has no Comp column; CREATE TABLE IF NOT EXISTS leaves it untouched.
        var hasComp = false;
        cmd.CommandText = "PRAGMA table_info(runs);";
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "Comp", StringComparison.OrdinalIgnoreCase))
                    hasComp = true;
            }
        }
        if (!hasComp)
        {
            cmd.CommandText = "ALTER TABLE runs ADD COLUMN Comp INTEGER NOT NULL DEFAULT 0;";
            cmd.ExecuteNonQuery();
        }
    }

    public long Insert(ScoreRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO runs (steam_id, player_name, map, route, mode, difficulty, bot_weapon, headshot_only,
                  elapsed_ms, bots, deaths, kills, hs_kills, finished_at, Comp)
VALUES ($steam, $name, $map, $route, $mode, $diff, $bw, $hs, $elapsed, $bots, $deaths, $kills, $hsk, $fin, $comp);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$steam", (long)row.SteamId);
        cmd.Parameters.AddWithValue("$name", row.PlayerName);
        cmd.Parameters.AddWithValue("$map", row.Map);
        cmd.Parameters.AddWithValue("$route", row.Route);
        cmd.Parameters.AddWithValue("$mode", row.TrainingMode);
        cmd.Parameters.AddWithValue("$diff", row.Difficulty);
        cmd.Parameters.AddWithValue("$bw", row.BotWeapon);
        cmd.Parameters.AddWithValue("$hs", row.HeadshotOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$elapsed", row.ElapsedMs);
        cmd.Parameters.AddWithValue("$bots", row.Bots);
        cmd.Parameters.AddWithValue("$deaths", row.Deaths);
        cmd.Parameters.AddWithValue("$kills", row.Kills);
        cmd.Parameters.AddWithValue("$hsk", row.HeadshotKills);
        cmd.Parameters.AddWithValue("$fin", row.FinishedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$comp", row.Comp ? 1 : 0);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int? BestFor(ulong steamId, string map, string route, int trainingMode, int difficulty)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT MIN(elapsed_ms) FROM runs
WHERE steam_id = $steam AND map = $map AND route = $route AND mode = $mode AND difficulty = $diff;";
        cmd.Parameters.AddWithValue("$steam", (long)steamId);
        cmd.Parameters.AddWithValue("$map", map);
        cmd.Parameters.AddWithValue("$route", route);
        cmd.Parameters.AddWithValue("$mode", trainingMode);
        cmd.Parameters.AddWithValue("$diff", difficulty);
        var result = cmd.ExecuteScalar();
        return result == null || result is DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <param name="compOnly">null = every run; true = comp runs only; false = casual runs only.</param>
    public IReadOnlyList<ScoreRow> Top(string map, string route, int limit, bool? compOnly = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, steam_id, player_name, map, route, mode, difficulty, bot_weapon, headshot_only,
       elapsed_ms, bots, deaths, kills, hs_kills, finished_at, Comp
FROM runs WHERE map = $map AND route = $route AND ($comp IS NULL OR Comp = $comp)
ORDER BY elapsed_ms ASC, finished_at ASC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$map", map);
        cmd.Parameters.AddWithValue("$route", route);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$comp", compOnly.HasValue ? (object)(compOnly.Value ? 1 : 0) : DBNull.Value);
        var rows = new List<ScoreRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ScoreRow(
                reader.GetInt64(0), (ulong)reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8) != 0,
                reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13),
                DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt32(15) != 0));
        }
        return rows;
    }

    public IReadOnlyList<(string Route, int BestMs, int Runs)> PlayerStats(ulong steamId, string map)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT route, MIN(elapsed_ms), COUNT(*) FROM runs
WHERE steam_id = $steam AND map = $map
GROUP BY route ORDER BY route;";
        cmd.Parameters.AddWithValue("$steam", (long)steamId);
        cmd.Parameters.AddWithValue("$map", map);
        var rows = new List<(string Route, int BestMs, int Runs)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }
        return rows;
    }

    public static void ReleaseAll() => SqliteConnection.ClearAllPools();
}
