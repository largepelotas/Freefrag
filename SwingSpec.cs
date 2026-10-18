using System.Globalization;

namespace Freefrag;

/// <summary>
/// The optional "peek x y z [trigger d]" part of a route's target line: a swinging bot hides at the
/// line's own position and runs to (X, Y, Z) once the player comes within Trigger units.
/// </summary>
public readonly record struct SwingSpec(float X, float Y, float Z, float Trigger)
{
    /// <summary>Trigger value meaning "the line sets none, use the configured default".</summary>
    public const float NoTrigger = 0f;
    public const float MinTrigger = 100f;
    public const float MaxTrigger = 3000f;

    private static string F(float v) => v.ToString("F6", CultureInfo.InvariantCulture);

    private static bool TryNumber(string[] tokens, int index, out float value)
    {
        value = 0f;
        return index < tokens.Length
               && float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && float.IsFinite(value);
    }

    /// <summary>
    /// Looks for "peek x y z" (and an optional "trigger d") in <paramref name="tokens"/> from index <paramref name="start"/>,
    /// the token after the crouch flag. A token starting with '#' ends the line. Returns false with a null
    /// <paramref name="error"/> when there is no peek token (a static bot), and false with a message when
    /// peek is present but malformed.
    /// </summary>
    public static bool TryParse(string[] tokens, int start, out SwingSpec spec, out string? error)
    {
        spec = default;
        error = null;

        var peekAt = -1;
        for (var i = start; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t.Length == 0) continue;
            if (t[0] == '#') break;
            if (t.Equals("peek", StringComparison.OrdinalIgnoreCase)) { peekAt = i; break; }
        }
        if (peekAt < 0) return false;

        if (!TryNumber(tokens, peekAt + 1, out var x) || !TryNumber(tokens, peekAt + 2, out var y) || !TryNumber(tokens, peekAt + 3, out var z))
        {
            error = "malformed peek spec, expected: peek x y z [trigger d]";
            return false;
        }

        var trigger = NoTrigger;
        for (var i = peekAt + 4; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t.Length == 0) continue;
            if (t[0] == '#') break;
            if (t.Equals("trigger", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryNumber(tokens, i + 1, out var d))
                {
                    error = "malformed peek spec, trigger needs a number: peek x y z trigger d";
                    return false;
                }
                trigger = Math.Clamp(d, MinTrigger, MaxTrigger);
                break;
            }
        }

        spec = new SwingSpec(x, y, z, trigger);
        return true;
    }

    /// <summary>The spec as route-file text: "peek x y z", plus " trigger d" when the line sets one.</summary>
    public static string Format(SwingSpec spec)
    {
        var text = $"peek {F(spec.X)} {F(spec.Y)} {F(spec.Z)}";
        if (spec.Trigger != NoTrigger)
            text += " trigger " + spec.Trigger.ToString("F0", CultureInfo.InvariantCulture);
        return text;
    }
}
