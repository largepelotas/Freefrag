namespace Freefrag;

public static class AutoSkip
{
    public static int? Parse(string token)
    {
        if (token.Equals("off", StringComparison.OrdinalIgnoreCase))
            return 0;
        return int.TryParse(token, out var value) && value > 0 ? value : null;
    }

    public static int? NextRoute(int current, int count, Func<int, bool> blocked)
    {
        if (count < 2)
            return null;

        for (var offset = 1; offset < count; offset++)
        {
            var candidate = (current + offset) % count;
            if (!blocked(candidate))
                return candidate;
        }

        return null;
    }
}
