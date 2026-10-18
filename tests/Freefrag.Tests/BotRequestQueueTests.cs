using Freefrag;
using Xunit;

public class BotRequestQueueTests
{
    private const int T = BotRequestQueue.TeamT;
    private const int CT = BotRequestQueue.TeamCT;

    [Fact]
    public void SecondPlayersRequestWaitsUntilFirstIsFulfilled()
    {
        var q = new BotRequestQueue();

        var first = q.Enqueue(ownerSlot: 1, botTeam: T, count: 2);
        var second = q.Enqueue(ownerSlot: 2, botTeam: CT, count: 1);

        Assert.NotNull(first);
        Assert.Equal((1, T, 2), (first!.OwnerSlot, first.BotTeam, first.Count));
        Assert.Null(second);

        // Both bots go to player 1 even though player 2 asked in the meantime.
        Assert.Equal(1, q.Claim(T, out var next));
        Assert.Null(next);
        Assert.Equal(1, q.Claim(T, out next));

        // Player 1 is done, so player 2's bots are issued now.
        Assert.NotNull(next);
        Assert.Equal((2, CT, 1), (next!.OwnerSlot, next.BotTeam, next.Count));
        Assert.Equal(2, q.Claim(CT, out next));
        Assert.Null(next);
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void BotOnTheWrongTeamIsNotClaimed()
    {
        var q = new BotRequestQueue();
        q.Enqueue(1, T, 1);

        Assert.Null(q.Claim(CT, out var next));
        Assert.Null(next);
        Assert.Equal(1, q.PendingFor(1));

        // A bot whose team is not known yet is accepted.
        Assert.Equal(1, q.Claim(0, out _));
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void ClaimWithoutRequestsIsAStray()
    {
        var q = new BotRequestQueue();
        Assert.Null(q.Claim(T, out var next));
        Assert.Null(next);
    }

    [Fact]
    public void ReenqueueOnActiveRequestOnlyIssuesTheDifference()
    {
        var q = new BotRequestQueue();
        q.Enqueue(1, T, 2);

        var more = q.Enqueue(1, T, 3);
        Assert.NotNull(more);
        Assert.Equal(1, more!.Count);
        Assert.Equal(3, q.PendingFor(1));

        var fewer = q.Enqueue(1, T, 1);
        Assert.Null(fewer);
        Assert.Equal(1, q.PendingFor(1));
    }

    [Fact]
    public void ReenqueueOnQueuedRequestIssuesNothing()
    {
        var q = new BotRequestQueue();
        q.Enqueue(1, T, 1);
        q.Enqueue(2, CT, 1);

        Assert.Null(q.Enqueue(2, CT, 4));
        Assert.Equal(4, q.PendingFor(2));

        // When it becomes active, the whole replaced count is issued.
        q.Claim(T, out var next);
        Assert.Equal(4, next!.Count);
    }

    [Fact]
    public void RemovingTheActiveRequestIssuesTheNextOne()
    {
        var q = new BotRequestQueue();
        q.Enqueue(1, T, 2);
        q.Enqueue(2, CT, 1);
        q.Enqueue(3, T, 1);

        Assert.Null(q.Remove(3));
        Assert.Null(q.Remove(99));

        var next = q.Remove(1);
        Assert.NotNull(next);
        Assert.Equal(2, next!.OwnerSlot);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void StalledIssueIsDroppedOnlyWhileStillActiveAndUnchanged()
    {
        var q = new BotRequestQueue();
        var first = q.Enqueue(1, T, 2)!;
        q.Enqueue(2, CT, 1);

        // Fulfilled in time: the old issue id no longer matches anything active.
        q.Claim(T, out _);
        q.Claim(T, out var next);
        Assert.Equal(2, next!.OwnerSlot);
        Assert.Null(q.ExpireIfStalled(1, first.IssueId, out var dropped));
        Assert.Equal(0, dropped);
        Assert.Equal(1, q.Count);

        // Still waiting after the deadline: drop it and move on.
        Assert.Null(q.ExpireIfStalled(2, next.IssueId + 100, out _));
        Assert.Null(q.ExpireIfStalled(2, next.IssueId, out dropped));
        Assert.Equal(1, dropped);
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void ZeroCountIsIgnored()
    {
        var q = new BotRequestQueue();
        Assert.Null(q.Enqueue(1, T, 0));
        Assert.True(q.IsEmpty);
    }
}
