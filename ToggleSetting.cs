namespace Freefrag;

public static class ToggleSetting
{
    public static bool? Parse(string? token, bool current)
    {
        if (string.IsNullOrWhiteSpace(token))
            return !current;

        switch (token.Trim().ToLowerInvariant())
        {
            case "on":
            case "1":
            case "true":
                return true;
            case "off":
            case "0":
            case "false":
                return false;
            default:
                return null;
        }
    }

    public static string DisplayName(bool on) => on ? "on" : "off";
}
