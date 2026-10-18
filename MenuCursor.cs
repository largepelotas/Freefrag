namespace Freefrag;

/// <summary>
/// Selection and scroll window of one on-screen menu level. The window always contains Index and
/// moves one row at a time; wrapping jumps so the new Index is visible. Rows is the total line
/// budget: a level longer than Rows spends two of them on the "more above/below" marker lines, so
/// only VisibleRows entries are shown and the rendered height never exceeds Rows.
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

    public int Count { get; private set; }
    public int Rows { get; }
    public int Index { get; private set; }
    public int WindowStart { get; private set; }

    /// <summary>Entries shown at once: Rows when everything fits, otherwise Rows minus the two marker lines.</summary>
    public int VisibleRows => Count > Rows ? Math.Max(1, Rows - 2) : Rows;

    public bool HasMoreAbove => WindowStart > 0;
    public bool HasMoreBelow => WindowStart + VisibleRows < Count;

    public void Up()
    {
        if (Index == 0)
        {
            Index = Count - 1;
            WindowStart = Math.Max(0, Count - VisibleRows);
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
        if (Index >= WindowStart + VisibleRows)
            WindowStart = Index - VisibleRows + 1;
    }

    /// <summary>
    /// The level's entry count changed (the menu was rebuilt in place): keep Index and the window where
    /// they were as far as the new count allows, and keep Index visible.
    /// </summary>
    public void ClampTo(int newCount)
    {
        if (newCount < 1) throw new ArgumentOutOfRangeException(nameof(newCount));
        Count = newCount;
        Index = Math.Min(Index, Count - 1);
        WindowStart = Math.Clamp(WindowStart, 0, Math.Max(0, Count - VisibleRows));
        if (Index < WindowStart)
            WindowStart = Index;
        else if (Index >= WindowStart + VisibleRows)
            WindowStart = Index - VisibleRows + 1;
    }
}
