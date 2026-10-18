using System.Text;

namespace Freefrag;

/// <summary>Builds the centre-screen HTML for one menu level (pure; no engine types).</summary>
public static class ScreenMenuHtml
{
    private const string Ellipsis = "...";
    private const string Placeholder = "&nbsp;";

    public static string Render(string title, IReadOnlyList<string> labels, MenuCursor cursor, string footer)
    {
        var lines = new List<string>();

        if (!string.IsNullOrEmpty(title))
        {
            // Some titles carry the plugin's chat line separator; the centre HTML needs <br> instead.
            foreach (var titleLine in title.Split(ChatChunks.LineBreak))
                lines.Add($"<font color='#7CFC00'><u>{Escape(titleLine)}</u></font>");
        }

        // A menu longer than Rows always reserves a line above and below the items (blank when nothing
        // is hidden on that side), so the panel height and the item rows never change while scrolling.
        var scrolls = cursor.Count > cursor.Rows;

        if (scrolls)
            lines.Add(cursor.HasMoreAbove ? Ellipsis : Placeholder);

        var end = Math.Min(Math.Min(cursor.WindowStart + cursor.VisibleRows, cursor.Count), labels.Count);
        for (var i = cursor.WindowStart; i < end; i++)
        {
            var label = Escape(labels[i]);
            lines.Add(i == cursor.Index
                ? $"<font color='#FFD700'>&gt; {label} &lt;</font>"
                : $"<font color='#FFFFFF'>{label}</font>");
        }

        if (scrolls)
            lines.Add(cursor.HasMoreBelow ? Ellipsis : Placeholder);

        lines.Add("");
        lines.Add($"<font class='fontSize-s' color='#5FD7FF'>{Escape(footer)}</font>");

        return string.Join("<br>", lines);
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
