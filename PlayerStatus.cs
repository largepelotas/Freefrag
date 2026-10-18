using CounterStrikeSharp.API.Core;

namespace OpenPrefirePrac;

public class PlayerStatus
{
    /**
     * -1 if player is not practicing
     */
    public int PracticeIndex = -1;
    
    public int Progress = 0;
    
    /**
     * 0: No healing
     * 1: Init hp 500 with no healing
     * 2: +25hp for each kill
     * 3: Reheal to 100hp after a kill (default)
     * 4: +100hp for each kill
     * 5: +500hp for each kill
     */
    public int HealingMethod = 3;
    
    public readonly List<int> Bots = new();         // slot of bots
    public readonly Dictionary<string, int> LocalizedPracticeNames = new();
    public readonly Dictionary<string, int> LocalizedDifficultyNames = new();
    
    /**
     * 0: Random mode, randomly spawn some targets(the spawn ratio is specified in the practice profile)
     * 1: Full mode, all targets
     */
    public int TrainingMode = 0;
    
    public readonly Dictionary<string, int> LocalizedTrainingModeNames = new();

    public readonly List<int> EnabledTargets = new();
    public readonly List<int> Beams = new();

    // Guiding-line recorder (!prefire record)
    public bool IsRecording = false;
    public readonly List<(float x, float y, float z)> RecordedPoints = new();
    public readonly List<int> RecordingBeams = new();
    public CounterStrikeSharp.API.Modules.Timers.Timer? RecordingTimer = null;

    /**
     * 0: Bots buy weapons randomly.
     * 1: Bots use UMP45.
     * 2: Bots use AK47.
     * 3: Bots use Scout.
     * 4: Bots use AWP.
     * 5: Bots use Pistols.
     * 6: Bots use Eco weapons.
     * 7: Bots use Force-buy weapons.
     */
    public int BotWeapon = 0;
    public string PlayerGun = "ak";
    public int Jitter = 0;
    public int Repeat = RepeatSetting.Off;
    public int AutoSkipDeaths = 0;
    public int DeathsOnRoute = 0;
    public int PendingRoute = -1;
    public bool HeadshotOnly = false;
    public bool Xray = false;
    public readonly RunTracker Run = new();
    public readonly Dictionary<int, List<int>> GlowEntities = new();         // bot slot -> entity indices of its glow props

    public PlayerStatus(DefaultConfig defaultConfig)
    {
        HealingMethod = defaultConfig.Difficulty;
        TrainingMode = defaultConfig.TrainingMode;
        BotWeapon = defaultConfig.BotWeapon;
        PlayerGun = defaultConfig.PlayerGun;
        Jitter = defaultConfig.Jitter;
        HeadshotOnly = defaultConfig.HeadshotOnly == 1;
        Xray = defaultConfig.Xray == 1;
    }

    public PlayerStatus()
    {
        // Default constructor
    }
}
