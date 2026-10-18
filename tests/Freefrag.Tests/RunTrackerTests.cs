using Freefrag;
using Xunit;

public class RunTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ElapsedIsLastKillMinusFirstKill()
    {
        // Prevents run time from including the walk-in or the bot spawn delay before the first kill.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(false, T0.AddSeconds(5));
        run.RecordKill(true, T0.AddSeconds(8));
        run.RecordKill(false, T0.AddMilliseconds(17340));

        var result = run.Finish();

        Assert.NotNull(result);
        Assert.Equal(12340, result!.ElapsedMs);
        Assert.Equal(3, result.Kills);
    }

    [Fact]
    public void FinishWithoutKillsIsNull()
    {
        // Prevents an abandoned route from being saved as a zero-second record.
        var run = new RunTracker();
        run.Begin();
        run.RecordDeath();

        Assert.Null(run.Finish());
    }

    [Fact]
    public void DirtyRunDoesNotCount()
    {
        // Prevents switching to an easier difficulty mid-route from producing a leaderboard time.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(false, T0);
        run.RecordKill(false, T0.AddSeconds(3));
        run.MarkDirty();

        Assert.Null(run.Finish());
        Assert.True(run.Dirty);
    }

    [Fact]
    public void BeginResetsDirtyAndCounts()
    {
        // Prevents a restarted route (repeat, re-select) from inheriting kills, deaths or the dirty flag of the last run.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(true, T0);
        run.RecordDeath();
        run.MarkDirty();

        run.Begin();

        Assert.False(run.Dirty);
        Assert.Equal(0, run.Kills);
        Assert.Equal(0, run.HeadshotKills);
        Assert.Equal(0, run.Deaths);
        Assert.Null(run.FirstKillUtc);
        Assert.Null(run.LastKillUtc);
    }

    [Fact]
    public void DeathsAreCounted()
    {
        // Prevents the death count on the finish line from always reading zero.
        var run = new RunTracker();
        run.Begin();
        run.RecordDeath();
        run.RecordDeath();
        run.RecordKill(false, T0);

        Assert.Equal(2, run.Finish()!.Deaths);
    }

    [Fact]
    public void HeadshotPercentRounds()
    {
        // Prevents 2 of 3 headshots from showing as 66% (truncated) instead of 67%.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(true, T0);
        run.RecordKill(true, T0.AddSeconds(1));
        run.RecordKill(false, T0.AddSeconds(2));

        var result = run.Finish()!;

        Assert.Equal(2, result.HeadshotKills);
        Assert.Equal(67, result.HeadshotPercent);
    }

    [Fact]
    public void SingleKillRunHasZeroElapsed()
    {
        // Prevents a one-bot route from being rejected or crashing on a missing second timestamp.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(false, T0);

        Assert.Equal(0, run.Finish()!.ElapsedMs);
    }

    [Fact]
    public void ClockStepBackwardsVoidsTheRun()
    {
        // Prevents a wall-clock adjustment mid-run from producing a negative, unbeatable leaderboard time.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(false, T0);
        run.RecordKill(false, T0.AddSeconds(-5));

        Assert.Null(run.Finish());
    }

    [Fact]
    public void ElapsedIsMeasuredFromThisRunsKillsNotFromEarlierActivity()
    {
        // Prevents a delayed Begin (after the start countdown) or a restarted route from inheriting the previous run's first-kill time.
        var run = new RunTracker();
        run.Begin();
        run.RecordKill(false, T0);
        run.RecordKill(false, T0.AddSeconds(30));

        run.Begin();
        run.RecordKill(false, T0.AddSeconds(100));
        run.RecordKill(false, T0.AddSeconds(104));

        Assert.Equal(4000, run.Finish()!.ElapsedMs);
    }
}
