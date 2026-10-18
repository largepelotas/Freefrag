namespace Freefrag;

public static class ConvarRestore
{
    /// Convars the plugin owns for as long as Prefire is active. RestoreConvars must not put the game
    /// mode's values back for these, or the engine refills the bot quota with strays the moment the
    /// last player leaves practice. Outside Prefire the plugin is dormant and never touches them; the
    /// next mode's cfg sets its own values.
    public static readonly IReadOnlySet<string> OwnedWhileLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bot_quota", "bot_quota_mode" };

    public static bool ShouldRestore(string convarName) => !OwnedWhileLoaded.Contains(convarName);
}
