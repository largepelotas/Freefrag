using Freefrag;
using Xunit;

public class CheapSettingsTests
{
    [Theory]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("1001", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("1000", 1000)]
    public void BotHealthRejectsOutOfRangeInsteadOfClamping(string token, int? expected)
    {
        // Prevents "!freefrag bothealth 0" from spawning invincible or already-dead bots, and 5000 from being clamped silently.
        Assert.Equal(expected, BotHealthSetting.Parse(token));
    }

    [Theory]
    [InlineData("-1", null)]
    [InlineData("101", null)]
    [InlineData("x", null)]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    public void BotArmorRejectsOutOfRangeInsteadOfClamping(string token, int? expected)
    {
        // Prevents a typo such as armor 1000 from becoming 100 without telling the player.
        Assert.Equal(expected, BotArmorSetting.Parse(token));
    }

    [Theory]
    [InlineData("-1", null)]
    [InlineData("6", null)]
    [InlineData("three", null)]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("5", 5)]
    public void CountdownRejectsOutOfRangeInsteadOfClamping(string token, int? expected)
    {
        // Prevents a long typo such as countdown 30 from freezing the player for half a minute.
        Assert.Equal(expected, CountdownSetting.Parse(token));
    }

    [Fact]
    public void RangeSettingHandlesNullAndWhitespace()
    {
        // Prevents a bare "!freefrag bothealth" from throwing instead of printing usage.
        Assert.Null(BotHealthSetting.Parse(null));
        Assert.Null(BotHealthSetting.Parse("  "));
        Assert.Equal(25, BotHealthSetting.Parse(" 25 "));
    }

    [Fact]
    public void CompLockRefusesExactlyTheComparabilitySettingsWhileCompIsOn()
    {
        // Prevents comp times from being set with easier bots, and prevents hs/xray/difficulty from being locked by mistake.
        foreach (var setting in new[]
                 {
                     LockedSetting.BotsFire, LockedSetting.BotHealth, LockedSetting.BotArmor,
                     LockedSetting.Countdown, LockedSetting.Repeat, LockedSetting.Jitter
                 })
        {
            Assert.True(CompLock.Refuses(true, setting), setting.ToString());
            Assert.False(CompLock.Refuses(false, setting), setting.ToString());
        }
    }

    [Fact]
    public void CompConfigDefaultsMatchThePinnedValues()
    {
        // Prevents the comp preset from drifting away from the documented bots-shoot, 100/100, 3 s countdown, no repeat, no jitter.
        var comp = new CompConfig();
        Assert.Equal(1, comp.BotsFire);
        Assert.Equal(100, comp.BotHealth);
        Assert.Equal(100, comp.BotArmor);
        Assert.Equal(3, comp.Countdown);
        Assert.Equal(RepeatSetting.Off, comp.Repeat);
        Assert.Equal(0, comp.Jitter);
        Assert.Equal(1, new DefaultConfig().Comp.BotsFire);
    }

    [Fact]
    public void CompConfigKeepsDefaultsForOutOfRangeValuesAndTakesValidOnes()
    {
        // Prevents a bad default_cfg.json from pinning impossible comp values (zero-health bots, negative jitter).
        var target = new CompConfig();
        target.CopyValidFrom(new CompConfig { BotsFire = 2, BotHealth = 0, BotArmor = 101, Countdown = 9, Repeat = -2, Jitter = 65 });
        Assert.Equal(new CompConfig().BotsFire, target.BotsFire);
        Assert.Equal(100, target.BotHealth);
        Assert.Equal(100, target.BotArmor);
        Assert.Equal(3, target.Countdown);
        Assert.Equal(RepeatSetting.Off, target.Repeat);
        Assert.Equal(0, target.Jitter);

        target.CopyValidFrom(new CompConfig { BotsFire = 0, BotHealth = 250, BotArmor = 0, Countdown = 5, Repeat = RepeatSetting.Infinite, Jitter = 16 });
        Assert.Equal(0, target.BotsFire);
        Assert.Equal(250, target.BotHealth);
        Assert.Equal(0, target.BotArmor);
        Assert.Equal(5, target.Countdown);
        Assert.Equal(RepeatSetting.Infinite, target.Repeat);
        Assert.Equal(16, target.Jitter);
    }

    [Fact]
    public void PlayerStatusTakesCheapSettingDefaultsFromServerConfig()
    {
        // Prevents the per-player settings from ignoring default_cfg.json, and comp from starting on.
        var config = new DefaultConfig { BotsFire = 0, BotHealth = 40, BotArmor = 0, Countdown = 1 };
        var status = new PlayerStatus(config);
        Assert.False(status.BotsFire);
        Assert.Equal(40, status.BotHealth);
        Assert.Equal(0, status.BotArmor);
        Assert.Equal(1, status.Countdown);
        Assert.False(status.Comp);

        var plain = new PlayerStatus(new DefaultConfig());
        Assert.True(plain.BotsFire);
        Assert.Equal(100, plain.BotHealth);
        Assert.Equal(100, plain.BotArmor);
        Assert.Equal(3, plain.Countdown);
    }

    // Prevents: a stray SwingTrigger in default_cfg.json (0, 99, 9000) releasing swingers instantly or never.
    [Fact]
    public void SwingTriggerOutOfRangeKeepsDefault()
    {
        Assert.Equal(600, SwingTriggerSetting.Default);
        Assert.False(SwingTriggerSetting.InRange(99));
        Assert.True(SwingTriggerSetting.InRange(100));
        Assert.True(SwingTriggerSetting.InRange(3000));
        Assert.False(SwingTriggerSetting.InRange(3001));
        Assert.Equal(100f, SwingTriggerSetting.Clamp(50f));
        Assert.Equal(3000f, SwingTriggerSetting.Clamp(9000f));
        Assert.Equal(450f, SwingTriggerSetting.Clamp(450f));
        Assert.Equal(SwingTriggerSetting.Default, new DefaultConfig().SwingTrigger);
    }
}
