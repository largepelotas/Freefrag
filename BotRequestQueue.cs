namespace Freefrag;

/// <summary>
/// Serializes bot requests from several practicing players. Only the request at the head of the
/// queue has its bots spawned; every unmanaged bot that joins while a request is active is matched
/// against that request's team. The plugin issues the actual bot_add commands whenever a method
/// here returns a non-null <see cref="Issue"/>.
/// </summary>
public class BotRequestQueue
{
    public const int TeamT = 2;
    public const int TeamCT = 3;

    /// <summary>Spawn commands the plugin must issue: <c>Count</c> bots on <c>BotTeam</c> for <c>OwnerSlot</c>.</summary>
    public sealed record Issue(int OwnerSlot, int BotTeam, int Count, int IssueId);

    private sealed class Request
    {
        public int OwnerSlot;
        public int BotTeam;
        public int Pending;
        public bool Issued;
        public int IssueId;
    }

    private readonly List<Request> _queue = new();
    private int _nextIssueId;

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.Count == 0;
    public int? ActiveOwner => _queue.Count > 0 ? _queue[0].OwnerSlot : null;
    /// <summary>Bots still wanted across all queued requests (issued or not).</summary>
    public int TotalPending => _queue.Sum(r => r.Pending);
    public int PendingFor(int ownerSlot) => _queue.Find(r => r.OwnerSlot == ownerSlot)?.Pending ?? 0;

    public void Clear() => _queue.Clear();

    /// <summary>
    /// Adds or replaces the owner's request for <paramref name="count"/> bots on <paramref name="botTeam"/>.
    /// Returns the bots to spawn right now, or null when nothing has to be spawned yet (the request is
    /// queued behind another player's, or its bots are already on their way).
    /// </summary>
    public Issue? Enqueue(int ownerSlot, int botTeam, int count)
    {
        if (count <= 0)
            return null;

        var existing = _queue.Find(r => r.OwnerSlot == ownerSlot);
        if (existing != null)
        {
            var alreadyOnTheWay = existing.Issued ? existing.Pending : 0;
            existing.BotTeam = botTeam;
            existing.Pending = count;
            if (!existing.Issued || count <= alreadyOnTheWay)
                return null;
            return MarkIssued(existing, count - alreadyOnTheWay);
        }

        var request = new Request { OwnerSlot = ownerSlot, BotTeam = botTeam, Pending = count };
        _queue.Add(request);
        return _queue.Count == 1 ? MarkIssued(request, count) : null;
    }

    /// <summary>
    /// Assigns a newly joined bot to the active request. <paramref name="botTeam"/> is the bot's team
    /// if known (T or CT); anything else counts as unknown and is accepted. Returns the owner slot, or
    /// null when the bot is a stray that should be kicked. <paramref name="next"/> is set when the
    /// claim fulfilled the active request and the following one must be issued now.
    /// </summary>
    public int? Claim(int botTeam, out Issue? next)
    {
        next = null;
        if (_queue.Count == 0)
            return null;

        var active = _queue[0];
        if ((botTeam == TeamT || botTeam == TeamCT) && botTeam != active.BotTeam)
            return null;

        active.Pending--;
        if (active.Pending <= 0)
            next = Advance();
        return active.OwnerSlot;
    }

    /// <summary>Drops the owner's request (player left or stopped practicing). Returns the next request to issue, if any.</summary>
    public Issue? Remove(int ownerSlot)
    {
        var index = _queue.FindIndex(r => r.OwnerSlot == ownerSlot);
        if (index < 0)
            return null;
        if (index > 0)
        {
            _queue.RemoveAt(index);
            return null;
        }
        return Advance();
    }

    /// <summary>
    /// Called when the bots of issue <paramref name="issueId"/> should long have arrived. If that issue
    /// is still the active, unfulfilled one, it is dropped so it cannot block everybody else; the owner
    /// gets a fresh request on their next spawn. Returns the next request to issue, if any.
    /// </summary>
    public Issue? ExpireIfStalled(int ownerSlot, int issueId, out int droppedPending)
    {
        droppedPending = 0;
        if (_queue.Count == 0)
            return null;
        var active = _queue[0];
        if (active.OwnerSlot != ownerSlot || active.IssueId != issueId || active.Pending <= 0)
            return null;
        droppedPending = active.Pending;
        return Advance();
    }

    private Issue? Advance()
    {
        _queue.RemoveAt(0);
        if (_queue.Count == 0)
            return null;
        var head = _queue[0];
        return MarkIssued(head, head.Pending);
    }

    private Issue MarkIssued(Request request, int count)
    {
        request.Issued = true;
        request.IssueId = ++_nextIssueId;
        return new Issue(request.OwnerSlot, request.BotTeam, count, request.IssueId);
    }
}
