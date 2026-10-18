using System.Globalization;

namespace Freefrag;

public static class ScoreFormat
{
    public static string Seconds(int ms) =>
        (ms / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "s";

    public static string SettingsTag(int trainingMode, int difficulty, int botWeapon, bool headshotOnly) =>
        $"{(trainingMode == 1 ? "full" : "rand")}/df{6 - difficulty}/{BotLoadout.DisplayName(botWeapon)}{(headshotOnly ? "/hs" : "")}";

    public static string FinishLine(RunResult r, int? previousBestMs)
    {
        var deaths = r.Deaths == 1 ? "1 death" : $"{r.Deaths} deaths";
        var head = $"Route done in {Seconds(r.ElapsedMs)}, {deaths}, {r.HeadshotPercent}% HS ({r.HeadshotKills}/{r.Kills}).";
        if (PersonalBest.IsBetter(r.ElapsedMs, previousBestMs))
        {
            return previousBestMs == null
                ? $"{head} New personal best."
                : $"{head} New personal best (was {Seconds(previousBestMs.Value)}).";
        }
        return $"{head} Personal best: {Seconds(previousBestMs!.Value)}.";
    }

    public static string TopLine(int rank, ScoreRow row)
    {
        var hsPercent = row.Kills == 0 ? 0 : (int)Math.Round(100.0 * row.HeadshotKills / row.Kills);
        return $"#{rank} {row.PlayerName} {Seconds(row.ElapsedMs)} {row.Deaths}d {hsPercent}%HS " +
               $"[{SettingsTag(row.TrainingMode, row.Difficulty, row.BotWeapon, row.HeadshotOnly)}]";
    }

    public static string StatsLine(string routeDisplayName, int bestMs, int runs) =>
        $"{routeDisplayName}: best {Seconds(bestMs)}, {runs} {(runs == 1 ? "run" : "runs")}";
}
