namespace Freefrag;

public static class ConvarRestore
{
    /// Convars the plugin owns for as long as it is loaded. RestoreConvars must not put the game
    /// mode's values back for these, or the engine refills the bot quota with strays the moment the
    /// last player leaves practice. The mode cfgs set their own values when the server leaves
    /// Prefire mode and the plugin is unloaded.
    public static readonly IReadOnlySet<string> OwnedWhileLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bot_quota", "bot_quota_mode" };

    public static bool ShouldRestore(string convarName) => !OwnedWhileLoaded.Contains(convarName);
}
