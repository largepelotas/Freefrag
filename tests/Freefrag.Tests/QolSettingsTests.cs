using Freefrag;
using Xunit;

public class QolSettingsTests
{
    [Fact]
    public void BotLoadoutParsesAllTokensCaseInsensitively()
    {
        // Prevents command shortcuts from rejecting valid bot loadout names or accepting unknown ones.
        Assert.Equal(0, BotLoadout.ParseBotWeapon("RAND"));
        Assert.Equal(1, BotLoadout.ParseBotWeapon("ump"));
        Assert.Equal(2, BotLoadout.ParseBotWeapon("ak"));
        Assert.Equal(3, BotLoadout.ParseBotWeapon("sct"));
        Assert.Equal(4, BotLoadout.ParseBotWeapon("awp"));
        Assert.Equal(5, BotLoadout.ParseBotWeapon("pistol"));
        Assert.Equal(6, BotLoadout.ParseBotWeapon("eco"));
        Assert.Equal(7, BotLoadout.ParseBotWeapon("force"));
        Assert.Null(BotLoadout.ParseBotWeapon("knife"));
    }

    [Fact]
    public void BotLoadoutRandomItemsStayWithinTheirPreset()
    {
        // Prevents pistol and eco presets from granting armor or weapons outside their documented pools.
        var random = new Random(7);
        for (var i = 0; i < 50; i++)
        {
            Assert.Contains(BotLoadout.ItemsFor(5, random).Single(), new[] { "weapon_glock", "weapon_usp_silencer", "weapon_p250" });
            Assert.Contains(BotLoadout.ItemsFor(6, random).Single(), new[] { "weapon_tec9", "weapon_fiveseven", "weapon_p250", "weapon_cz75a" });
            var force = BotLoadout.ItemsFor(7, random);
            Assert.Contains(force[0], new[] { "weapon_galilar", "weapon_famas", "weapon_mac10", "weapon_mp9" });
            Assert.Contains("item_assaultsuit", force);
        }

    }

    [Fact]
    public void BotLoadoutRandomRollsAPresetPerBot()
    {
        // Random is decided per spawn, not per round: over many rolls more than one preset shows up,
        // and every roll is one of the named presets (never empty hands).
        var random = new Random(3);
        var seen = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var items = BotLoadout.ItemsFor(0, random);
            Assert.NotEmpty(items);
            seen.Add(items[0]);
        }
        Assert.True(seen.Count > 1);
    }

    [Fact]
    public void PlayerLoadoutBuildsPrimaryAndPistolKits()
    {
        // Prevents the none option from silently restoring a rifle or giving more than one pistol.
        var none = PlayerLoadout.ItemsFor("none");
        Assert.DoesNotContain(none, item => item is "weapon_ak47" or "weapon_m4a1" or "weapon_m4a1_silencer" or "weapon_awp");
        Assert.Equal(1, none.Count(item => item is "weapon_usp_silencer" or "weapon_glock" or "weapon_deagle"));
        var ak = PlayerLoadout.ItemsFor("ak");
        Assert.Contains("weapon_ak47", ak);
        Assert.Contains("weapon_deagle", ak);
        Assert.Equal("awp", PlayerLoadout.ParseGun("AWP"));
        Assert.Null(PlayerLoadout.ParseGun("p90"));
    }

    [Fact]
    public void SpawnJitterStaysInsideConfiguredBounds()
    {
        // Prevents spawn offsets from exceeding the configured maximum or moving bots when disabled.
        Assert.Equal((0f, 0f), SpawnJitter.Offset(0, new Random(1)));
        var random = new Random(11);
        var hasNonZeroOffset = false;
        for (var i = 0; i < 200; i++)
        {
            var offset = SpawnJitter.Offset(64, random);
            Assert.InRange(offset.dx, -64, 64);
            Assert.InRange(offset.dy, -64, 64);
            hasNonZeroOffset |= offset != (0f, 0f);
        }

        Assert.True(hasNonZeroOffset);
        Assert.Null(SpawnJitter.Parse("65"));
        Assert.Null(SpawnJitter.Parse("-1"));
    }

    [Fact]
    public void RepeatCountsDownAndInfiniteNeverChanges()
    {
        // Prevents a finite repeat setting from restarting forever or an infinite setting from expiring.
        Assert.Equal(-1, RepeatSetting.Parse("inf"));
        Assert.Equal(0, RepeatSetting.Parse("off"));
        Assert.Equal(3, RepeatSetting.Parse("3"));
        Assert.Null(RepeatSetting.Parse("x"));

        var remaining = 2;
        Assert.True(RepeatSetting.ShouldRestart(ref remaining));
        Assert.Equal(1, remaining);
        Assert.True(RepeatSetting.ShouldRestart(ref remaining));
        Assert.Equal(0, remaining);
        Assert.False(RepeatSetting.ShouldRestart(ref remaining));

        remaining = RepeatSetting.Infinite;
        Assert.True(RepeatSetting.ShouldRestart(ref remaining));
        Assert.Equal(RepeatSetting.Infinite, remaining);
    }

    [Fact]
    public void AutoSkipFindsNextAvailableRoute()
    {
        // Prevents auto-skip from restarting the current route or choosing a route another player blocks.
        Assert.Equal(0, AutoSkip.NextRoute(2, 3, _ => false));
        Assert.Equal(1, AutoSkip.NextRoute(0, 4, route => route is 2 or 3));
        // Prevents a blocked first candidate or a blocked wrap-around candidate from being returned.
        Assert.Equal(2, AutoSkip.NextRoute(0, 4, i => i == 1));
        Assert.Equal(1, AutoSkip.NextRoute(3, 4, i => i == 0));
        Assert.Null(AutoSkip.NextRoute(0, 3, route => route != 0));
        Assert.Null(AutoSkip.NextRoute(0, 1, _ => false));
    }
}
