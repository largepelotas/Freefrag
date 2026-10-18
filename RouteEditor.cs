using System.Globalization;
using System.Text.RegularExpressions;

namespace Freefrag;

/// <summary>Pure helpers behind !freefrag addbot / newroute: profile text in, profile text out.</summary>
public static class RouteEditor
{
    /// <summary>A bot placed in game. A swinger hides here and runs to <see cref="Peek"/> when released; it has no peek until the player sets one.</summary>
    public readonly record struct DraftBot(float X, float Y, float Z, float Pitch, float Yaw, bool Crouch, bool Swing = false, SwingSpec? Peek = null);

    private static readonly char[] Delimiters = { ' ', ',', '\t' };
    private static readonly Regex NamePattern = new("\\A[A-Za-z0-9_-]{1,40}\\z", RegexOptions.Compiled);

    private static string F(float v) => v.ToString("F6", CultureInfo.InvariantCulture);

    public static string FormatBotLine(DraftBot b)
    {
        var line = $"{F(b.X)} {F(b.Y)} {F(b.Z)} {F(b.Pitch)} {F(b.Yaw)} {F(0f)} {(b.Crouch ? "True" : "False")}";
        return b.Peek != null ? line + " " + SwingSpec.Format(b.Peek.Value) : line;
    }

    /// <summary>The bot with its peek point set to the given feet position and no trigger of its own.</summary>
    public static DraftBot WithPeek(DraftBot bot, float x, float y, float z) =>
        bot with { Peek = new SwingSpec(x, y, z, SwingSpec.NoTrigger) };

    /// <summary>How many draft swingers still lack a peek point; saving is refused while this is above zero.</summary>
    public static int MissingPeeks(IReadOnlyList<DraftBot> bots) => bots.Count(b => b.Swing && b.Peek == null);

    public static int ClampNumBots(int numBots, int totalTargets) => Math.Min(numBots, totalTargets);

    public static bool IsValidRouteName(string? name) => name != null && NamePattern.IsMatch(name);

    public static List<string> NewProfile(int numBots, float x, float y, float z, float pitch, float yaw) => new()
    {
        "",
        $"{numBots.ToString(CultureInfo.InvariantCulture)} {F(1f)}",
        $"{F(x)} {F(y)} {F(z)} {F(pitch)} {F(yaw)} {F(0f)}",
    };

    private static bool IsTargetLine(string line)
    {
        var w = line.Split(Delimiters);
        return w.Length >= 7 && bool.TryParse(w[6], out _);
    }

    /// <summary>
    /// Inserts the bots after the last existing target line (after line 3 when the route has none), which is
    /// always before the guiding-line joints, and clamps line 2's bot count to the new target total.
    /// Every other line is preserved; blank lines at the end are dropped only when nothing follows the targets.
    /// </summary>
    public static List<string> InsertBots(IEnumerable<string> profileLines, IReadOnlyList<DraftBot> bots)
    {
        var lines = profileLines.ToList();

        var lastTarget = -1;
        var targetCount = 0;
        for (var i = 3; i < lines.Count; i++)
        {
            if (IsTargetLine(lines[i])) { lastTarget = i; targetCount++; }
        }

        var insertAt = lastTarget >= 0 ? lastTarget + 1 : Math.Min(3, lines.Count);

        // With no guiding line after the targets, trailing blanks would sit between old and new bots.
        if (!lines.Skip(insertAt).Any(l => !string.IsNullOrWhiteSpace(l)))
            lines.RemoveRange(insertAt, lines.Count - insertAt);

        lines.InsertRange(insertAt, bots.Select(FormatBotLine));

        if (lines.Count > 1)
        {
            var w = lines[1].Split(Delimiters);
            if (int.TryParse(w[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                var clamped = ClampNumBots(n, targetCount + bots.Count);
                if (clamped != n)
                {
                    w[0] = clamped.ToString(CultureInfo.InvariantCulture);
                    lines[1] = string.Join(' ', w);
                }
            }
        }
        return lines;
    }
}
