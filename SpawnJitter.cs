namespace OpenPrefirePrac;

public static class SpawnJitter
{
    public const int Max = 64;

    public static int? Parse(string token) => int.TryParse(token, out var value) && value >= 0 && value <= Max ? value : null;

    public static (float dx, float dy) Offset(int max, Random rnd)
    {
        if (max == 0)
            return (0, 0);

        return ((float)(rnd.NextDouble() * 2 * max - max), (float)(rnd.NextDouble() * 2 * max - max));
    }
}
