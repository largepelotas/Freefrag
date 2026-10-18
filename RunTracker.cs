namespace OpenPrefirePrac;

public sealed class RunTracker
{
    public DateTime? FirstKillUtc { get; private set; }
    public DateTime? LastKillUtc { get; private set; }
    public int Kills { get; private set; }
    public int HeadshotKills { get; private set; }
    public int Deaths { get; private set; }
    public bool Dirty { get; private set; }

    public void Begin()
    {
        FirstKillUtc = null;
        LastKillUtc = null;
        Kills = 0;
        HeadshotKills = 0;
        Deaths = 0;
        Dirty = false;
    }

    public void RecordKill(bool headshot, DateTime nowUtc)
    {
        FirstKillUtc ??= nowUtc;
        LastKillUtc = nowUtc;
        Kills++;
        if (headshot) HeadshotKills++;
    }

    public void RecordDeath() => Deaths++;

    public void MarkDirty() => Dirty = true;

    public RunResult? Finish()
    {
        if (Dirty || Kills == 0 || FirstKillUtc == null || LastKillUtc == null) return null;
        var elapsed = (int)Math.Round((LastKillUtc.Value - FirstKillUtc.Value).TotalMilliseconds);
        // A wall-clock step backwards during the run would otherwise produce an unbeatable negative time.
        if (elapsed < 0) return null;
        return new RunResult(elapsed, Kills, HeadshotKills, Deaths);
    }
}

public sealed record RunResult(int ElapsedMs, int Kills, int HeadshotKills, int Deaths)
{
    public int HeadshotPercent => Kills == 0 ? 0 : (int)Math.Round(100.0 * HeadshotKills / Kills);
}
