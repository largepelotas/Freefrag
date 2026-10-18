using Freefrag;
using Xunit;

public class ScreenMenuHtmlTests
{
    private static readonly string[] Ten =
        { "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9" };

    private static List<string> Lines(string html) => html.Split("<br>").ToList();

    [Fact]
    public void OnlyTheSelectedRowHasMarkers()
    {
        // Prevents every row (or none) from looking selected.
        var cursor = new MenuCursor(3, 7);
        cursor.Down();

        var html = ScreenMenuHtml.Render("T", new[] { "one", "two", "three" }, cursor, "f");

        Assert.Contains("&gt; two &lt;", html);
        Assert.DoesNotContain("&gt; one &lt;", html);
        Assert.DoesNotContain("&gt; three &lt;", html);
        Assert.Contains("one</font>", html);
        Assert.Contains("three</font>", html);
    }

    [Fact]
    public void EllipsisMarkersAppearOnlyWhenEntriesAreHidden()
    {
        // Prevents "..." on a menu that fits, or a missing one that hides entries without a hint.
        var fits = ScreenMenuHtml.Render("T", new[] { "x", "y" }, new MenuCursor(2, 7), "f");
        Assert.DoesNotContain("...", fits);

        var top = new MenuCursor(10, 6);
        var topLines = Lines(ScreenMenuHtml.Render("T", Ten, top, "f"));
        Assert.Equal(1, topLines.Count(l => l == "..."));
        Assert.Equal("...", topLines[^3]);   // Just below the visible rows, above the blank line and footer.

        var bottom = new MenuCursor(10, 6);
        bottom.Up();
        var bottomLines = Lines(ScreenMenuHtml.Render("T", Ten, bottom, "f"));
        Assert.Equal(1, bottomLines.Count(l => l == "..."));
        Assert.Equal("...", bottomLines[1]);   // Just under the title.

        var middle = new MenuCursor(10, 6);
        for (var i = 0; i < 5; i++) middle.Down();
        Assert.Equal(2, Lines(ScreenMenuHtml.Render("T", Ten, middle, "f")).Count(l => l == "..."));
    }

    [Fact]
    public void ScrollingMenuUsesPlaceholderWhereNothingIsHidden()
    {
        // Prevents the item rows shifting by a line when a marker appears or disappears.
        var top = Lines(ScreenMenuHtml.Render("T", Ten, new MenuCursor(10, 6), "f"));
        Assert.Equal("&nbsp;", top[1]);
        Assert.Equal("...", top[^3]);

        var bottomCursor = new MenuCursor(10, 6);
        bottomCursor.Up();
        var bottom = Lines(ScreenMenuHtml.Render("T", Ten, bottomCursor, "f"));
        Assert.Equal("...", bottom[1]);
        Assert.Equal("&nbsp;", bottom[^3]);
    }

    [Fact]
    public void NonScrollingMenuEmitsNoMarkerLines()
    {
        // Prevents extra blank lines on menus that fit within Rows.
        var lines = Lines(ScreenMenuHtml.Render("T", new[] { "x", "y" }, new MenuCursor(2, 7), "f"));

        Assert.DoesNotContain("&nbsp;", lines);
        Assert.Equal(1 + 2 + 2, lines.Count);   // title, two rows, blank, footer
    }

    [Fact]
    public void LineCountIsConstantAcrossTheWholeScrollRangeAndNeverExceedsRows()
    {
        // Prevents the panel growing taller as the window scrolls (the bottom row gets clipped).
        var labels = Enumerable.Range(0, 12).Select(i => $"item{i}").ToList();
        var cursor = new MenuCursor(12, 7);
        var counts = new HashSet<int>();

        for (var step = 0; step < 30; step++)
        {
            var lines = Lines(ScreenMenuHtml.Render("T", labels, cursor, "f"));
            counts.Add(lines.Count);
            Assert.Contains(lines, l => l.Contains($"&gt; item{cursor.Index} &lt;"));
            Assert.Equal(1, lines.Count(l => l.Contains("&gt; ")));
            cursor.Down();
        }
        for (var step = 0; step < 30; step++)
        {
            var lines = Lines(ScreenMenuHtml.Render("T", labels, cursor, "f"));
            counts.Add(lines.Count);
            Assert.Contains(lines, l => l.Contains($"&gt; item{cursor.Index} &lt;"));
            cursor.Up();
        }

        Assert.Single(counts);
        Assert.Equal(1 + 7 + 2, counts.Single());   // title + Rows (items + two marker lines) + blank + footer
    }

    [Fact]
    public void FirstVisibleItemIsAlwaysOnTheSameLine()
    {
        // Prevents rows shifting under the cursor when a marker toggles: the first item is always line 2.
        var labels = Enumerable.Range(0, 12).Select(i => $"item{i}").ToList();
        var cursor = new MenuCursor(12, 7);

        for (var step = 0; step < 14; step++)
        {
            var lines = Lines(ScreenMenuHtml.Render("T", labels, cursor, "f"));
            Assert.Contains($"item{cursor.WindowStart}", lines[2]);
            cursor.Down();
        }
    }

    [Fact]
    public void LabelsAndTitleAreHtmlEscaped()
    {
        // Prevents a route or map name containing < or & from injecting markup into the centre HTML.
        var html = ScreenMenuHtml.Render("A & <B>", new[] { "x <b>y</b> & z" }, new MenuCursor(1, 7), "f");

        Assert.Contains("A &amp; &lt;B&gt;", html);
        Assert.Contains("x &lt;b&gt;y&lt;/b&gt; &amp; z", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void EmptyTitleEmitsNoTitleLine()
    {
        // Prevents an empty underlined line at the top of title-less menus.
        var html = ScreenMenuHtml.Render("", new[] { "x" }, new MenuCursor(1, 7), "f");

        Assert.DoesNotContain("<u>", html);
        Assert.DoesNotContain("#7CFC00", html);
    }

    [Fact]
    public void ShowsExactlyRowsLabelsWhenThereAreMore()
    {
        // Prevents a long menu from dumping every entry into the centre HTML (it would overflow the screen).
        var cursor = new MenuCursor(10, 6);

        var html = ScreenMenuHtml.Render("T", Ten, cursor, "f");

        for (var i = 0; i < 4; i++) Assert.Contains($"a{i}", html);
        for (var i = 4; i < 10; i++) Assert.DoesNotContain($"a{i}", html);
    }

    [Fact]
    public void ScrolledWindowShowsTheRowsAroundTheSelection()
    {
        // Prevents the rendered rows disagreeing with the cursor's window after scrolling.
        var cursor = new MenuCursor(10, 6);
        for (var i = 0; i < 6; i++) cursor.Down();    // index 6, window 3..6

        var html = ScreenMenuHtml.Render("T", Ten, cursor, "f");

        Assert.DoesNotContain("a2<", html);
        Assert.Contains("a3", html);
        Assert.Contains("&gt; a6 &lt;", html);
        Assert.DoesNotContain("a7", html);
    }

    [Fact]
    public void ChatLineSeparatorInTitleBecomesLineBreaks()
    {
        // Prevents titles written for chat (modemenu.title has two lines) from showing a stray separator glyph on screen.
        var title = "first" + ChatChunks.LineBreak + "second";

        var html = ScreenMenuHtml.Render(title, new[] { "x" }, new MenuCursor(1, 7), "f");

        Assert.DoesNotContain(ChatChunks.LineBreak.ToString(), html);
        Assert.Contains("first</u></font><br><font color='#7CFC00'><u>second", html);
    }

    [Fact]
    public void DoneRowsAreGrayAndKeepTheSelectionMarkers()
    {
        // A route finished this session is dimmed so the player sees what is left, but it stays
        // selectable, so the cursor markers must still show on it.
        var cursor = new MenuCursor(3, 7);
        cursor.Down();
        var html = ScreenMenuHtml.Render("T", new[] { "one", "two", "three" }, cursor, "f", new[] { true, true, false });

        Assert.Contains("<font color='#808080'>one</font>", html);
        Assert.Contains("<font color='#FFD700'>&gt; </font><font color='#808080'>two</font><font color='#FFD700'> &lt;</font>", html);
        Assert.Contains("<font color='#FFFFFF'>three</font>", html);
    }

    [Fact]
    public void FooterIsLastAfterABlankLine()
    {
        // Prevents the key legend from running into the last menu row.
        var lines = Lines(ScreenMenuHtml.Render("T", new[] { "x" }, new MenuCursor(1, 7), "keys here"));

        Assert.Contains("keys here", lines[^1]);
        Assert.Equal("", lines[^2]);
    }
}
