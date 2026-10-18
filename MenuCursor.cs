namespace Freefrag;

/// <summary>
/// Selection and scroll window of one on-screen menu level. The window always contains Index and
/// moves one row at a time; wrapping jumps so the new Index is visible.
/// </summary>
public sealed class MenuCursor
{
    public MenuCursor(int count, int rows)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        Count = count;
        Rows = rows;
    }

    public int Count { get; }
    public int Rows { get; }
    public int Index { get; private set; }
    public int WindowStart { get; private set; }

    public bool HasMoreAbove => WindowStart > 0;
    public bool HasMoreBelow => WindowStart + Rows < Count;

    public void Up()
    {
        if (Index == 0)
        {
            Index = Count - 1;
            WindowStart = Math.Max(0, Count - Rows);
            return;
        }

        Index--;
        if (Index < WindowStart)
            WindowStart = Index;
    }

    public void Down()
    {
        if (Index == Count - 1)
        {
            Index = 0;
            WindowStart = 0;
            return;
        }

        Index++;
        if (Index >= WindowStart + Rows)
            WindowStart = Index - Rows + 1;
    }
}
