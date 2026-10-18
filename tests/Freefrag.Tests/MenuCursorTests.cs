using Freefrag;
using Xunit;

public class MenuCursorTests
{
    [Fact]
    public void UpAtTopWrapsToBottomWithWindowShowingIt()
    {
        // Prevents W on the first entry from doing nothing, or selecting the last entry while the
        // window still shows the top (an invisible selection).
        var cursor = new MenuCursor(10, 4);

        cursor.Up();

        Assert.Equal(9, cursor.Index);
        Assert.Equal(6, cursor.WindowStart);
    }

    [Fact]
    public void DownAtBottomWrapsToTopWithWindowZero()
    {
        // Prevents S on the last entry from sticking, or wrapping to 0 while the window stays scrolled to the bottom.
        var cursor = new MenuCursor(10, 4);
        cursor.Up();

        cursor.Down();

        Assert.Equal(0, cursor.Index);
        Assert.Equal(0, cursor.WindowStart);
    }

    [Fact]
    public void MovingDownPastTheWindowScrollsByOneRow()
    {
        // Prevents paging (window jumping a whole screen) or an unmoved window that hides the selection.
        var cursor = new MenuCursor(10, 4);

        for (var i = 0; i < 3; i++) cursor.Down();
        Assert.Equal(3, cursor.Index);
        Assert.Equal(0, cursor.WindowStart);

        cursor.Down();
        Assert.Equal(4, cursor.Index);
        Assert.Equal(1, cursor.WindowStart);
    }

    [Fact]
    public void MovingUpAboveTheWindowScrollsByOneRow()
    {
        // Prevents the selection leaving the window when scrolling back up.
        var cursor = new MenuCursor(10, 4);
        for (var i = 0; i < 6; i++) cursor.Down();     // index 6, window 3..6
        Assert.Equal(3, cursor.WindowStart);

        for (var i = 0; i < 3; i++) cursor.Up();       // index 3, still in window
        Assert.Equal(3, cursor.WindowStart);

        cursor.Up();                                   // index 2, above the window
        Assert.Equal(2, cursor.Index);
        Assert.Equal(2, cursor.WindowStart);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(5, true, true)]
    [InlineData(9, true, false)]
    public void MoreAboveAndBelowFlagsFollowTheWindow(int index, bool above, bool below)
    {
        // Prevents the "..." markers lying about hidden entries (shown at the ends, or missing in the middle).
        var cursor = new MenuCursor(10, 4);
        for (var i = 0; i < index; i++) cursor.Down();

        Assert.Equal(index, cursor.Index);
        Assert.Equal(above, cursor.HasMoreAbove);
        Assert.Equal(below, cursor.HasMoreBelow);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 7)]
    [InlineData(1, 7)]
    public void RowsAtLeastCountNeverScrolls(int count, int rows)
    {
        // Prevents a short menu (e.g. 3 entries in a 7-row window) from showing "..." or scrolling.
        var cursor = new MenuCursor(count, rows);

        for (var i = 0; i < count * 2 + 1; i++)
        {
            Assert.Equal(0, cursor.WindowStart);
            Assert.False(cursor.HasMoreAbove);
            Assert.False(cursor.HasMoreBelow);
            cursor.Down();
        }

        for (var i = 0; i < count * 2 + 1; i++)
        {
            cursor.Up();
            Assert.Equal(0, cursor.WindowStart);
        }
    }

    [Fact]
    public void SelectionIsAlwaysInsideTheWindow()
    {
        // Prevents any wrap/scroll combination from leaving the highlighted entry off screen.
        var cursor = new MenuCursor(10, 4);

        for (var i = 0; i < 40; i++)
        {
            cursor.Down();
            Assert.InRange(cursor.Index, cursor.WindowStart, cursor.WindowStart + cursor.Rows - 1);
        }

        for (var i = 0; i < 40; i++)
        {
            cursor.Up();
            Assert.InRange(cursor.Index, cursor.WindowStart, cursor.WindowStart + cursor.Rows - 1);
        }
    }
}
