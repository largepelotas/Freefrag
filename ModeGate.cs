namespace Freefrag;

/// <summary>What a mode signal did to <see cref="ModeGate.Active"/>.</summary>
public enum ModeTransition { None, Activated, Deactivated }

/// <summary>
/// Tracks the server's current game mode and decides whether Freefrag is active (Prefire) or dormant (any other mode
/// or unknown). Pure: the plugin feeds it mode names from the mode cfgs and from GameModeManager's API.
/// </summary>
public sealed class ModeGate
{
    public const string PrefireMode = "Prefire";

    private static readonly HashSet<string> DormantSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "modes", "gamemodes", "lang", "top", "stats", "help", "binds",
    };

    /// <summary>The last mode name applied; null means unknown or none.</summary>
    public string? CurrentMode { get; private set; }

    public bool Active => IsPrefire(CurrentMode);

    public static bool IsPrefire(string? name)
        => name != null && string.Equals(name.Trim(), PrefireMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>The mode name carried by a signal argument; null/blank/"none" (any case) mean no mode.</summary>
    public static string? ParseSignal(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return null;
        }

        var trimmed = arg.Trim();
        return trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    /// <summary>Records the mode and reports whether <see cref="Active"/> flipped.</summary>
    public ModeTransition Apply(string? modeName)
    {
        var wasActive = Active;
        CurrentMode = modeName;
        var isActive = Active;

        if (isActive == wasActive)
        {
            return ModeTransition.None;
        }

        return isActive ? ModeTransition.Activated : ModeTransition.Deactivated;
    }

    /// <summary>Whether a !freefrag subcommand may run while dormant. No subcommand opens the dormant menu.</summary>
    public static bool AllowedWhileDormant(string? subcommand)
        => string.IsNullOrEmpty(subcommand) || DormantSubcommands.Contains(subcommand);
}
