using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Guards the shape of resources/bt/bt_hard_config.kv3 against the engine's bt_config.kv3 (synced 2026-10-18).
/// CS2 rejects the whole behavior tree when the 'attack' table has the wrong number of rows, so bots silently
/// fall back to the stock profile. See README.md, "Re-syncing the bot behavior tree after a CS2 update".
/// </summary>
public class BehaviorTreeConfigTests
{
    private const int AttackRows = 13;      // KNIFE .. STACKABLEITEM, UNKNOWN
    private const int AttackColumns = 5;    // duration, duration deviation, cadence, cooldown, cooldown deviation

    private static readonly string[] RequiredKeys =
    [
        "aim_target_acquisition_lerp_time", "aim_target_acquisition_lerp_time_deviation",
        "aim_target_acquisition_angle_penalty", "aim_target_acquisition_angle_penalty_deviation",
        "aim_target_acquisition_angle_penalty_reduction_ratio", "aim_target_acquisition_angle_tolerance",
        "aim_target_acquisition_angle_lerp_bias", "aim_target_tracking_lerp_time",
        "aim_target_tracking_lerp_time_deviation", "aim_target_tracking_focus_interval",
        "aim_target_tracking_focus_interval_deviation", "aim_target_tracking_angle_lerp_bias",
        "aim_ready_angle_tolerance", "aim_new_target_angle_tolerance", "aim_max_duration",
        "aim_max_duration_deviation", "aim_punch_angle_reaction_chance",
        "look_around_awareness_yaw_range", "look_around_awareness_pitch_range", "look_around_focus_interval",
        "look_around_focus_interval_deviation", "look_around_lerp_time", "look_around_lerp_time_deviation",
        "look_around_lerp_bias", "reaction_time", "combat_crouch_chance", "combat_dodge_command_duration",
        "combat_dodge_command_duration_deviation", "attack",
    ];

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Freefrag.csproj")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(parts.Prepend(dir!).ToArray());
    }

    private static string StripComments(string text) =>
        Regex.Replace(text, @"//[^\n]*|<!--.*?-->", "", RegexOptions.Singleline);

    [Fact]
    public void HardConfigAttackTableHasEngineShape()
    {
        var text = StripComments(File.ReadAllText(RepoFile("resources", "bt", "bt_hard_config.kv3")));
        var attack = Regex.Match(text, @"attack\s*=\s*\[(.*?)\]\s*\}", RegexOptions.Singleline);
        Assert.True(attack.Success, "no attack table found");

        var rows = Regex.Matches(attack.Groups[1].Value, @"\[([^\]]*)\]").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(AttackRows, rows.Count);
        foreach (var row in rows)
            Assert.Equal(AttackColumns, row.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void HardConfigHasEveryEngineKeyOnce()
    {
        var text = StripComments(File.ReadAllText(RepoFile("resources", "bt", "bt_hard_config.kv3")));
        var keys = Regex.Matches(text, @"^\s*([a-z_]+)\s*=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Where(k => k != "default").ToList();
        Assert.Equal(RequiredKeys.OrderBy(k => k), keys.OrderBy(k => k));
    }

    [Fact]
    public void HardModeTreeReferencesItsOwnConfig()
    {
        var text = File.ReadAllText(RepoFile("resources", "bt", "hard_mode.kv3"));
        Assert.Contains("bt_hard_config.kv3", text);
        Assert.Contains("bt_memorize_enemies_vision.kv3", text);
    }
}
