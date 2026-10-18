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

        var top = new MenuCursor(10, 4);
        var topLines = Lines(ScreenMenuHtml.Render("T", Ten, top, "f"));
        Assert.Equal(1, topLines.Count(l => l == "..."));
        Assert.Equal("...", topLines[^3]);   // Just below the visible rows, above the blank line and footer.

        var bottom = new MenuCursor(10, 4);
        bottom.Up();
        var bottomLines = Lines(ScreenMenuHtml.Render("T", Ten, bottom, "f"));
        Assert.Equal(1, bottomLines.Count(l => l == "..."));
        Assert.Equal("...", bottomLines[1]);   // Just under the title.

        var middle = new MenuCursor(10, 4);
        for (var i = 0; i < 5; i++) middle.Down();
        Assert.Equal(2, Lines(ScreenMenuHtml.Render("T", Ten, middle, "f")).Count(l => l == "..."));
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
        var cursor = new MenuCursor(10, 4);

        var html = ScreenMenuHtml.Render("T", Ten, cursor, "f");

        for (var i = 0; i < 4; i++) Assert.Contains($"a{i}", html);
        for (var i = 4; i < 10; i++) Assert.DoesNotContain($"a{i}", html);
    }

    [Fact]
    public void ScrolledWindowShowsTheRowsAroundTheSelection()
    {
        // Prevents the rendered rows disagreeing with the cursor's window after scrolling.
        var cursor = new MenuCursor(10, 4);
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
    public void FooterIsLastAfterABlankLine()
    {
        // Prevents the key legend from running into the last menu row.
        var lines = Lines(ScreenMenuHtml.Render("T", new[] { "x" }, new MenuCursor(1, 7), "keys here"));

        Assert.Contains("keys here", lines[^1]);
        Assert.Equal("", lines[^2]);
    }
}
