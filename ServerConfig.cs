using System.Text.Json;

namespace Freefrag;

public class DefaultConfig
{
    public int Difficulty { get; set; } = 3;

    public int TrainingMode { get; set; } = 0;

    public int BotWeapon {get; set; } = 0;

    public string PlayerGun { get; set; } = "ak";

    public int Jitter { get; set; } = 0;

    public int HeadshotOnly { get; set; } = 0;

    public int Xray { get; set; } = 0;

    /*
     * 1 = bots shoot back. 0 = bots stand unarmed.
    */
    public int BotsFire { get; set; } = 1;

    /*
     * Bot health (1-1000) and armor (0-100) applied when a bot spawns.
    */
    public int BotHealth { get; set; } = BotHealthSetting.Default;

    public int BotArmor { get; set; } = BotArmorSetting.Default;

    /*
     * Seconds the player is held still before a route starts (0-5). 0 = no countdown.
    */
    public int Countdown { get; set; } = CountdownSetting.Default;

    /*
     * Values that `!freefrag comp on` pins. Every key is optional; out-of-range values are ignored.
    */
    public CompConfig Comp { get; set; } = new();

    /*
     * 0 = chat menus.
     * 1 = screen menus drawn by this plugin in the centre of the screen (W/S/A/D/Tab, or bound keys).
    */
    public int MenuType { get; set; } = 1;

    /*
     * Rows of a screen menu visible at once (1-8); longer menus scroll.
    */
    public int MenuRows { get; set; } = 7;

    /*
     * 0 = disabled.
     * 1 = face and track the owning player every tick (plugin aim lock).
     * 2 = same tracking plus the hard_mode behaviour tree for firing.
    */
    public int BotAimLock {get; set; } = 1;

    public int EquipPlayer { get; set; } = 1;

    private string _moduleDirectory = "";

    public DefaultConfig()
    {
        // DeserializeConstructor
    }

    public DefaultConfig(string moduleDirectory)
    {
        _moduleDirectory = moduleDirectory;
    }
    
    public void LoadDefaultSettings()
    {
        string path = $"{_moduleDirectory}/default_cfg.json";

        if (!File.Exists(path))
        {
            // Use default settings
            Console.WriteLine("[Freefrag] No custom settings provided. Will use default settings.");
        }
        else
        {
            // Load settings from default_cfg.json
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,

            };

            string jsonString = File.ReadAllText(path);
            
            try
            {
                DefaultConfig jsonConfig = JsonSerializer.Deserialize<DefaultConfig>(jsonString, options)!;

                if (jsonConfig.Difficulty > -1 && jsonConfig.Difficulty < 6)
                {
                    Difficulty = jsonConfig.Difficulty;
                }

                if (jsonConfig.TrainingMode > -1 && jsonConfig.TrainingMode < 2)
                {
                    TrainingMode = jsonConfig.TrainingMode;
                }
                
                if (jsonConfig.BotWeapon > -1 && jsonConfig.BotWeapon < 8)
                {
                    BotWeapon = jsonConfig.BotWeapon;
                }

                var playerGun = string.IsNullOrEmpty(jsonConfig.PlayerGun) ? null : PlayerLoadout.ParseGun(jsonConfig.PlayerGun);
                if (playerGun != null)
                {
                    PlayerGun = playerGun;
                }

                if (jsonConfig.Jitter >= 0 && jsonConfig.Jitter <= SpawnJitter.Max)
                {
                    Jitter = jsonConfig.Jitter;
                }

                if (jsonConfig.HeadshotOnly > -1 && jsonConfig.HeadshotOnly < 2)
                {
                    HeadshotOnly = jsonConfig.HeadshotOnly;
                }

                if (jsonConfig.Xray > -1 && jsonConfig.Xray < 2)
                {
                    Xray = jsonConfig.Xray;
                }

                if (jsonConfig.BotsFire > -1 && jsonConfig.BotsFire < 2)
                {
                    BotsFire = jsonConfig.BotsFire;
                }

                if (jsonConfig.BotHealth >= BotHealthSetting.Min && jsonConfig.BotHealth <= BotHealthSetting.Max)
                {
                    BotHealth = jsonConfig.BotHealth;
                }

                if (jsonConfig.BotArmor >= BotArmorSetting.Min && jsonConfig.BotArmor <= BotArmorSetting.Max)
                {
                    BotArmor = jsonConfig.BotArmor;
                }

                if (jsonConfig.Countdown >= CountdownSetting.Min && jsonConfig.Countdown <= CountdownSetting.Max)
                {
                    Countdown = jsonConfig.Countdown;
                }

                if (jsonConfig.Comp != null)
                {
                    Comp.CopyValidFrom(jsonConfig.Comp);
                }

                if (jsonConfig.MenuType > -1 && jsonConfig.MenuType < 2)
                {
                    MenuType = jsonConfig.MenuType;
                }

                if (jsonConfig.MenuRows > 0 && jsonConfig.MenuRows < 9)
                {
                    MenuRows = jsonConfig.MenuRows;
                }

                if (jsonConfig.BotAimLock > -1 && jsonConfig.BotAimLock < 3)
                {
                    BotAimLock = jsonConfig.BotAimLock;
                }

                if (jsonConfig.EquipPlayer > -1 && jsonConfig.EquipPlayer < 2)
                {
                    EquipPlayer = jsonConfig.EquipPlayer;
                }

                Console.WriteLine($"[Freefrag] Using default settings: Difficulty = {Difficulty}, TrainingMode = {TrainingMode}, BotWeapon = {BotWeapon}, PlayerGun = {PlayerGun}, Jitter = {Jitter}, HeadshotOnly = {HeadshotOnly}, Xray = {Xray}, BotsFire = {BotsFire}, BotHealth = {BotHealth}, BotArmor = {BotArmor}, Countdown = {Countdown}, MenuType = {MenuType}, MenuRows = {MenuRows}, BotAimLock = {BotAimLock}, EquipPlayer = {EquipPlayer}");
            }
            catch (System.Exception)
            {
                Console.WriteLine("[Freefrag] Failed to load custom settings. Will use default settings.");
            }
        }
    }
}

/// <summary>The values `!freefrag comp on` applies to a player, so every comp time is set under the same rules.</summary>
public class CompConfig
{
    public int BotsFire { get; set; } = 1;

    public int BotHealth { get; set; } = BotHealthSetting.Default;

    public int BotArmor { get; set; } = BotArmorSetting.Default;

    public int Countdown { get; set; } = CountdownSetting.Default;

    /*
     * 0 = off, -1 = infinite, n > 0 = n repeats (see RepeatSetting).
    */
    public int Repeat { get; set; } = RepeatSetting.Off;

    public int Jitter { get; set; } = 0;

    /// <summary>Takes each value of <paramref name="other"/> that is in range; an out-of-range value leaves the current one.</summary>
    public void CopyValidFrom(CompConfig other)
    {
        if (other.BotsFire > -1 && other.BotsFire < 2)
            BotsFire = other.BotsFire;
        if (other.BotHealth >= BotHealthSetting.Min && other.BotHealth <= BotHealthSetting.Max)
            BotHealth = other.BotHealth;
        if (other.BotArmor >= BotArmorSetting.Min && other.BotArmor <= BotArmorSetting.Max)
            BotArmor = other.BotArmor;
        if (other.Countdown >= CountdownSetting.Min && other.Countdown <= CountdownSetting.Max)
            Countdown = other.Countdown;
        if (other.Repeat >= RepeatSetting.Infinite)
            Repeat = other.Repeat;
        if (other.Jitter >= 0 && other.Jitter <= SpawnJitter.Max)
            Jitter = other.Jitter;
    }
}
