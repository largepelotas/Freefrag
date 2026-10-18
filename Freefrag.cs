using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Globalization;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Core.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities;

namespace Freefrag;

public class Freefrag : BasePlugin
{
    public override string ModuleName => "Freefrag";
    public override string ModuleVersion => "0.1.55";
    public override string ModuleAuthor => "Klitch";
    public override string ModuleDescription => "Klitch's Freefrag: prefire practice for CS2, a fork of OpenPrefirePrac by Lengran. https://github.com/largepelotas/Freefrag";

    private readonly Dictionary<int, PlayerStatus> _playerStatuses = new();         // Key: slot of player
    
    private readonly Dictionary<int, int> _ownerOfBots = new();       // Map: slot of bot -> owner.
    
    private readonly Dictionary<string, int> _practiceNameToId = new();
    
    private readonly Dictionary<int, int> _pracConflictCount = new();           // Num of on-going practices that might interfere
    
    private string _mapName = "";
    
    private int _playerCount;
    
    private readonly List<PrefirePractice> _practices = new();
    
    private readonly List<string> _availableMaps = new();

    private readonly ServerStatus _serverStatus = new();

    private CCSGameRules ?_serverGameRules;
    
    private Translator ?_translator;
    private ScoreRepository? _scores;

    private readonly BotRequestQueue _botRequests = new();        // One player's bots spawn at a time; see BotRequestQueue.

    private const float BotSpawnInterval = 0.1f;
    private const float BotSpawnTimeout = 8f;                      // Seconds after the last bot_add before an unfulfilled request is dropped.
    private const string ChatPrefix = "[Klitch's Freefrag]";       // Chat-facing name; console log lines use [Freefrag].

    private DefaultConfig ?_defaultPlayerSettings;

    private CommandDefinition ?_command;

    private ScreenMenu ?_screenMenu;                                // In-plugin centre-screen menus (MenuType 1); created in Load.

    private CounterStrikeSharp.API.Modules.Timers.Timer ?_timerBroadcastProgress;

    private int _glowCount;                                         // Bots currently wearing an x-ray glow; lets CheckTransmit skip all work when zero.

    private readonly HashSet<string> _hookErrorsLogged = new();     // Error types already reported from engine hooks.

    private readonly ModeGate _modeGate = new();                    // Active only while the server is in Prefire mode; dormant otherwise.

    private bool _modeApiFailureLogged;                             // The GameModeManager API read failed once already; stay quiet after that.

    public override void Load(bool hotReload)
    {
        _playerCount = 0;

        _translator = new Translator(Localizer, ModuleDirectory, CultureInfo.CurrentCulture.Name);

        try
        {
            var scoresPath = Path.Combine(ModuleDirectory, "scores.db");
            _scores = new ScoreRepository(scoresPath);
            _scores.EnsureCreated();
            Console.WriteLine($"[Freefrag] Scores DB at {scoresPath}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Freefrag] Scores disabled: {e.Message}");
            _scores = null;
        }

        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterListener<Listeners.OnTick>(OnTickHandler);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnTakeDamage);

        _defaultPlayerSettings = new DefaultConfig(ModuleDirectory);
        _defaultPlayerSettings.LoadDefaultSettings();

        _screenMenu = new ScreenMenu(
            () => _defaultPlayerSettings?.MenuRows ?? 7,
            p => _translator!.Translate(p, "menu.footer"));
        RegisterMenuCommands();

        if (hotReload)
        {
            // Clear status registers
            _ownerOfBots.Clear();
            _practiceNameToId.Clear();
            _pracConflictCount.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerStatuses.Clear();
            _botRequests.Clear();
            _glowCount = 0;
            
            // Clear saved convars
            _serverStatus.WarmupStatus = false;
            _serverStatus.BoolConvars.Clear();
            _serverStatus.IntConvars.Clear();
            _serverStatus.FloatConvars.Clear();
            _serverStatus.StringConvars.Clear();

            // // Setup map (20250109: Moved outside of the hotReload block)
            // OnMapStartHandler(Server.MapName);
        }

        // Register players already on the server. This used to run only on hotReload, so a manual
        // unload-then-load left connected players without a status entry and every !freefrag command
        // threw KeyNotFoundException until they reconnected.
        foreach (var tempPlayer in Utilities.GetPlayers())
        {
            if (tempPlayer == null || !tempPlayer.IsValid || tempPlayer.IsBot || tempPlayer.IsHLTV)
            {
                continue;
            }

            OnClientPutInServerHandler(tempPlayer.Slot);
        }

        // Setup map and practice routes
        // Note 20250109: 
        //   This line is moved from inside the hotReload block to support 
        //   mannually-unload-then-load way of loading the plugin. It seems
        //   work okay except throwing a "Failed to load practices on map" 
        //   during server's cold start phase so I guess I'll just leave it
        //   here. If the cold start is found broken in any future issues, 
        //   find another way to do this. Otherwise delete these comments 
        //   later.
        OnMapStartHandler(Server.MapName);

        RegisterCommand();

        // The mode cfgs tell us which mode the server is in (console only); the sender is never a player.
        AddCommand("css_freefrag_mode", "Freefrag: set the active game mode (sent by the mode cfgs); 'none' = dormant", (player, commandInfo) =>
        {
            if (player != null)
            {
                return;
            }

            ApplyModeSignal(ModeGate.ParseSignal(commandInfo.ArgCount > 1 ? commandInfo.ArgByIndex(1) : null), "cfg");
        });

        // !modes / /modes opens this plugin's game modes menu in every state. deploy.sh turns off GameModeManager's
        // own css_modes (Commands.Modes=false): CounterStrikeSharp runs every callback of a command name, and its
        // permission failure for players without @css/changemap would cut ours off. The permission for switching
        // stays in BuildGameModesMenu (GameModesPermission).
        AddCommand("css_modes", "Freefrag: open the game modes menu", (player, _) =>
        {
            if (player == null || !player.IsValid || player.IsBot)
            {
                return;
            }

            OnClientPutInServerHandler(player.Slot);        // Idempotent: makes sure the player has a status entry.
            OpenMenu(player, BuildGameModesMenu(player));
        });

        // Freefrag loads before GameModeManager at server start, so this read may come back empty; the cfg signal
        // and the next map start fill it in. On a hot reload during Prefire it finds the mode right away.
        ApplyModeSignal(ReadGameModeManagerMode(), "GameModeManager");

        if (_timerBroadcastProgress == null)
        {
            _timerBroadcastProgress = AddTimer(3f, () => PrintProgress(), TimerFlags.REPEAT);
        }

        Console.WriteLine("[Freefrag] Plugin has been loaded. If the plugin is neither loaded along with server startup, nor reloaded on the fly, please reload it once to make it fully functional.");
        Console.WriteLine($"[Freefrag] mode: {_modeGate.CurrentMode ?? "unknown"} -> {(_modeGate.Active ? "active" : "dormant")}");
    }

    /// <summary>
    /// GameModeManager's current mode name, or null when it cannot be read (not loaded yet, API capability missing,
    /// shared assembly missing). Never throws.
    /// </summary>
    private string? ReadGameModeManagerMode()
    {
        try
        {
            var api = new PluginCapability<GameModeManager.Shared.IGameModeApi>("game_mode:api").Get();
            return api?.State?.CurrentMode?.Name;
        }
        catch (Exception e)
        {
            if (!_modeApiFailureLogged)
            {
                _modeApiFailureLogged = true;
                Console.WriteLine($"[Freefrag] GameModeManager mode API unavailable ({e.GetType().Name}); relying on the mode cfg signal.");
            }
            return null;
        }
    }

    /// <summary>Feeds a mode name to the gate and reacts when Freefrag wakes up or goes dormant.</summary>
    private void ApplyModeSignal(string? modeName, string source)
    {
        var transition = _modeGate.Apply(modeName);
        if (transition == ModeTransition.None)
        {
            return;
        }

        Console.WriteLine($"[Freefrag] Mode {modeName ?? "none"} from {source}: Freefrag is now {(transition == ModeTransition.Activated ? "active" : "dormant")}.");
        if (transition == ModeTransition.Deactivated)
        {
            EnterDormant();
        }
    }

    /// <summary>
    /// The server left Prefire: stop every practice through the normal exit path (kicks the bots and, with the last
    /// practicing player, restores the convars), cancel countdowns and close the menus, whose contents change.
    /// Must never call SaveConvars: originals are only ever captured inside Prefire.
    /// </summary>
    private void EnterDormant()
    {
        foreach (var slot in _playerStatuses.Keys.ToList())
        {
            var status = _playerStatuses[slot];
            var player = Utilities.GetPlayerFromSlot(slot);

            if (status.PracticeIndex >= 0)
            {
                ExitPrefireMode(slot);
                if (player != null && player.IsValid)
                {
                    player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "practice.exit")}");
                }
            }
            else if (status.PendingRoute >= 0)
            {
                // Not practicing, so no convars were saved: only the pending route and countdown need clearing.
                UnsetPrefireMode(slot);
            }

            CancelCountdown(slot);

            if (player != null && player.IsValid && !player.IsBot)
            {
                // Silent close (CloseAllMenus would chat "menu closed" to everyone on the server).
                if (UseScreenMenu())
                {
                    if (_screenMenu!.IsOpen(player))
                    {
                        _screenMenu.Close(player);
                    }
                }
                else
                {
                    MenuManager.CloseActiveMenu(player);
                }
            }
        }

        if (_playerCount != 0)
        {
            // Should not happen: every practicing player was exited above.
            Console.WriteLine($"[Freefrag] Warning: {_playerCount} practicing player(s) left after going dormant; resetting.");
            _playerCount = 0;
            foreach (var botSlot in _ownerOfBots.Keys.ToList())
            {
                KickBot(botSlot);
            }
            foreach (var status in _playerStatuses.Values)
            {
                status.Bots.Clear();
                status.PracticeIndex = -1;
                status.PendingRoute = -1;
            }
        }

        _botRequests.Clear();
    }

    public override void Unload(bool hotReload)
    {
        try { ScoreRepository.ReleaseAll(); } catch (Exception) { }

        UnregisterCommand();
        _screenMenu?.Clear();

        RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RemoveListener<Listeners.OnMapStart>(OnMapStartHandler);
        RemoveListener<Listeners.OnTick>(OnTickHandler);
        RemoveListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RemoveListener<Listeners.OnEntityTakeDamagePre>(OnTakeDamage);

        // Glow props outlive the plugin otherwise; Unload clears the status registers below.
        foreach (var ownerSlot in _playerStatuses.Keys.ToList())
        {
            RemoveAllGlow(ownerSlot);
        }

        if (hotReload)
        {
            // Clear status registers
            _ownerOfBots.Clear();
            _practiceNameToId.Clear();
            _pracConflictCount.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerStatuses.Clear();
            _botRequests.Clear();
            
            // Clear saved convars
            _serverStatus.WarmupStatus = false;
            _serverStatus.BoolConvars.Clear();
            _serverStatus.IntConvars.Clear();
            _serverStatus.FloatConvars.Clear();
            _serverStatus.StringConvars.Clear();
        }

        if (_timerBroadcastProgress != null)
        {
            _timerBroadcastProgress.Kill();
            _timerBroadcastProgress = null;
        }

        Console.WriteLine("[Freefrag] Plugin has been unloaded.");
    }

    // TODO: Figure out if we can use the GameEventHandler attribute here instead
    // [GameEventHandler]
    public void OnClientPutInServerHandler(int slot)
    {
        // var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));
        var player = Utilities.GetPlayerFromSlot(slot);

        if (player == null || !player.IsValid || player.IsHLTV)
        {
            return;
        }

        if (player.IsBot)
        {
            // For bots: If someone is practicing and it's an unmanaged bot, add or kick the bot
            if (_modeGate.Active && _playerCount > 0 && !_ownerOfBots.ContainsKey(slot))
            {
                // Only the active request's bots are being spawned, so this bot belongs to its owner
                // unless the bot's team says otherwise (then it came from somewhere else).
                var ownerSlot = _botRequests.Claim(player.TeamNum, out var nextRequest);
                if (ownerSlot.HasValue && _playerStatuses.ContainsKey(ownerSlot.Value))
                {
                    _playerStatuses[ownerSlot.Value].Bots.Add(slot);
                    _ownerOfBots.Add(slot, ownerSlot.Value);
                    Console.WriteLine($"[Freefrag] Bot {player.PlayerName}, slot: {slot} (team {player.TeamNum}) assigned to player slot {ownerSlot.Value}.");
                }
                else
                {
                    // Nobody is waiting for this bot (or its owner is gone): kick it.
                    KickBot(slot);
                }

                if (nextRequest != null)
                {
                    IssueBotRequest(nextRequest);
                }
            }
        }
        else
        {
            // For players (idempotent: Load() also calls this for players already connected):
            if (_playerStatuses.ContainsKey(slot))
                return;

            // A slot reused after a disconnect we never saw must not inherit the previous player's open menu.
            _screenMenu?.ForgetPlayer(slot);
            _playerStatuses.Add(slot, new PlayerStatus(_defaultPlayerSettings!));

            // Record player language
            _translator!.RecordPlayerCulture(player);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null)
        {
            // Console.WriteLine("[Freefrag] OnPlayerDisconnect doesn't work now. Player = null.");
            return HookResult.Continue;
        }

        _screenMenu?.ForgetPlayer(player.Slot);

        if (!_playerStatuses.ContainsKey(player.Slot))
            return HookResult.Continue;

        if (_playerStatuses[player.Slot].PracticeIndex != -1)
        {
            ExitPrefireMode(player.Slot);
        }

        // Release resources(practices, targets, bots...)
        _playerStatuses.Remove(player.Slot);
        DropBotRequest(player.Slot);

        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        _mapName = map;

        // Entities do not survive a map change; stale indices must not be killed or hidden later.
        foreach (var status in _playerStatuses.Values)
        {
            status.GlowEntities.Clear();
        }
        _glowCount = 0;

        // load practices available in current map, from corresponding map directory.
        _availableMaps.Clear();
        var mapDirectories = new List<string>(Directory.EnumerateDirectories(ModuleDirectory + "/maps"));
        var found = false;
        for (var i = 0; i < mapDirectories.Count; i++)
        {
            var mapPath = mapDirectories[i].Substring(mapDirectories[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
            // Console.WriteLine($"[Freefrag] Map folder for map {mapPath} found.");
            _availableMaps.Add(mapPath);

            if (mapPath.Equals(_mapName))
            {
                found = true;
                Console.WriteLine("[Freefrag] Map folder for current map found.");
            }
        }

        if (found)
        {
            LoadPractice();
        }
        else
        {
            Console.WriteLine("[Freefrag] Failed to load practices on map " + _mapName);
        }

        // A failed API read must never deactivate a mode the cfg already signalled, so only a real mode name counts.
        var apiMode = ReadGameModeManagerMode();
        if (apiMode != null)
        {
            ApplyModeSignal(apiMode, "map start");
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!_modeGate.Active)
        {
            return HookResult.Continue;
        }

        var playerOrBot = @event.Userid;
        
        if (playerOrBot == null || !playerOrBot.IsValid|| playerOrBot.IsHLTV)
        {
            // Console.WriteLine($"[Freefrag] DEBUG: player is null or hltv.");
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot)
        {
            // Console.WriteLine($"[Freefrag] DEBUG: A bot {playerOrBot.PlayerName} just spawned.");
            if (_ownerOfBots.ContainsKey(playerOrBot.Slot))
            {
                // Console.WriteLine($"[Freefrag] DEBUG: {playerOrBot.PlayerName} is a managed bot.");
                // For managed bots
                int ownerSlot = _ownerOfBots[playerOrBot.Slot];
                var targetNo = _playerStatuses[ownerSlot].Progress;
                var practiceIndex = _playerStatuses[ownerSlot].PracticeIndex;

                if (targetNo < _playerStatuses[ownerSlot].EnabledTargets.Count)
                {
                    // If there are more targets to place, move bot to next place
                    _playerStatuses[ownerSlot].Progress++;
                    // Console.WriteLine($"[Freefrag] DEBUG: Update progress to {_playerStatuses[owner].Progress}.");

                    AddTimer(0.5f, () => FreezeBot(playerOrBot));

                    var target = _practices[practiceIndex].Targets[_playerStatuses[ownerSlot].EnabledTargets[targetNo]];
                    var position = target.Position;
                    if (_playerStatuses[ownerSlot].Jitter > 0)
                    {
                        var (dx, dy) = SpawnJitter.Offset(_playerStatuses[ownerSlot].Jitter, new Random());
                        position = new Vector(position.X + dx, position.Y + dy, position.Z);
                    }
                    AddTimer(0.55f, () => MovePlayer(playerOrBot, target.IsCrouching, position, target.Rotation));
                    AddTimer(0.6f, () => ApplyGlow(ownerSlot, playerOrBot));
                    

                    // Give bot weapons. With bots-fire off the bot is stripped and gets nothing back
                    // (money 0 also stops it buying), so it stands frozen with empty hands.
                    var botsFire = _playerStatuses[ownerSlot].BotsFire;
                    var items = BotLoadout.ItemsFor(_playerStatuses[ownerSlot].BotWeapon, new Random());
                    if (!botsFire || items.Count > 0)
                    {
                        SetMoney(playerOrBot, 0);
                        playerOrBot.RemoveWeapons();
                        if (botsFire)
                        {
                            foreach (var item in items)
                                playerOrBot.GiveNamedItem(item);
                        }
                    }

                    // Bot health and armor. Applied after the loadout and mp_free_armor have run, so ours wins.
                    var botHealth = _playerStatuses[ownerSlot].BotHealth;
                    var botArmor = _playerStatuses[ownerSlot].BotArmor;
                    AddTimer(0.45f, () => ApplyBotVitals(playerOrBot, botHealth, botArmor));

                    // Try to increase bot difficulty
                    // playerOrBot.beha
                }
                else
                {
                    // This code block is to patch the issue of extra bots.
                    // Explain:
                    //     Bot B is died while Bot A is still spawning, so progress 
                    //     is not updated in time. This could cause Bot B not being
                    //     kicked. So kick them here.
                    _ownerOfBots.Remove(playerOrBot.Slot);
                    _playerStatuses[ownerSlot].Bots.Remove(playerOrBot.Slot);
                    KickBot(playerOrBot.Slot);

                    if (_playerStatuses[ownerSlot].Bots.Count == 0)
                    {
                        // Practice finished.
                        var owner = Utilities.GetPlayerFromSlot(ownerSlot);

                        if (owner != null && owner.IsValid && !owner.IsBot && !owner.IsHLTV)
                        {
                            FinishRoute(owner);
                        }
                    }
                }
            }
            // else if (_playerCount > 0)
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[Freefrag] Find an unmanaged bot ({playerOrBot.PlayerName}) spawning, kick it.");
            //     KickBot(playerOrBot.Slot);
            // }
        }
        else
        {
            // Console.WriteLine($"[Freefrag] DEBUG: A player {playerOrBot.PlayerName} just spawned.");
            // For players: Set them up if they are practicing.
            if (!_playerStatuses.ContainsKey(playerOrBot.Slot))
                return HookResult.Continue;

            if (_playerStatuses[playerOrBot.Slot].PracticeIndex < 0)
                return HookResult.Continue;

            if (_playerStatuses[playerOrBot.Slot].PendingRoute > -1)
            {
                var pendingRoute = _playerStatuses[playerOrBot.Slot].PendingRoute;
                _playerStatuses[playerOrBot.Slot].PendingRoute = -1;
                if (RouteBlockedByOthers(playerOrBot.Slot, pendingRoute))
                {
                    // Another player took that route between the death and this respawn. Stay on the current route;
                    // the same-index path re-adds the bots the death branch skipped and runs SetupPrefireMode.
                    playerOrBot.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(playerOrBot, "practice.incompatible")}");
                    pendingRoute = _playerStatuses[playerOrBot.Slot].PracticeIndex;
                }
                StartPractice(playerOrBot, pendingRoute);
                return HookResult.Continue;
            }

            // Console.WriteLine($"[Freefrag] DEBUG: Setup player {playerOrBot.PlayerName}.");
            SetupPrefireMode(playerOrBot);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_modeGate.Active)
        {
            return HookResult.Continue;
        }

        var playerOrBot = @event.Userid;
        
        if (playerOrBot == null || !playerOrBot.IsValid || playerOrBot.IsHLTV)
        {
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot) 
        {
            if (_ownerOfBots.ContainsKey(playerOrBot.Slot))
            {
                // For managed bots
                int ownerSlot = _ownerOfBots[playerOrBot.Slot];
                RemoveGlow(ownerSlot, playerOrBot.Slot);
                var targetNo = _playerStatuses[ownerSlot].Progress;
                var practiceIndex = _playerStatuses[ownerSlot].PracticeIndex;

                var owner = Utilities.GetPlayerFromSlot(ownerSlot);

                if (owner == null || !owner.IsValid || owner.IsBot || owner.IsHLTV)
                {
                    Console.WriteLine($"[Freefrag] The owner of bot ({playerOrBot.PlayerName}) is not right. This could be a bug.");
                    return HookResult.Continue;
                }

                if (targetNo >= _practices[practiceIndex].NumBots)         // Bots might be killed after their first time getting spawned, so as to move them to target spots.
                {
                    // Award the player. Only the owner's own kills time the run; a bot that dies to another
                    // player or to the world would otherwise shorten the owner's time, so that run is voided.
                    if (@event.Attacker != null && @event.Attacker.IsValid && @event.Attacker.Slot == ownerSlot)
                    {
                        _playerStatuses[ownerSlot].Run.RecordKill(@event.Headshot, DateTime.UtcNow);
                    }
                    else
                    {
                        _playerStatuses[ownerSlot].Run.MarkDirty();
                    }
                    if (owner.PawnIsAlive && owner.Pawn.Value != null)
                    {
                        owner.GiveNamedItem("item_assaultsuit");
                        RefillAmmo(owner);

                        if (_playerStatuses[ownerSlot].HealingMethod > 1)
                        {
                            var currentHp = owner.Pawn.Value.Health;
                            switch (_playerStatuses[ownerSlot].HealingMethod)
                            {
                                case 2:
                                    currentHp = currentHp + 25;
                                    break;
                                case 3:
                                    currentHp = 100;
                                    break;
                                case 4:
                                    currentHp = currentHp + 100;
                                    break;
                                case 5:
                                    currentHp = currentHp + 500;
                                    break;
                                default:
                                    currentHp = 100;
                                    break;
                            }
                            SetPlayerHealth(owner, currentHp);
                        }
                    }

                    // Print progress
                    string tmpPractice = _translator!.Translate(owner, "map." + _mapName + "." + _practices[_playerStatuses[ownerSlot].PracticeIndex].PracticeName);
                    string tmpProgress = _translator!.Translate(owner, "practice.progress", _playerStatuses[ownerSlot].EnabledTargets.Count, _playerStatuses[ownerSlot].EnabledTargets.Count - targetNo + _playerStatuses[ownerSlot].Bots.Count - 1);
                    string content = $"{tmpPractice}\u2029{tmpProgress}";
                    owner.PrintToCenter(content);
                }

                // Kick unnecessary bots
                if (targetNo >= _playerStatuses[ownerSlot].EnabledTargets.Count)
                {
                    _ownerOfBots.Remove(playerOrBot.Slot);
                    _playerStatuses[ownerSlot].Bots.Remove(playerOrBot.Slot);
                    KickBot(playerOrBot.Slot);

                    if (_playerStatuses[ownerSlot].Bots.Count == 0)
                    {
                        // Practice finished.
                        FinishRoute(owner);
                    }
                }
                else
                {
                    // Fast respawn
                    AddTimer(0.35f, () => {
                        if (playerOrBot.IsValid && !playerOrBot.PawnIsAlive)
                        {
                            playerOrBot.Respawn();
                        }
                    });
                }
            }
            // else if (_playerCount > 0)
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[Freefrag] Find an unmanaged bot ({playerOrBot.PlayerName}) dying, kick it.");
            //     KickBot(playerOrBot.Slot);
            // }
        }
        else
        {
            // For players: If some bots have already been kicked, add them back.
            if (!_playerStatuses.ContainsKey(playerOrBot.Slot))
                return HookResult.Continue;
            
            var status = _playerStatuses[playerOrBot.Slot];
            var practiceIndex = status.PracticeIndex;
            var numBots = status.Bots.Count;

            if (practiceIndex > -1)
            {
                status.DeathsOnRoute++;
                status.Run.RecordDeath();
                if (status.AutoSkipDeaths > 0 && status.DeathsOnRoute >= status.AutoSkipDeaths)
                {
                    var nextRoute = AutoSkip.NextRoute(practiceIndex, _practices.Count, i => RouteBlockedByOthers(playerOrBot.Slot, i));
                    if (nextRoute != null)
                    {
                        status.PendingRoute = nextRoute.Value;
                        status.DeathsOnRoute = 0;
                        var practiceName = _translator!.Translate(playerOrBot, "map." + _mapName + "." + _practices[nextRoute.Value].PracticeName);
                        playerOrBot.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator.Translate(playerOrBot, "practice.autoskip", status.AutoSkipDeaths, practiceName)}");
                        return HookResult.Continue;
                    }
                }
            }
            
            if (practiceIndex > -1 && numBots < _practices[practiceIndex].NumBots)
            {
                _playerStatuses[playerOrBot.Slot].Progress = 0;
                AddBot(playerOrBot, _practices[practiceIndex].NumBots - numBots);
            }
        }
        
        return HookResult.Continue;
    }

    // Menus are described once as MenuSpecs (PrefireMenu.cs) and rendered by whichever backend is active:
    // screen menus drawn by ScreenMenu, or the stock ChatMenu (see UseScreenMenu).

    private void OnRouteSelect(CCSPlayerController player, string label)
    {
        int practiceNo = _playerStatuses[player.Slot].LocalizedPracticeNames[label];
        StartPractice(player, practiceNo);
        CloseAllMenus(player);
    }

    private void OnForceExitPrefireMode(CCSPlayerController player)
    {
        ForceStopPractice(player);
        CloseAllMenus(player);
    }

    private MenuSpec BuildMapMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var map in _availableMaps)
        {
            var mapName = map;
            items.Add(new MenuItem(mapName, p =>
            {
                ChangeMap(p, mapName);
                CloseScreenMenu(p);   // The chat menu closes itself on selection; the screen menu does not.
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "mapmenu.title"), items);
    }

    private MenuSpec BuildDifficultyMenu(CCSPlayerController player)
    {
        // Dynamically draw menu
        var items = new List<MenuItem>();
        _playerStatuses[player.Slot].LocalizedDifficultyNames.Clear();

        for (var i = 0; i < 5; i++)
        {
            var tmpLocalizedDifficultyName = _translator!.Translate(player, $"difficulty.{i}");
            _playerStatuses[player.Slot].LocalizedDifficultyNames.Add(tmpLocalizedDifficultyName, i);
            items.Add(new MenuItem(tmpLocalizedDifficultyName, p => OnDifficultyChosen(p, tmpLocalizedDifficultyName))); // practice name here is split by space instead of underline. TODO: Use localized text.
        }

        return new MenuSpec(_translator!.Translate(player, "difficulty.title"), items);
    }

    private void OnDifficultyChosen(CCSPlayerController player, string label)
    {
        int difficultyNo = _playerStatuses[player.Slot].LocalizedDifficultyNames[label];
        ChangeDifficulty(player, difficultyNo);
        AfterSettingChosen(player);
    }

    private MenuSpec BuildModeMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        _playerStatuses[player.Slot].LocalizedTrainingModeNames.Clear();

        for (var i = 0; i < 2; i++)
        {
            var tmpLocalizedTrainingModeName = _translator!.Translate(player, $"modemenu.{i}");
            _playerStatuses[player.Slot].LocalizedTrainingModeNames.Add(tmpLocalizedTrainingModeName, i);
            items.Add(new MenuItem(tmpLocalizedTrainingModeName, p => OnModeChosen(p, tmpLocalizedTrainingModeName)));
        }

        return new MenuSpec(_translator!.Translate(player, "modemenu.title"), items);
    }

    private void OnModeChosen(CCSPlayerController player, string label)
    {
        var trainingModeNo = _playerStatuses[player.Slot].LocalizedTrainingModeNames[label];
        ChangeTrainingMode(player, trainingModeNo);
        AfterSettingChosen(player);
    }

    private MenuSpec BuildLanguageMenu(CCSPlayerController player)
    {
        // No need for localization here.
        var items = new List<MenuItem>
        {
            new MenuItem("English", p => OnLanguageChosen(p, "English")),
            new MenuItem("Português", p => OnLanguageChosen(p, "Português")),
            new MenuItem("中文", p => OnLanguageChosen(p, "中文")),
        };

        return new MenuSpec("Change language settings", items);
    }

    private void OnLanguageChosen(CCSPlayerController player, string label)
    {
        switch (label)
        {
            case "English":
                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                break;
            case "Português":
                _translator!.UpdatePlayerCulture(player.SteamID, "pt-BR");
                break;
            case "中文":
                _translator!.UpdatePlayerCulture(player.SteamID, "ZH");
                break;
            default:
                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                break;
        }

        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "languagemenu.set")}");
        AfterSettingChosen(player);
    }

    private MenuSpec BuildBotWeaponMenu(CCSPlayerController player)
    {
        // Dynamically draw menu
        var items = new List<MenuItem>();
        var labels = new[]
        {
            _translator!.Translate(player, "weaponmenu.random"),
            "UMP-45", "AK47", "SSG08", "AWP", "Pistol", "Eco", "Force",
        };
        foreach (var label in labels)
        {
            var weaponLabel = label;
            items.Add(new MenuItem(weaponLabel, p => OnBotWeaponChosen(p, weaponLabel)));
        }

        return new MenuSpec(_translator!.Translate(player, "weaponmenu.title"), items);
    }

    private void OnBotWeaponChosen(CCSPlayerController player, string label)
    {
        var botWeaponChoice = label switch
        {
            "UMP-45" => 1,
            "AK47" => 2,
            "SSG08" => 3,
            "AWP" => 4,
            "Pistol" => 5,
            "Eco" => 6,
            "Force" => 7,
            _ => 0
        };

        SetBotWeapon(player, botWeaponChoice);

        AfterSettingChosen(player);
    }

    /// <summary>True when menus are drawn on screen by this plugin; false means the stock chat menus.</summary>
    private bool UseScreenMenu()
    {
        return _screenMenu != null
            && MenuBackend.Choose(_defaultPlayerSettings?.MenuType ?? MenuBackend.Screen) == MenuBackend.Screen;
    }

    private void OpenMenu(CCSPlayerController player, MenuSpec spec)
    {
        if (UseScreenMenu())
        {
            _screenMenu!.Open(player, spec);
            PrintBindsHintUntilBound(player);
            return;
        }

        OpenChatMenu(player, spec);
    }

    // The screen menu is unusable until the player has bound the four navigation commands, so the
    // hint and the bind line repeat on every open until one of those commands has been seen.
    private void PrintBindsHintUntilBound(CCSPlayerController player)
    {
        if (!_playerStatuses.TryGetValue(player.Slot, out var status) || status.MenuBindsUsed)
        {
            return;
        }

        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "menu.binds_hint")}");
        PrintBinds(player);
    }

    private void MarkMenuBindsUsed(CCSPlayerController player)
    {
        if (_playerStatuses.TryGetValue(player.Slot, out var status))
        {
            status.MenuBindsUsed = true;
        }
    }

    private void PrintBinds(CCSPlayerController player)
    {
        var chunks = ChatChunks.Split(_translator!.Translate(player, "menu.binds"));
        for (var i = 0; i < chunks.Count; i++)
        {
            var prefix = i == 0 ? $" {ChatColors.Green}{ChatPrefix} {ChatColors.White} " : " ";
            player.PrintToChat(prefix + chunks[i]);
        }
    }

    private void OpenChatMenu(CCSPlayerController player, MenuSpec spec)
    {
        var menu = new ChatMenu(spec.Title);
        foreach (var item in spec.Items)
        {
            var menuItem = item;
            menu.AddMenuOption(menuItem.Label, (p, _) =>
            {
                if (menuItem.Submenu != null)
                {
                    OpenChatMenu(p, menuItem.Submenu());
                }
                else
                {
                    menuItem.OnChoose(p);
                }
            });
        }
        menu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), (p, _) => CloseCurrentMenu(p));

        player.PrintToChat($"============ {ChatPrefix} ============");
        MenuManager.OpenChatMenu(player, menu);
        if (spec.ChatFooter != null)
        {
            player.PrintToChat(spec.ChatFooter);
        }
        player.PrintToChat("===========================================");
    }

    /// <summary>Closes the screen menu if that backend is active; the chat menu needs nothing here.</summary>
    private bool CloseScreenMenu(CCSPlayerController player)
    {
        if (!UseScreenMenu())
        {
            return false;
        }

        _screenMenu!.Close(player);
        return true;
    }

    /// <summary>Route picked, exit, or close: nothing stays open on either backend.</summary>
    private void CloseAllMenus(CCSPlayerController player)
    {
        if (!CloseScreenMenu(player))
        {
            CloseCurrentMenu(player);
        }
    }

    /// <summary>
    /// A setting was changed from a submenu. Chat: close, as it always did. Screen: go back to a rebuilt
    /// main menu, because its "Current ..." labels were rendered before the change.
    /// </summary>
    private void AfterSettingChosen(CCSPlayerController player)
    {
        if (CloseScreenMenu(player))
        {
            OpenMenu(player, BuildMainMenu(player));
        }
        else
        {
            CloseCurrentMenu(player);
        }
    }

    // Console commands for the screen menu; players bind them to arrow keys, Enter and Backspace
    // (!freefrag binds), because the server cannot set client binds.
    private void RegisterMenuCommands()
    {
        AddCommand("css_freefrag_up", "Screen menu: move up.", (player, _) => { if (player != null) { MarkMenuBindsUsed(player); _screenMenu?.Up(player); } });
        AddCommand("css_freefrag_down", "Screen menu: move down.", (player, _) => { if (player != null) { MarkMenuBindsUsed(player); _screenMenu?.Down(player); } });
        AddCommand("css_freefrag_select", "Screen menu: choose the highlighted entry.", (player, _) => { if (player != null) { MarkMenuBindsUsed(player); _screenMenu?.Select(player); } });
        AddCommand("css_freefrag_back", "Screen menu: go back one level, or close at the main menu.", (player, _) => { if (player != null) { MarkMenuBindsUsed(player); _screenMenu?.Back(player); } });
    }

    private void LoadPractice()
    {
        Console.WriteLine($"[Freefrag] Loading practices for map {_mapName}.");
        var practiceFiles = new List<string>(Directory.EnumerateFiles($"{ModuleDirectory}/maps/{_mapName}"));
        _practices.Clear();
        _practiceNameToId.Clear();
        _pracConflictCount.Clear();
        for (var i = 0; i < practiceFiles.Count; i++)
        {
            var practiceName = practiceFiles[i].Substring(practiceFiles[i].LastIndexOf(Path.DirectorySeparatorChar) + 1).Split(".")[0];
            _practices.Add(new PrefirePractice(ModuleDirectory, _mapName, practiceName));
            _practiceNameToId.Add(practiceName, i);
            _pracConflictCount.Add(i, 0);
            Console.WriteLine($"[Freefrag] {_mapName} {practiceName} Loaded.");
        }
    }
    
    private void ExitPrefireMode(int playerSlot)
    {
        UnsetPrefireMode(playerSlot);

        if (_playerCount == 0)
        {
            RestoreConvars();
        }
    }

    private void FinishRoute(CCSPlayerController owner)
    {
        var status = _playerStatuses[owner.Slot];
        RecordRun(owner);
        if (RepeatSetting.ShouldRestart(ref status.Repeat))
        {
            owner.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(owner, "practice.repeat", status.Repeat == RepeatSetting.Infinite ? (object)"infinite" : status.Repeat)}");
            StartPractice(owner, status.PracticeIndex);
            return;
        }

        owner.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(owner, "practice.finish")}");
        ExitPrefireMode(owner.Slot);
    }

    private void HandleTopCommand(CCSPlayerController player, string routeArg, bool compOnly = false)
    {
        if (_scores == null)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "score.disabled")}");
            return;
        }

        var route = _playerStatuses[player.Slot].PracticeIndex;
        if (!string.IsNullOrEmpty(routeArg))
        {
            // An explicit but invalid route number gets the help text rather than silently showing the current route.
            route = int.TryParse(routeArg, out var choice) && choice > 0 && choice <= _practices.Count ? choice - 1 : -1;
        }
        if (route < 0 || route >= _practices.Count)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "score.top.help", _practices.Count)}");
            return;
        }

        try
        {
            var practiceName = _practices[route].PracticeName;
            var localizedName = _translator!.Translate(player, "map." + _mapName + "." + practiceName);
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator.Translate(player, "score.top.header", localizedName)}");
            var rows = _scores.Top(_mapName, practiceName, 5, compOnly ? (bool?)true : null);
            if (rows.Count == 0)
            {
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator.Translate(player, "score.none")}");
                return;
            }
            for (var i = 0; i < rows.Count; i++)
            {
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{ScoreFormat.TopLine(i + 1, rows[i])}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Freefrag] Failed to read top scores: {e.Message}");
        }
    }

    private void HandleStatsCommand(CCSPlayerController player)
    {
        if (_scores == null)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "score.disabled")}");
            return;
        }

        try
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "score.stats.header", _mapName)}");
            var rows = _scores.PlayerStats(player.SteamID, _mapName);
            if (rows.Count == 0)
            {
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator.Translate(player, "score.none")}");
                return;
            }
            foreach (var row in rows)
            {
                var localizedName = _translator.Translate(player, "map." + _mapName + "." + row.Route);
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{ScoreFormat.StatsLine(localizedName, row.BestMs, row.Runs)}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Freefrag] Failed to read stats: {e.Message}");
        }
    }

    private void RecordRun(CCSPlayerController owner)
    {
        var status = _playerStatuses[owner.Slot];
        var result = status.Run.Finish();
        if (result == null)
        {
            if (status.Run.Dirty)
            {
                owner.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(owner, "score.dirty")}");
            }
            return;
        }
        if (_scores == null) return;
        if (owner.SteamID == 0)
        {
            // Unauthenticated (LAN / insecure) players would all share one leaderboard identity.
            Console.WriteLine($"[Freefrag] Score for {owner.PlayerName} not recorded: no SteamID.");
            return;
        }

        try
        {
            var practice = _practices[status.PracticeIndex];
            int? prevBest = _scores.BestFor(owner.SteamID, _mapName, practice.PracticeName, status.TrainingMode, status.HealingMethod);
            _scores.Insert(new ScoreRow(0, owner.SteamID, owner.PlayerName, _mapName, practice.PracticeName, status.TrainingMode,
                status.HealingMethod, status.BotWeapon, status.HeadshotOnly, result.ElapsedMs, status.EnabledTargets.Count,
                result.Deaths, result.Kills, result.HeadshotKills, DateTime.UtcNow, status.Comp));
            var line = ScoreFormat.FinishLine(result, prevBest);
            owner.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{line}");
            Console.WriteLine($"[Freefrag] Score: {owner.PlayerName} {_mapName}/{practice.PracticeName} {line}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Freefrag] Failed to record score: {e.Message}");
        }
    }

    private void ResetBots(int slot)
    {
        _playerStatuses[slot].Progress = 0;

        List<int> botsToDelete = new List<int>();

        // for (var i = 0; i < _playerStatuses[player].Bots.Count; i++)
        foreach (var botSlot in _playerStatuses[slot].Bots)
        {
            // var bot = _playerStatuses[player].Bots[i];
            var bot = Utilities.GetPlayerFromSlot(botSlot);

            if (bot == null || !bot.IsValid)
            {
                Console.WriteLine($"[Freefrag] Error: Player has an invalid bot. Unmanage it.");
                botsToDelete.Add(botSlot);
                continue;
            }

            if (bot.PawnIsAlive)
            {
                KillBot(bot);
            }
        }

        AddTimer(3f, () => {
            foreach (int bot in botsToDelete)
            {
                // _playerStatuses[slot].Bots.Remove(bot);
                // _ownerOfBots.Remove(bot);
                KickBot(bot);
            }
        });
    }

    private void SetupPrefireMode(CCSPlayerController player, bool withCountdown = false)
    {
        if (!_modeGate.Active)
        {
            return;
        }

        var status = _playerStatuses[player.Slot];
        var practiceNo = status.PracticeIndex;

        // A respawn or a restart replaces any countdown still running.
        CancelCountdown(player.Slot);
        
        GenerateRandomPractice(player.Slot);
        ResetBots(player.Slot);
        
        AddTimer(0.2f, () => MovePlayer(player, false, _practices[practiceNo].Player.Position, _practices[practiceNo].Player.Rotation));
        if (withCountdown && status.Countdown > 0)
        {
            // Hold the player still once they are at the start spot; the route proper begins when they are released.
            AddTimer(0.25f, () => StartCountdown(player, status));
        }
        AddTimer(0.3f, () => EquipPlayer(player));
        AddTimer(0.35f, () => SetMoney(player, 60000));
        if (_playerStatuses[player.Slot].HealingMethod == 1 || _playerStatuses[player.Slot].HealingMethod == 5)
        {
            AddTimer(0.4f, () => SetPlayerHealth(player, 500));
        }
        else
        {
            AddTimer(0.4f, () => SetPlayerHealth(player, 100));         // in case player got injured by bots before teleport
        }
    }

    private void RemoveBots(int slot)
    {
        List<int> botsToKick = new List<int>();

        foreach (int botSlot in _playerStatuses[slot].Bots)
        {
            var bot = Utilities.GetPlayerFromSlot(botSlot);

            if (bot == null || !bot.IsValid)
            {
                Console.WriteLine($"[Freefrag] Trying to kick an invalid bot (slot {botSlot}). Unmanaging it.");
            }

            // KickBot drops the ownership records either way and only sends bot_kick for a valid bot.
            botsToKick.Add(botSlot);
        }

        foreach (int botSlot in botsToKick)
        {
            KickBot(botSlot);
        }

        _playerStatuses[slot].Progress = 0;
    }

    private void AddBot(CCSPlayerController player, int numberOfBots)
    {
        if (!_modeGate.Active)
        {
            return;
        }

        int botTeam;
        if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
        {
            botTeam = BotRequestQueue.TeamT;
        }
        else if (player.TeamNum == (byte)CsTeam.Terrorist)
        {
            botTeam = BotRequestQueue.TeamCT;
        }
        else
        {
            Console.WriteLine($"[Freefrag] Not requesting bots for player slot {player.Slot}: not on T or CT (team {player.TeamNum}).");
            return;
        }

        Console.WriteLine($"[Freefrag] Player slot {player.Slot} requests {numberOfBots} bots (queue length {_botRequests.Count}).");

        var issue = _botRequests.Enqueue(player.Slot, botTeam, numberOfBots);
        if (issue != null)
        {
            IssueBotRequest(issue);
        }
    }

    // Sends the bot_add commands for one request. Requests are issued one player at a time, so every
    // bot that joins while this request is active is assigned to its owner in OnClientPutInServerHandler.
    private void IssueBotRequest(BotRequestQueue.Issue issue)
    {
        var joinTeam = issue.BotTeam == BotRequestQueue.TeamT ? "T" : "CT";
        var addCommand = issue.BotTeam == BotRequestQueue.TeamT ? "bot_add_t" : "bot_add_ct";
        Console.WriteLine($"[Freefrag] Creating {issue.Count} {joinTeam} bots for player slot {issue.OwnerSlot}.");

        for (var i = 0; i < issue.Count; i++)
        {
            AddTimer(i * BotSpawnInterval, () => {
                Server.ExecuteCommand($"bot_join_team {joinTeam}");
                Server.ExecuteCommand(addCommand);
            });
        }

        // If the bots never show up (server full, bot_quota, ...) the request must not block everyone else.
        AddTimer(issue.Count * BotSpawnInterval + BotSpawnTimeout, () => {
            var next = _botRequests.ExpireIfStalled(issue.OwnerSlot, issue.IssueId, out var dropped);
            if (dropped > 0)
            {
                Console.WriteLine($"[Freefrag] Bot request of player slot {issue.OwnerSlot} timed out with {dropped} bots missing; dropping it.");
            }
            if (next != null)
            {
                IssueBotRequest(next);
            }
        });
    }

    private void DropBotRequest(int playerSlot)
    {
        var next = _botRequests.Remove(playerSlot);
        if (next != null)
        {
            IssueBotRequest(next);
        }
    }

    private void MovePlayer(CCSPlayerController? player, bool crouch, Vector pos, QAngle ang)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value == null)
        {
            return;
        }

        // Console.WriteLine($"[Freefrag] DEBUG: {player.PlayerName} moved to spawn point.");

        // Only bot can crouch
        if (player.IsBot)
        {
            var movementService = new CCSPlayer_MovementServices(player.PlayerPawn.Value.MovementServices!.Handle);
            if (crouch)
            {
                AddTimer(0.05f, () => movementService.DuckAmount = 1);
                AddTimer(0.1f, () => player.PlayerPawn.Value.Bot!.IsCrouching = true);
            }
            else
            {
                AddTimer(0.05f, () => movementService.DuckAmount = 0);
                AddTimer(0.1f, () => player.PlayerPawn.Value.Bot!.IsCrouching = false);
            }
        }
        
        player.PlayerPawn.Value.Teleport(pos, ang, Vector.Zero);
    }

    private void FreezeBot(CCSPlayerController? bot)
    {
        // Console.WriteLine($"[Freefrag] DEBUG: Trying to freeze a bot.");
        if (bot != null &&
            bot is { IsValid: true, IsBot: true, IsHLTV: false, PawnIsAlive: true } 
            && bot.PlayerPawn.Value != null
        )
        {
            // Console.WriteLine($"[Freefrag] DEBUG: Bot {bot.PlayerName} freezed.");

            // bot.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
            bot.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_VPHYSICS;
            Schema.SetSchemaValue(bot.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 5);
            Utilities.SetStateChanged(bot.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
        }
    }

    private static void SetPlayerFrozen(CCSPlayerController? player, bool frozen)
    {
        if (player == null || !player.IsValid || player.IsHLTV || !player.PawnIsAlive || player.PlayerPawn.Value == null)
            return;

        // Same mechanism as FreezeBot; unfreezing restores MOVETYPE_WALK (2).
        var pawn = player.PlayerPawn.Value;
        pawn.MoveType = frozen ? MoveType_t.MOVETYPE_VPHYSICS : MoveType_t.MOVETYPE_WALK;
        Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", frozen ? 5 : 2);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    // True while this exact player (not a later occupant of the same slot) is still connected and registered.
    private bool IsRegisteredPlayer(CCSPlayerController player, PlayerStatus status) =>
        player.IsValid && _playerStatuses.TryGetValue(player.Slot, out var current) && ReferenceEquals(current, status);

    private void StartCountdown(CCSPlayerController player, PlayerStatus status)
    {
        if (!IsRegisteredPlayer(player, status) || status.PracticeIndex < 0 || !player.PawnIsAlive || status.Countdown <= 0)
            return;

        CancelCountdown(player.Slot);
        status.CountdownRemaining = status.Countdown;
        SetPlayerFrozen(player, true);
        player.PrintToCenter(_translator!.Translate(player, "countdown.tick", status.CountdownRemaining));
        status.CountdownTimer = AddTimer(1f, () => CountdownTick(player, status), TimerFlags.REPEAT);
    }

    private void CountdownTick(CCSPlayerController player, PlayerStatus status)
    {
        if (!IsRegisteredPlayer(player, status))
        {
            // The player left (or the slot was reused): never touch whoever is in the slot now.
            status.CountdownTimer?.Kill();
            status.CountdownTimer = null;
            status.CountdownRemaining = 0;
            return;
        }

        if (status.PracticeIndex < 0 || !player.PawnIsAlive)
        {
            // Practice stopped or the player died; a respawn brings a fresh, unfrozen pawn.
            CancelCountdown(player.Slot);
            return;
        }

        status.CountdownRemaining--;
        if (status.CountdownRemaining > 0)
        {
            player.PrintToCenter(_translator!.Translate(player, "countdown.tick", status.CountdownRemaining));
            return;
        }

        status.CountdownTimer?.Kill();
        status.CountdownTimer = null;
        SetPlayerFrozen(player, false);
        player.PrintToCenter(_translator!.Translate(player, "countdown.go"));
    }

    // Stops a running countdown and releases the player. Safe to call when none is running.
    private void CancelCountdown(int slot)
    {
        if (!_playerStatuses.TryGetValue(slot, out var status) || status.CountdownTimer == null)
            return;

        status.CountdownTimer.Kill();
        status.CountdownTimer = null;
        status.CountdownRemaining = 0;
        SetPlayerFrozen(Utilities.GetPlayerFromSlot(slot), false);
    }

    private static void ApplyBotVitals(CCSPlayerController? bot, int health, int armor)
    {
        if (bot == null || !bot.IsValid || !bot.IsBot || bot.IsHLTV || !bot.PawnIsAlive || bot.PlayerPawn.Value == null)
            return;

        var pawn = bot.PlayerPawn.Value;
        pawn.MaxHealth = health;
        pawn.Health = health;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

        pawn.ArmorValue = armor;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");

        // Armor 0 means no kevlar and no helmet; mp_free_armor 2 and the force-buy preset would otherwise leave the helmet on.
        if (armor == 0 && pawn.ItemServices != null)
        {
            var itemServices = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
            itemServices.HasHelmet = false;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
        }
    }

    private void EquipPlayer(CCSPlayerController player)
    {
        if (player == null || !player.PawnIsAlive || player.PlayerPawn.Value == null)
            return;
        
        if (_defaultPlayerSettings!.EquipPlayer == 1)
        {
            player.RemoveWeapons();

            var gun = _playerStatuses.TryGetValue(player.Slot, out var status) ? status.PlayerGun : "ak";
            foreach (var item in PlayerLoadout.ItemsFor(gun))
                player.GiveNamedItem(item);

            // Switch to main weapon
            player.ExecuteClientCommand("slot1");
        }
        else
        {
            player.GiveNamedItem("weapon_knife");
            player.GiveNamedItem("item_assaultsuit");
        }
    }

    private static void SetPlayerHealth(CCSPlayerController player, int hp)
    {
        if (player == null || !player.PawnIsAlive || player.Pawn.Value == null || hp < 0)
            return;
        
        // Console.WriteLine($"[Freefrag] DEBUG: Setup player {player.PlayerName} with health.");

        if (hp > 100)
            player.Pawn.Value.MaxHealth = hp;
        player.Pawn.Value.Health = hp;
        Utilities.SetStateChanged(player.Pawn.Value, "CBaseEntity", "m_iHealth");
    }

    private void GenerateRandomPractice(int playerSlot)
    {
        _playerStatuses[playerSlot].EnabledTargets.Clear();
        var practiceNo = _playerStatuses[playerSlot].PracticeIndex;
        
        for (var i = 0; i < _practices[practiceNo].Targets.Count; i++)
            _playerStatuses[playerSlot].EnabledTargets.Add(i);

        if (_playerStatuses[playerSlot].TrainingMode == 0)
        {
            // 0: Use part of the targets.
            var numTargets = (int)(_practices[practiceNo].SpawnRatio * _practices[practiceNo].Targets.Count);
            var rnd = new Random(DateTime.Now.Millisecond);

            var numToRemove = _practices[practiceNo].Targets.Count - numTargets;
            for (var i = 0; i < numToRemove; i++)
                _playerStatuses[playerSlot].EnabledTargets.RemoveAt(rnd.Next(_playerStatuses[playerSlot].EnabledTargets.Count));
        }
        // 1: Use all of the targets.
    }

    private void CreateGuidingLine(int playerSlot)
    {
        var practiceNo = _playerStatuses[playerSlot].PracticeIndex;

        if (practiceNo < 0 || practiceNo >= _practices.Count)
        {
            Console.WriteLine($"[Freefrag] Error when creating guiding line. Current practice_no illegal. (practice_no = {practiceNo})");
            return;
        }

        if (_practices[practiceNo].GuidingLine.Count < 2)
            return;

        // Draw beams
        for (int i = 0; i < _practices[practiceNo].GuidingLine.Count - 1; i++)
        {
            int beamIndex = DrawBeam(_practices[practiceNo].GuidingLine[i], _practices[practiceNo].GuidingLine[i + 1]);
            
            if (beamIndex == -1)
                return;

            _playerStatuses[playerSlot].Beams.Add(beamIndex);
        }
    }

    private void DeleteGuidingLine(int playerSlot)
    {
        for (var i = 0; i < _playerStatuses[playerSlot].Beams.Count; i++)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>(_playerStatuses[playerSlot].Beams[i]);

            if (beam == null || !beam.IsValid)
            {
                Console.WriteLine($"[Freefrag] Error when deleting guiding line. Failed to get beam entity(index = {_playerStatuses[playerSlot].Beams[i]})");
                continue;
            }

            beam.Remove();
        }

        _playerStatuses[playerSlot].Beams.Clear();
    }

    private static int DrawBeam(Vector startPos, Vector endPos)
    {
        return DrawBeam(startPos, endPos, System.Drawing.Color.Blue);
    }

    private static int DrawBeam(Vector startPos, Vector endPos, System.Drawing.Color color)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam == null)
        {
            // Failed to create beam
            Console.WriteLine($"[Freefrag] Failed to create beam. Start position: {startPos}, end position: {endPos}");
            return -1;
        }

        beam.Render = color;
        beam.Width = 2.0f;

        beam.Teleport(startPos, QAngle.Zero, Vector.Zero);
        beam.EndPos.Add(endPos);
        beam.DispatchSpawn();

        // Console.WriteLine($"[Freefrag] Created a beam. Start position: {startPos}, end position: {endPos}, entity index: {beam.Index}");
        return (int)beam.Index;
    }

    // ---------------------------------------------------------------------
    // Guiding-line recorder: walk a route in game, then save it to the profile.
    //   !freefrag record         toggle recording of your position
    //   !freefrag record save    write the recorded points into the current route and redraw the line
    //   !freefrag record clear   discard recorded points
    // ---------------------------------------------------------------------
    private const float RecordSampleInterval = 0.2f;    // seconds
    private const float RecordMinDistance = 50f;        // units between joints
    private const float RecordHeightOffset = 8f;        // lift the line slightly off the floor

    /// <summary>While recording the player cannot take damage, so the route can be walked without the bots interrupting.</summary>
    private static void SetInvulnerable(CCSPlayerController? player, bool invulnerable)
    {
        var pawn = player?.PlayerPawn.Value;
        if (player == null || !player.IsValid || pawn == null || !pawn.IsValid)
            return;
        pawn.TakesDamage = !invulnerable;
    }

    private void HandleRecordCommand(CCSPlayerController player, string subCommand)
    {
        var slot = player.Slot;
        if (!_playerStatuses.ContainsKey(slot) || _playerStatuses[slot].PracticeIndex < 0)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.not_practicing")}");
            return;
        }

        switch (subCommand)
        {
            case "":
                if (_playerStatuses[slot].IsRecording)
                {
                    StopRecording(slot);
                    player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.stop", _playerStatuses[slot].RecordedPoints.Count)}");
                }
                else
                {
                    StartRecording(player);
                    player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.start")}");
                }
                return;
            case "save":
                StopRecording(slot);
                SaveRecording(player);
                return;
            case "clear":
                StopRecording(slot);
                ClearRecording(slot);
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.cleared")}");
                return;
            default:
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.help")}");
                return;
        }
    }

    private static (float x, float y, float z)? GetPlayerFeet(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (!player.IsValid || !player.PawnIsAlive || pawn == null || pawn.AbsOrigin == null)
            return null;
        return (pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + RecordHeightOffset);
    }

    private void StartRecording(CCSPlayerController player)
    {
        if (!_modeGate.Active)
        {
            return;
        }

        var slot = player.Slot;
        var status = _playerStatuses[slot];
        if (status.IsRecording)
            return;

        status.IsRecording = true;
        SetInvulnerable(player, true);
        var feet = GetPlayerFeet(player);
        if (feet != null && (status.RecordedPoints.Count == 0 || PrefirePractice.FarEnough(status.RecordedPoints[^1], feet.Value, RecordMinDistance)))
            AddRecordedPoint(slot, feet.Value);

        status.RecordingTimer = AddTimer(RecordSampleInterval, () => SampleRecording(slot), TimerFlags.REPEAT);
    }

    private void SampleRecording(int slot)
    {
        if (!_playerStatuses.TryGetValue(slot, out var status) || !status.IsRecording)
            return;

        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid)
        {
            StopRecording(slot);
            return;
        }

        SetInvulnerable(player, true);      // survives a respawn mid-recording

        var feet = GetPlayerFeet(player);
        if (feet == null)
            return;

        if (status.RecordedPoints.Count == 0 || PrefirePractice.FarEnough(status.RecordedPoints[^1], feet.Value, RecordMinDistance))
            AddRecordedPoint(slot, feet.Value);
    }

    private void AddRecordedPoint(int slot, (float x, float y, float z) point)
    {
        var status = _playerStatuses[slot];
        if (status.RecordedPoints.Count > 0)
        {
            var prev = status.RecordedPoints[^1];
            var beamIndex = DrawBeam(new Vector(prev.x, prev.y, prev.z), new Vector(point.x, point.y, point.z), System.Drawing.Color.Lime);
            if (beamIndex != -1)
                status.RecordingBeams.Add(beamIndex);
        }
        status.RecordedPoints.Add(point);
    }

    private void StopRecording(int slot)
    {
        if (!_playerStatuses.TryGetValue(slot, out var status))
            return;
        status.IsRecording = false;
        status.RecordingTimer?.Kill();
        status.RecordingTimer = null;
        SetInvulnerable(Utilities.GetPlayerFromSlot(slot), false);
    }

    private void ClearRecording(int slot)
    {
        if (!_playerStatuses.TryGetValue(slot, out var status))
            return;
        status.RecordedPoints.Clear();
        foreach (var index in status.RecordingBeams)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>(index);
            if (beam != null && beam.IsValid)
                beam.Remove();
        }
        status.RecordingBeams.Clear();
    }

    private void SaveRecording(CCSPlayerController player)
    {
        var slot = player.Slot;
        var status = _playerStatuses[slot];
        var practiceNo = status.PracticeIndex;

        if (practiceNo < 0 || practiceNo >= _practices.Count)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.not_practicing")}");
            return;
        }

        if (status.RecordedPoints.Count < 2)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.too_few")}");
            return;
        }

        var practice = _practices[practiceNo];
        var path = $"{ModuleDirectory}/maps/{_mapName}/{practice.PracticeName}.txt";
        try
        {
            var lines = PrefirePractice.RewriteProfileWithGuidingLine(File.ReadAllLines(path), status.RecordedPoints);
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Freefrag] Failed to save guiding line to {path}: {ex.Message}");
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.Red}{_translator!.Translate(player, "record.save_failed")}");
            return;
        }

        practice.ReplaceGuidingLine(status.RecordedPoints);
        var count = status.RecordedPoints.Count;
        ClearRecording(slot);
        DeleteGuidingLine(slot);
        CreateGuidingLine(slot);

        Console.WriteLine($"[Freefrag] Saved {count}-point guiding line for {_mapName}/{practice.PracticeName} to {path}");
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "record.saved", count, practice.PracticeName)}");
    }

    /// <summary>True if another player's active route makes this one unavailable. The asking player's own route never blocks them.</summary>
    private bool RouteBlockedByOthers(int slot, int practiceIndex)
    {
        var own = _playerStatuses.TryGetValue(slot, out var status) ? status.PracticeIndex : -1;
        IReadOnlyList<string> ownIncompatible = own > -1 && own < _practices.Count ? _practices[own].IncompatiblePractices : Array.Empty<string>();
        return PrefirePractice.BlockedByOthers(_pracConflictCount[practiceIndex], own, practiceIndex, ownIncompatible, _practices[practiceIndex].PracticeName);
    }

    /// <summary>
    /// Path of a bundled behavior tree relative to game/csgo, derived from where the plugin actually lives
    /// (plugins/Freefrag on kus/cs2-modded-server) instead of a hardcoded path.
    /// </summary>
    private string BehaviorTreePath(string file)
    {
        var dir = ModuleDirectory.Replace('\\', '/');
        var idx = dir.IndexOf("/addons/", StringComparison.OrdinalIgnoreCase);
        var rel = idx >= 0 ? dir[(idx + 1)..] : "addons/counterstrikesharp/plugins/Freefrag";
        return $"{rel}/resources/bt/{file}".Replace('/', '\\');
    }

    private void SaveConvars()
    {
        // Originals must only ever be captured inside Prefire; anywhere else they would be another mode's settings.
        if (!_modeGate.Active)
        {
            return;
        }

        string[] boolConvarNames = [
            "tv_enable",
            "bot_allow_grenades",
            "bot_allow_shotguns",
            "mp_autoteambalance",
            "sv_alltalk",
            "sv_full_alltalk",
            "bot_allow_pistols",
            "bot_allow_rifles",
            "bot_allow_snipers",
            "sv_auto_adjust_bot_difficulty",
        ];

        string[] intConvarNames = [
            "mp_buy_anywhere",
            "mp_warmup_pausetimer",
            "mp_free_armor",
            "mp_limitteams",
            // "sv_infinite_ammo",
            "mp_maxmoney",
            "mp_startmoney",
            "bot_difficulty",
            "custom_bot_difficulty",
            "mp_death_drop_gun",
            "mp_death_drop_grenade",
            "bot_quota",
        ];

        string[] floatConvarNames = [
            "mp_respawn_immunitytime",
            "mp_buytime",
            "bot_max_vision_distance_override",
        ];

        string[] stringConvarNames = [
            "bot_quota_mode",
            "mp_ct_default_melee",
            "mp_t_default_melee",
            "mp_bot_ai_bt",
        ];

        try
        {
            // // sv_cheats
            // var sv_cheats = ConVar.Find("sv_cheats");
            // _serverStatus.sv_cheats = sv_cheats!.GetPrimitiveValue<bool>();

            // Bool convars
            foreach (var convarName in boolConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<bool>();
                    _serverStatus.BoolConvars.Add(convarName, value);
                    // Console.WriteLine($"[Freefrag] {convarName}: {value}");
                }
            }

            // Int convars
            foreach (var convarName in intConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<int>();
                    _serverStatus.IntConvars.Add(convarName, value);
                    // Console.WriteLine($"[Freefrag] {convarName}: {value}");
                }
            }

            // Float convars
            foreach (var convarName in floatConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<float>();
                    _serverStatus.FloatConvars.Add(convarName, value);
                    // Console.WriteLine($"[Freefrag] {convarName}: {value}");
                }
            }

            // String convars
            foreach (var convarName in stringConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.StringValue;
                    _serverStatus.StringConvars.Add(convarName, value);
                    // Console.WriteLine($"[Freefrag] DEBUG {convarName}: {value}");
                }
            }
        }
        catch (System.Exception)
        {
            Console.WriteLine("[Freefrag] Error reading convars.");
            throw;
        }

        // Read Warmup status
        try
        {
            if (_serverGameRules == null)
            {
                _serverGameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            }
            _serverStatus.WarmupStatus = _serverGameRules.WarmupPeriod;
            // Console.WriteLine($"[Freefrag] Warmup Status: {_serverGameRules.WarmupPeriod}");
        }
        catch (System.Exception)
        {
            Console.WriteLine($"[Freefrag] Can't read server's warmup status, will use the default value {_serverStatus.WarmupStatus}.");
        }

        Console.WriteLine("[Freefrag] Values of convars saved.");
    }

    private void RestoreConvars()
    {
        // Bool convars
        foreach (var convar in _serverStatus.BoolConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // tmpConvar!.SetValue(convar.Value);
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
        }
        _serverStatus.BoolConvars.Clear();

        // Int convars
        foreach (var convar in _serverStatus.IntConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // Somehow the following 2 methods don't work, just make up a command to implement this.
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
            // tmpConvar!.GetPrimitiveValue<int>() = convar.Value;
            // tmpConvar!.SetValue(convar.Value);
        }
        _serverStatus.IntConvars.Clear();

        // Float convars
        foreach (var convar in _serverStatus.FloatConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // Somehow the following 2 methods don't work, just make up a command to implement this.
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
            // tmpConvar!.GetPrimitiveValue<float>() = convar.Value;
            // tmpConvar!.SetValue(convar.Value);
        }
        _serverStatus.FloatConvars.Clear();

        // String convars
        foreach (var convar in _serverStatus.StringConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // tmpConvar!.StringValue = convar.Value;
            Server.ExecuteCommand(convar.Key + " \"" + convar.Value + "\"");
        }
        _serverStatus.StringConvars.Clear();

        // Restore sv_cheats
        // var sv_cheats = ConVar.Find("sv_cheats");
        // sv_cheats!.SetValue(_serverStatus.sv_cheats);
        // Server.ExecuteCommand("sv_cheats " + _serverStatus.sv_cheats.ToString());

        // Restore warmup status
        if (!_serverStatus.WarmupStatus)
        {
            Server.ExecuteCommand("mp_warmup_end");
        }

        Console.WriteLine("[Freefrag] Values of convars restored.");
    }

    private void SetupConvars()
    {
        // Same rule as SaveConvars: the practice tweaks must never be applied outside Prefire.
        if (!_modeGate.Active)
        {
            return;
        }

        Server.ExecuteCommand("tv_enable 0");
        // Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("bot_allow_grenades 0");
        Server.ExecuteCommand("bot_allow_shotguns 0");
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("sv_alltalk 1");
        Server.ExecuteCommand("sv_full_alltalk 1");
        Server.ExecuteCommand("bot_allow_pistols 1");
        Server.ExecuteCommand("bot_allow_rifles 1");
        Server.ExecuteCommand("bot_allow_snipers 1");
        Server.ExecuteCommand("sv_auto_adjust_bot_difficulty 0");

        Server.ExecuteCommand("mp_buy_anywhere 1");
        Server.ExecuteCommand("mp_warmup_pausetimer 1");
        Server.ExecuteCommand("mp_free_armor 2");
        Server.ExecuteCommand("mp_limitteams 0");
        // Server.ExecuteCommand("sv_infinite_ammo 1");
        Server.ExecuteCommand("mp_maxmoney 60000");
        Server.ExecuteCommand("mp_startmoney 60000");
        Server.ExecuteCommand("bot_difficulty 5");
        Server.ExecuteCommand("custom_bot_difficulty 5");
        Server.ExecuteCommand("mp_death_drop_gun 0");
        Server.ExecuteCommand("mp_death_drop_grenade 0");
        Server.ExecuteCommand("bot_quota 0");

        Server.ExecuteCommand("mp_respawn_immunitytime -1");
        Server.ExecuteCommand("mp_buytime 9999");
        Server.ExecuteCommand("bot_max_vision_distance_override 99999");

        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand("mp_ct_default_melee \"\"");
        Server.ExecuteCommand("mp_t_default_melee \"\"");
        
        Server.ExecuteCommand("mp_warmup_start");
        // Server.ExecuteCommand("bot_kick all");

        // Set behavior tree for bots (many thanks to 5EPlay)
        if (_defaultPlayerSettings!.BotAimLock == 2)
        {
            Server.ExecuteCommand($"mp_bot_ai_bt \"{BehaviorTreePath("hard_mode.kv3")}\"");
            Server.ExecuteCommand("mp_bot_ai_bt_clear_cache");
        }

        // Kick unmanaged bots
        AddTimer(2f, () => {
            var players = Utilities.GetPlayers();
            List<CCSPlayerController> botsToKick = new List<CCSPlayerController>();
            foreach(var player in players)
            {
                if (player == null || !player.IsValid || player.IsHLTV || !player.IsBot)
                {
                    continue;
                }

                if (_ownerOfBots.ContainsKey(player.Slot))
                {
                    continue;
                }

                botsToKick.Add(player);
            }
            foreach (var bot in botsToKick)
            {
                KickBot(bot.Slot);
            }
            botsToKick.Clear();
        });

        // Server.ExecuteCommand("bot_autodifficulty_threshold_high 5");
        // Server.ExecuteCommand("bot_autodifficulty_threshold_low 5");
        // Server.ExecuteCommand("sv_auto_adjust_bot_difficulty 0");
        // Server.ExecuteCommand("weapon_auto_cleanup_time 1");       
        // Server.ExecuteCommand("mp_roundtime 60");
        // Server.ExecuteCommand("mp_roundtime_defuse 60");
        // Server.ExecuteCommand("mp_freezetime 0");
        // Server.ExecuteCommand("mp_team_intro_time 0");
        // Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        // Server.ExecuteCommand("mp_respawn_on_death_ct 1");
        // Server.ExecuteCommand("mp_respawn_on_death_t 1");

        Console.WriteLine("[Freefrag] Values of convars set.");
    }

    private void RefillAmmo(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn == null || player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null)
        {
            return;
        }

        var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;
        foreach (var weapon in weapons)
        {
            if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.DesignerName.Length != 0 && !weapon.Value.DesignerName.Contains("knife") && !weapon.Value.DesignerName.Contains("bayonet"))
            {
                int magAmmo = 999;
                int reservedAmmo = 999;
                switch (weapon.Value.DesignerName)
                {
                    case "weapon_ak47":
                    case "weapon_m4a1":         // M4A4
                        magAmmo = 31;
                        reservedAmmo = 90;
                        break;
                    case "weapon_m4a1_":         // M4A1_silencer
                        magAmmo = 21;
                        reservedAmmo = 80;
                        break;
                    case "weapon_deagle":
                        magAmmo = 8;
                        reservedAmmo = 35;
                        break;
                    case "weapon_flashbang":
                    case "weapon_smokegrenade":
                    case "weapon_decoy":
                    case "weapon_molotov":
                    case "weapon_incgrenade":
                        continue;
                    default:
                        magAmmo = 999;
                        reservedAmmo = 999;
                        break;
                }

                weapon.Value.Clip1 = magAmmo;
                Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_iClip1");
                weapon.Value.ReserveAmmo[0] = reservedAmmo;
                Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_pReserveAmmo");
            }
        }
    }

    private void KillBot(CCSPlayerController bot)
    {
        if (!bot.IsValid || !bot.IsBot || bot.IsHLTV || !bot.PawnIsAlive)
        {
            return;
        }

        bot.CommitSuicide(false, false);
    }

    private void StartPractice(CCSPlayerController player, int practiceIndex)
    {
        if (!_modeGate.Active)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "mode.dormant")}");
            return;
        }

        if (_playerCount == 0)
        {
            SaveConvars();
            SetupConvars();
            AddTimer(0.5f, () => BreakBreakables());
        }

        var previousPracticeIndex = _playerStatuses[player.Slot].PracticeIndex;

        if (previousPracticeIndex != practiceIndex)
        {
            // Check if selected practice route is compatible with other players' on-going routes.
            if (RouteBlockedByOthers(player.Slot, practiceIndex))
            {
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "practice.incompatible")}");
                return;
            }

            // A new run starts only once the route is actually taken; a blocked attempt keeps the current run intact.
            _playerStatuses[player.Slot].Run.Begin();

            // Update practice status
            if (previousPracticeIndex > -1)
            {
                UnsetPrefireMode(player.Slot);
                DeleteGuidingLine(player.Slot);
            }
            
            _playerCount++;
            _playerStatuses[player.Slot].PracticeIndex = practiceIndex;
            _playerStatuses[player.Slot].DeathsOnRoute = 0;
            AddTimer(1f, () => CreateGuidingLine(player.Slot));

            // Disable incompatible practices.
            for (var i = 0; i < _practices[practiceIndex].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.ContainsKey(_practices[practiceIndex].IncompatiblePractices[i]))
                {
                    var disabledPracticeNo = _practiceNameToId[_practices[practiceIndex].IncompatiblePractices[i]];
                    _pracConflictCount[disabledPracticeNo]++;
                }
            }
            _pracConflictCount[practiceIndex]++;

            // Setup practice
            AddBot(player, _practices[practiceIndex].NumBots);
        }
        else
        {
            // Re-selecting the current route (or a repeat restart) is a fresh run.
            _playerStatuses[player.Slot].Run.Begin();

            // If some bots have already been kicked, add them back.
            var numRemainingBots = _playerStatuses[player.Slot].Bots.Count;
            
            if (numRemainingBots < _practices[practiceIndex].NumBots)
            {
                _playerStatuses[player.Slot].Progress = 0;
                AddBot(player, _practices[practiceIndex].NumBots - numRemainingBots);
            }
        }

        // Practice begin
        AddTimer(0.5f, () => SetupPrefireMode(player, withCountdown: true));
        var localizedPracticeName = _translator!.Translate(player, "map." + _mapName + "." + _practices[practiceIndex].PracticeName);
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator.Translate(player, "practice.choose", localizedPracticeName)}");
        if (_playerStatuses[player.Slot].Countdown == 0)
        {
            // With a countdown running, its own center text replaces this one.
            player.PrintToCenter(_translator.Translate(player, "practice.begin"));
        }
    }

    private void ChangeMap(CCSPlayerController player, string mapName)
    {
        if (!_modeGate.Active)
        {
            return;
        }

        // Check if the map has practice routes
        if (!_availableMaps.Contains(mapName))
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "mapmenu.not_available")}");
            return;
        }

        // Only allow change map when nobody is practicing.
        if (_playerCount == 0)
        {
            Server.ExecuteCommand($"changelevel {mapName}");
        }
        else
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "mapmenu.busy")}");
        }
    }

    // The help text outgrew one chat message; CS2 silently truncates, so send it in chunks.
    private void PrintHelp(CCSPlayerController player)
    {
        var chunks = ChatChunks.Split(_translator!.Translate(player, "mainmenu.help", _practices.Count));
        for (var i = 0; i < chunks.Count; i++)
        {
            var prefix = i == 0 ? $" {ChatColors.Green}{ChatPrefix} {ChatColors.White} " : " ";
            player.PrintToChat(prefix + chunks[i]);
        }
        player.PrintToChat(" " + _translator.Translate(player, "gamemodes.help"));
    }

    private void ChangeDifficulty(CCSPlayerController player, int difficultyNo)
    {
        if (_playerStatuses[player.Slot].HealingMethod != difficultyNo && _playerStatuses[player.Slot].PracticeIndex > -1)
        {
            _playerStatuses[player.Slot].Run.MarkDirty();
        }
        _playerStatuses[player.Slot].HealingMethod = difficultyNo;
        var currentDifficulty = _translator!.Translate(player, $"difficulty.{difficultyNo}");
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "difficulty.set", currentDifficulty)}");
    }

    private void ChangeTrainingMode(CCSPlayerController player, int trainingMode)
    {
        if (!_modeGate.Active)
        {
            return;
        }

        if (_playerStatuses[player.Slot].TrainingMode != trainingMode && _playerStatuses[player.Slot].PracticeIndex > -1)
        {
            _playerStatuses[player.Slot].Run.MarkDirty();
        }
        _playerStatuses[player.Slot].TrainingMode = trainingMode;
        var currentTrainingMode = _translator!.Translate(player, $"modemenu.{trainingMode}");
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "modemenu.set", currentTrainingMode)}");
    }

    private void ForceStopPractice(CCSPlayerController player)
    {
        ExitPrefireMode(player.Slot);
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "practice.exit")}");
    }

    private void UnregisterCommand()
    {
        if (_command == null)
        {
            return;
        }

        CommandManager.RemoveCommand(_command);
        _command = null;
    }

    private void RegisterCommand()
    {
        if (_command != null)
        {
            UnregisterCommand();
        }

        _command = new CommandDefinition("css_freefrag", "Command to bring up the main menu of Klitch's Freefrag.", (player, commandInfo) => {
            // This is a client only command
            if (player == null)
            {
                return;
            }

            // Dormant outside Prefire: only the mode switcher and the read-only commands stay available.
            if (!_modeGate.Active
                && !ModeGate.AllowedWhileDormant(commandInfo.ArgCount > 1 ? commandInfo.ArgByIndex(1) : ""))
            {
                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White}{_translator!.Translate(player, "mode.dormant")}");
                return;
            }

            // Command shortcuts
            if (commandInfo.ArgCount > 1)
            {
                switch (commandInfo.ArgByIndex(1))
                {
                    case "prac":
                        int choice = 0;
                        if (int.TryParse(commandInfo.ArgByIndex(2), out choice) && choice > 0 && choice <= _practices.Count)
                        {
                            StartPractice(player, choice - 1);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "practice.help", _practices.Count)}");
                        return;
                    case "map":
                        string mapName = commandInfo.ArgByIndex(2);
                        ChangeMap(player, mapName);
                        return;
                    case "df":
                        int difficulty = 0;
                        if (int.TryParse(commandInfo.ArgByIndex(2), out difficulty) && difficulty > 0 && difficulty <= 6)
                        {
                            ChangeDifficulty(player, 6 - difficulty);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "difficulty.help")}");
                        return;
                    case "mode":
                        string trainingMode = commandInfo.ArgByIndex(2);
                        switch (trainingMode)
                        {
                            case "full":
                                ChangeTrainingMode(player, 1);
                                return;
                            case "rand":
                                ChangeTrainingMode(player, 0);
                                return;
                            default:
                                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "modemenu.help")}");
                                return;
                        }
                    case "bw":
                        var botWeapon = BotLoadout.ParseBotWeapon(commandInfo.ArgByIndex(2));
                        if (botWeapon != null)
                        {
                            SetBotWeapon(player, botWeapon.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "weaponmenu.help")}");
                        return;
                    case "gun":
                        var gun = PlayerLoadout.ParseGun(commandInfo.ArgByIndex(2));
                        if (gun != null)
                        {
                            SetPlayerGun(player, gun);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gun.help")}");
                        return;
                    case "jitter":
                        var jitter = SpawnJitter.Parse(commandInfo.ArgByIndex(2));
                        if (jitter != null)
                        {
                            SetJitter(player, jitter.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "jitter.help")}");
                        return;
                    case "repeat":
                        var repeat = RepeatSetting.Parse(commandInfo.ArgByIndex(2));
                        if (repeat != null)
                        {
                            SetRepeat(player, repeat.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "repeat.help")}");
                        return;
                    case "autoskip":
                        var autoSkip = AutoSkip.Parse(commandInfo.ArgByIndex(2));
                        if (autoSkip != null)
                        {
                            SetAutoSkip(player, autoSkip.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "autoskip.help")}");
                        return;
                    case "hs":
                        var hsSetting = ToggleSetting.Parse(commandInfo.ArgByIndex(2), _playerStatuses[player.Slot].HeadshotOnly);
                        if (hsSetting != null)
                        {
                            SetHeadshotOnly(player, hsSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "hs.help")}");
                        return;
                    case "xray":
                        var xraySetting = ToggleSetting.Parse(commandInfo.ArgByIndex(2), _playerStatuses[player.Slot].Xray);
                        if (xraySetting != null)
                        {
                            SetXray(player, xraySetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "xray.help")}");
                        return;
                    case "botsfire":
                        var botsFireSetting = ToggleSetting.Parse(commandInfo.ArgByIndex(2), _playerStatuses[player.Slot].BotsFire);
                        if (botsFireSetting != null)
                        {
                            SetBotsFire(player, botsFireSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "botsfire.help")}");
                        return;
                    case "bothealth":
                        var botHealthSetting = BotHealthSetting.Parse(commandInfo.ArgByIndex(2));
                        if (botHealthSetting != null)
                        {
                            SetBotHealth(player, botHealthSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "bothealth.help")}");
                        return;
                    case "botarmor":
                        var botArmorSetting = BotArmorSetting.Parse(commandInfo.ArgByIndex(2));
                        if (botArmorSetting != null)
                        {
                            SetBotArmor(player, botArmorSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "botarmor.help")}");
                        return;
                    case "countdown":
                        var countdownSetting = CountdownSetting.Parse(commandInfo.ArgByIndex(2));
                        if (countdownSetting != null)
                        {
                            SetCountdown(player, countdownSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "countdown.help")}");
                        return;
                    case "comp":
                        var compSetting = ToggleSetting.Parse(commandInfo.ArgByIndex(2), _playerStatuses[player.Slot].Comp);
                        if (compSetting != null)
                        {
                            SetComp(player, compSetting.Value);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "comp.help")}");
                        return;
                    case "lang":
                        string language = commandInfo.ArgByIndex(2);
                        switch (language)
                        {
                            case "en":
                                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                                break;
                            case "pt":
                                _translator!.UpdatePlayerCulture(player.SteamID, "pt-BR");
                                break;
                            case "zh":
                                _translator!.UpdatePlayerCulture(player.SteamID, "ZH");
                                break;
                            default:
                                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "languagemenu.help")}");
                                return;
                        }
                        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "languagemenu.set")}");
                        return;
                    case "top":
                    {
                        // "top", "top 3", "top comp" or "top comp 3": the word comp limits the list to comp runs.
                        var topCompOnly = false;
                        var topRoute = "";
                        for (var argIndex = 2; argIndex < commandInfo.ArgCount; argIndex++)
                        {
                            var topArg = commandInfo.ArgByIndex(argIndex);
                            if (topArg.Equals("comp", StringComparison.OrdinalIgnoreCase))
                                topCompOnly = true;
                            else
                                topRoute = topArg;
                        }
                        HandleTopCommand(player, topRoute, topCompOnly);
                        return;
                    }
                    case "stats":
                        HandleStatsCommand(player);
                        return;
                    case "record":
                        HandleRecordCommand(player, commandInfo.ArgByIndex(2));
                        return;
                    case "exit":
                        ForceStopPractice(player);
                        return;
                    case "binds":
                        PrintBinds(player);
                        return;
                    case "modes":
                    case "gamemodes":
                    {
                        if (commandInfo.ArgCount > 2)
                        {
                            // An explicit chat command is its own confirmation.
                            var modeQuery = string.Join(" ", Enumerable.Range(2, commandInfo.ArgCount - 2).Select(argIndex => commandInfo.ArgByIndex(argIndex)));
                            var chosenMode = GameModeList.Find(LoadGameModes(), modeQuery);
                            if (chosenMode == null)
                            {
                                player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gamemodes.unknown", modeQuery)}");
                                return;
                            }

                            SwitchGameMode(player, chosenMode);
                            return;
                        }

                        if (UseScreenMenu())
                        {
                            OpenMenu(player, BuildMainMenu(player));
                            _screenMenu!.Push(player, BuildGameModesMenu(player));
                        }
                        else
                        {
                            OpenMenu(player, BuildGameModesMenu(player));
                        }
                        return;
                    }
                    case "help":
                        PrintHelp(player);
                        return;
                    // case "test":
                    //     SetMoney(player, 0);
                    //     AddTimer(5f, () => SetMoney(player, 123));
                    //     return;
                    default:
                        PrintHelp(player);
                        break;
                }
            }

            // Draw the menu
            OpenMenu(player, BuildMainMenu(player));
        });

        CommandManager.RegisterCommand(_command);
    }

    private string? _lastGameModesWarning;

    /// <summary>
    /// The server's game modes from GameModeManager.json, read fresh each time because the admin may edit it.
    /// Empty (with one console line per distinct problem) when the file is missing or unreadable.
    /// </summary>
    private IReadOnlyList<GameMode> LoadGameModes()
    {
        string? path = null;
        try
        {
            var configured = _defaultPlayerSettings?.GameModesConfig ?? "";
            path = string.IsNullOrWhiteSpace(configured)
                ? GameModeList.FindConfigPath(ModuleDirectory, File.Exists)
                : configured;
            if (path == null)
            {
                WarnGameModes("GameModeManager.json not found next to this plugin; set GameModesConfig in default_cfg.json.");
                return Array.Empty<GameMode>();
            }

            var modes = GameModeList.Parse(File.ReadAllText(path));
            _lastGameModesWarning = null;
            return modes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            WarnGameModes($"could not read {path}: {e.Message}");
            return Array.Empty<GameMode>();
        }
    }

    private void WarnGameModes(string message)
    {
        if (_lastGameModesWarning == message)
        {
            return;
        }

        _lastGameModesWarning = message;
        Console.WriteLine($"[Freefrag] Game modes: {message}");
    }

    private bool CanSwitchGameModes(CCSPlayerController player)
    {
        var flag = _defaultPlayerSettings?.GameModesPermission ?? "";
        return string.IsNullOrWhiteSpace(flag) || AdminManager.PlayerHasPermissions(player, flag);
    }

    private MenuSpec BuildGameModesMenu(CCSPlayerController player)
    {
        var title = _translator!.Translate(player, "gamemodesmenu.title");
        if (!CanSwitchGameModes(player))
        {
            return new MenuSpec(title, new List<MenuItem>
            {
                new MenuItem(_translator.Translate(player, "gamemodesmenu.denied"), AfterSettingChosen),
            });
        }

        var modes = LoadGameModes();
        if (modes.Count == 0)
        {
            return new MenuSpec(title, new List<MenuItem>
            {
                new MenuItem(_translator.Translate(player, "gamemodesmenu.none"), AfterSettingChosen),
            });
        }

        var items = new List<MenuItem>();
        foreach (var mode in modes)
        {
            var chosen = mode;
            items.Add(MenuItem.Opens(chosen.Name, () => BuildGameModeConfirmMenu(player, chosen)));
        }

        return new MenuSpec(title, items);
    }

    private MenuSpec BuildGameModeConfirmMenu(CCSPlayerController player, GameMode mode)
    {
        var items = new List<MenuItem>
        {
            new MenuItem(_translator!.Translate(player, "gamemodesmenu.confirm_yes"), p => SwitchGameMode(p, mode)),
            new MenuItem(_translator.Translate(player, "gamemodesmenu.confirm_no"), AfterGameModeDeclined),
        };

        return new MenuSpec(_translator.Translate(player, "gamemodesmenu.confirm_title", mode.Name), items);
    }

    /// <summary>"No, go back": rebuild main and put the mode list back on top (Back still returns to main).</summary>
    private void AfterGameModeDeclined(CCSPlayerController player)
    {
        if (CloseScreenMenu(player))
        {
            OpenMenu(player, BuildMainMenu(player));
            _screenMenu!.Push(player, BuildGameModesMenu(player));
        }
        else
        {
            CloseCurrentMenu(player);
        }
    }

    /// <summary>
    /// Runs the mode's cfg, exactly as an admin would by rcon. The plugin stays loaded in every mode: the cfgs
    /// signal the new mode (css_freefrag_mode) and Freefrag goes dormant outside Prefire. The menu is closed
    /// first and the command waits for the next frame.
    /// </summary>
    private void SwitchGameMode(CCSPlayerController player, GameMode mode)
    {
        if (!CanSwitchGameModes(player))
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gamemodes.denied")}");
            return;
        }

        CloseAllMenus(player);
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gamemodes.switching", mode.Name)}");
        Console.WriteLine($"[Freefrag] {player.PlayerName} ({player.SteamID}) switched the server to game mode {mode.Name} ({mode.Config}).");
        var command = GameModeList.SwitchCommand(mode);
        Server.NextFrame(() => Server.ExecuteCommand(command));
    }

    /// <summary>Outside Prefire only two entries make sense: the mode list and a shortcut back to Prefire.</summary>
    private MenuSpec BuildDormantMainMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>
        {
            MenuItem.Opens(_translator!.Translate(player, "mainmenu.gamemodes"), () => BuildGameModesMenu(player)),
            new MenuItem(_translator.Translate(player, "mainmenu.switch_prefire"), SwitchToPrefire),
        };

        return new MenuSpec(_translator.Translate(player, "mainmenu.title"), items, _translator.Translate(player, "mainmenu.shortcut_prompt"));
    }

    private void SwitchToPrefire(CCSPlayerController player)
    {
        var prefire = GameModeList.Find(LoadGameModes(), ModeGate.PrefireMode);
        if (prefire == null)
        {
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gamemodesmenu.none")}");
            CloseAllMenus(player);
            return;
        }

        SwitchGameMode(player, prefire);
    }

    private MenuSpec BuildMainMenu(CCSPlayerController player)
    {
        if (!_modeGate.Active)
        {
            return BuildDormantMainMenu(player);
        }

        var items = new List<MenuItem>();

        // 1 Practice menu
        items.Add(MenuItem.Opens(_translator!.Translate(player, "mainmenu.practice"), () => BuildPracticeMenu(player)));

        // 2 Map menu
        items.Add(MenuItem.Opens(_translator.Translate(player, "mainmenu.map"), () => BuildMapMenu(player)));

        // 3 Difficulty menu
        string currentDifficulty = _translator.Translate(player, $"difficulty.{_playerStatuses[player.Slot].HealingMethod}");
        items.Add(MenuItem.Opens(_translator.Translate(player, "mainmenu.difficulty", currentDifficulty), () => BuildDifficultyMenu(player)));

        // 4 Training mode menu
        string currentTrainingMode = _translator.Translate(player, $"modemenu.{_playerStatuses[player.Slot].TrainingMode}");
        items.Add(MenuItem.Opens(_translator.Translate(player, "mainmenu.mode", currentTrainingMode), () => BuildModeMenu(player)));

        // 5 Bot weapon menu
        string currentBotWeapon = _playerStatuses[player.Slot].BotWeapon == 0 ? _translator!.Translate(player, "weaponmenu.random") : BotLoadout.DisplayName(_playerStatuses[player.Slot].BotWeapon);
        items.Add(MenuItem.Opens(_translator!.Translate(player, "mainmenu.bot_weapon", currentBotWeapon), () => BuildBotWeaponMenu(player)));

        // 6 More options menu
        items.Add(MenuItem.Opens(_translator.Translate(player, "mainmenu.more"), () => BuildMoreOptionsMenu(player)));

        // Game modes menu
        items.Add(MenuItem.Opens(_translator.Translate(player, "mainmenu.gamemodes"), () => BuildGameModesMenu(player)));

        // 7 Language menu
        items.Add(MenuItem.Opens("Language preference", () => BuildLanguageMenu(player)));

        // 8 (Optional) End practicing button
        if (_playerStatuses[player.Slot].PracticeIndex > 0)
        {
            items.Add(new MenuItem(_translator.Translate(player, "mainmenu.exit"), OnForceExitPrefireMode));
        }

        // 9 Close menu button: added by the chat renderer; the screen menu closes with Tab.

        return new MenuSpec(_translator!.Translate(player, "mainmenu.title"), items, _translator.Translate(player, "mainmenu.shortcut_prompt"));
    }

    private void CloseCurrentMenu(CCSPlayerController player)
    {
        MenuManager.CloseActiveMenu(player);
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "mainmenu.menu_closed")}");
    }

    private void SetBotWeapon(CCSPlayerController player, int botWeapon)
    {
        if (_playerStatuses[player.Slot].BotWeapon != botWeapon && _playerStatuses[player.Slot].PracticeIndex > -1)
        {
            _playerStatuses[player.Slot].Run.MarkDirty();
        }
        _playerStatuses[player.Slot].BotWeapon = botWeapon;

        string weaponName = botWeapon == 0 ? _translator!.Translate(player, "weaponmenu.random") : BotLoadout.DisplayName(botWeapon);

        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "weaponmenu.set", weaponName)}");
    }

    private void SetPlayerGun(CCSPlayerController player, string gun)
    {
        _playerStatuses[player.Slot].PlayerGun = gun;
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "gun.set", gun)}");
        if (_playerStatuses[player.Slot].PracticeIndex > -1)
            EquipPlayer(player);
    }

    private void SetJitter(CCSPlayerController player, int jitter, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.Jitter))
            return;

        _playerStatuses[player.Slot].Jitter = jitter;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "jitter.set", jitter)}");
    }

    private void SetRepeat(CCSPlayerController player, int repeat, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.Repeat))
            return;

        _playerStatuses[player.Slot].Repeat = repeat;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "repeat.set", RepeatSetting.DisplayName(repeat))}");
    }

    // While comp is on the settings in CompLock are pinned; both chat and menus reach them through the setters below.
    private bool RefuseWhileComp(CCSPlayerController player, LockedSetting setting)
    {
        if (!CompLock.Refuses(_playerStatuses[player.Slot].Comp, setting))
            return false;

        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "comp.locked")}");
        return true;
    }

    private void SetBotsFire(CCSPlayerController player, bool on, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.BotsFire))
            return;

        var status = _playerStatuses[player.Slot];
        if (status.BotsFire != on && status.PracticeIndex > -1)
        {
            status.Run.MarkDirty();
        }
        status.BotsFire = on;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "botsfire.set", ToggleSetting.DisplayName(on))}");
    }

    private void SetBotHealth(CCSPlayerController player, int health, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.BotHealth))
            return;

        var status = _playerStatuses[player.Slot];
        if (status.BotHealth != health && status.PracticeIndex > -1)
        {
            status.Run.MarkDirty();
        }
        status.BotHealth = health;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "bothealth.set", health)}");
    }

    private void SetBotArmor(CCSPlayerController player, int armor, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.BotArmor))
            return;

        var status = _playerStatuses[player.Slot];
        if (status.BotArmor != armor && status.PracticeIndex > -1)
        {
            status.Run.MarkDirty();
        }
        status.BotArmor = armor;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "botarmor.set", armor)}");
    }

    // Takes effect at the next route start, so changing it mid-route does not void the run.
    private void SetCountdown(CCSPlayerController player, int seconds, bool announce = true)
    {
        if (RefuseWhileComp(player, LockedSetting.Countdown))
            return;

        _playerStatuses[player.Slot].Countdown = seconds;
        if (announce)
            player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "countdown.set", seconds)}");
    }

    private void SetComp(CCSPlayerController player, bool on)
    {
        var status = _playerStatuses[player.Slot];
        if (on && !status.Comp)
        {
            // Pin every comp value through the normal setters (so a mid-route change voids the run as usual),
            // with Comp still off so they are not refused, then lock.
            var pins = _defaultPlayerSettings!.Comp;
            SetBotsFire(player, pins.BotsFire == 1, false);
            SetBotHealth(player, pins.BotHealth, false);
            SetBotArmor(player, pins.BotArmor, false);
            SetCountdown(player, pins.Countdown, false);
            SetRepeat(player, pins.Repeat, false);
            SetJitter(player, pins.Jitter, false);
            status.Comp = true;
            if (status.PracticeIndex > -1)
            {
                // A comp run has to start under comp rules (countdown included).
                status.Run.MarkDirty();
            }
        }
        else if (!on)
        {
            status.Comp = false;
        }

        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "comp.set", ToggleSetting.DisplayName(status.Comp))}");
    }


    private void SetAutoSkip(CCSPlayerController player, int deaths)
    {
        _playerStatuses[player.Slot].AutoSkipDeaths = deaths;
        _playerStatuses[player.Slot].DeathsOnRoute = 0;
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {(deaths == 0 ? _translator!.Translate(player, "autoskip.off") : _translator!.Translate(player, "autoskip.set", deaths))}");
    }

    private void SetHeadshotOnly(CCSPlayerController player, bool on)
    {
        if (_playerStatuses[player.Slot].HeadshotOnly != on && _playerStatuses[player.Slot].PracticeIndex > -1)
        {
            _playerStatuses[player.Slot].Run.MarkDirty();
        }
        _playerStatuses[player.Slot].HeadshotOnly = on;
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "hs.set", ToggleSetting.DisplayName(on))}");
    }

    private void SetXray(CCSPlayerController player, bool on)
    {
        _playerStatuses[player.Slot].Xray = on;
        player.PrintToChat($" {ChatColors.Green}{ChatPrefix} {ChatColors.White} {_translator!.Translate(player, "xray.set", ToggleSetting.DisplayName(on))}");
        if (_playerStatuses[player.Slot].PracticeIndex > -1)
        {
            if (on)
            {
                foreach (var botSlot in _playerStatuses[player.Slot].Bots.ToList())
                {
                    var xrayBot = Utilities.GetPlayerFromSlot(botSlot);
                    if (xrayBot != null && xrayBot.IsValid && xrayBot.PawnIsAlive)
                        ApplyGlow(player.Slot, xrayBot);
                }
            }
            else
            {
                RemoveAllGlow(player.Slot);
            }
        }
    }

    private static readonly string[] GunChoices = { "ak", "m4", "m4a1s", "awp", "deagle", "usp", "glock", "none" };
    private static readonly int[] JitterChoices = { 0, 16, 32, 48, 64 };
    private static readonly int[] RepeatChoices = { RepeatSetting.Off, 1, 3, 5, RepeatSetting.Infinite };
    private static readonly int[] AutoSkipChoices = { 0, 1, 2, 3, 5 };
    private static readonly int[] BotHealthChoices = { 1, 25, 50, 100, 250, 500, 1000 };
    private static readonly int[] BotArmorChoices = { 0, 50, 100 };
    private static readonly int[] CountdownChoices = { 0, 1, 2, 3, 4, 5 };

    private static string OffOrNumber(int value) => value == 0 ? "off" : value.ToString();

    private string GunLabel(CCSPlayerController player, string gun) =>
        gun == "none" ? _translator!.Translate(player, "gunmenu.none") : gun;

    /// <summary>
    /// An option on the More options menu was changed. Screen: rebuild the More options level in place so its
    /// labels show the new value and the cursor stays on the row just changed; when the change was made in a
    /// submenu of More options, step back out of it first. Chat: close, as for any setting.
    /// </summary>
    private void AfterMoreOptionChosen(CCSPlayerController player, bool fromSubmenu = false)
    {
        if (UseScreenMenu())
        {
            if (fromSubmenu)
            {
                _screenMenu!.Back(player);
            }
            _screenMenu!.Replace(player, BuildMoreOptionsMenu(player));
        }
        else
        {
            CloseCurrentMenu(player);
        }
    }

    private MenuSpec BuildMoreOptionsMenu(CCSPlayerController player)
    {
        var status = _playerStatuses[player.Slot];
        var items = new List<MenuItem>
        {
            new MenuItem(_translator!.Translate(player, "moremenu.comp", ToggleSetting.DisplayName(status.Comp)), p =>
            {
                SetComp(p, !_playerStatuses[p.Slot].Comp);
                AfterMoreOptionChosen(p);
            }),
            new MenuItem(_translator!.Translate(player, "moremenu.hs", ToggleSetting.DisplayName(status.HeadshotOnly)), p =>
            {
                SetHeadshotOnly(p, !_playerStatuses[p.Slot].HeadshotOnly);
                AfterMoreOptionChosen(p);
            }),
            new MenuItem(_translator.Translate(player, "moremenu.xray", ToggleSetting.DisplayName(status.Xray)), p =>
            {
                SetXray(p, !_playerStatuses[p.Slot].Xray);
                AfterMoreOptionChosen(p);
            }),
            MenuItem.Opens(_translator.Translate(player, "moremenu.gun", GunLabel(player, status.PlayerGun)), () => BuildGunMenu(player)),
            new MenuItem(_translator.Translate(player, "moremenu.botsfire", ToggleSetting.DisplayName(status.BotsFire)), p =>
            {
                SetBotsFire(p, !_playerStatuses[p.Slot].BotsFire);
                AfterMoreOptionChosen(p);
            }),
            MenuItem.Opens(_translator.Translate(player, "moremenu.bothealth", status.BotHealth), () => BuildBotHealthMenu(player)),
            MenuItem.Opens(_translator.Translate(player, "moremenu.botarmor", status.BotArmor), () => BuildBotArmorMenu(player)),
            MenuItem.Opens(_translator.Translate(player, "moremenu.jitter", OffOrNumber(status.Jitter)), () => BuildJitterMenu(player)),
            MenuItem.Opens(_translator.Translate(player, "moremenu.repeat", RepeatSetting.DisplayName(status.Repeat)), () => BuildRepeatMenu(player)),
            MenuItem.Opens(_translator.Translate(player, "moremenu.countdown", OffOrNumber(status.Countdown)), () => BuildCountdownMenu(player)),
            MenuItem.Opens(_translator.Translate(player, "moremenu.autoskip", OffOrNumber(status.AutoSkipDeaths)), () => BuildAutoSkipMenu(player)),
            new MenuItem(_translator.Translate(player, "moremenu.top"), p =>
            {
                CloseAllMenus(p);
                HandleTopCommand(p, "");
            }),
            new MenuItem(_translator.Translate(player, "moremenu.stats"), p =>
            {
                CloseAllMenus(p);
                HandleStatsCommand(p);
            }),
            new MenuItem(_translator.Translate(player, "moremenu.binds"), p =>
            {
                CloseAllMenus(p);
                PrintBinds(p);
            }),
        };

        return new MenuSpec(_translator.Translate(player, "moremenu.title"), items);
    }

    private MenuSpec BuildGunMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var gun in GunChoices)
        {
            var choice = gun;
            items.Add(new MenuItem(GunLabel(player, choice), p =>
            {
                SetPlayerGun(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "gunmenu.title"), items);
    }

    private MenuSpec BuildJitterMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var jitter in JitterChoices)
        {
            var choice = jitter;
            items.Add(new MenuItem(OffOrNumber(choice), p =>
            {
                SetJitter(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "jittermenu.title"), items);
    }

    private MenuSpec BuildRepeatMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var repeat in RepeatChoices)
        {
            var choice = repeat;
            items.Add(new MenuItem(RepeatSetting.DisplayName(choice), p =>
            {
                SetRepeat(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "repeatmenu.title"), items);
    }

    private MenuSpec BuildAutoSkipMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var deaths in AutoSkipChoices)
        {
            var choice = deaths;
            items.Add(new MenuItem(OffOrNumber(choice), p =>
            {
                SetAutoSkip(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "autoskipmenu.title"), items);
    }

    private MenuSpec BuildBotHealthMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var health in BotHealthChoices)
        {
            var choice = health;
            items.Add(new MenuItem(choice.ToString(), p =>
            {
                SetBotHealth(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "bothealthmenu.title"), items);
    }

    private MenuSpec BuildBotArmorMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var armor in BotArmorChoices)
        {
            var choice = armor;
            items.Add(new MenuItem(choice.ToString(), p =>
            {
                SetBotArmor(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "botarmormenu.title"), items);
    }

    private MenuSpec BuildCountdownMenu(CCSPlayerController player)
    {
        var items = new List<MenuItem>();
        foreach (var seconds in CountdownChoices)
        {
            var choice = seconds;
            items.Add(new MenuItem(OffOrNumber(choice), p =>
            {
                SetCountdown(p, choice);
                AfterMoreOptionChosen(p, fromSubmenu: true);
            }));
        }

        return new MenuSpec(_translator!.Translate(player, "countdownmenu.title"), items);
    }

    private MenuSpec BuildPracticeMenu(CCSPlayerController player)
    {
        // Dynamically draw menu
        var items = new List<MenuItem>();
        _playerStatuses[player.Slot].LocalizedPracticeNames.Clear();

        // Add menu options for practices
        for (var i = 0; i < _practices.Count; i++)
        {
            if (!RouteBlockedByOthers(player.Slot, i))
            {
                var tmpLocalizedPracticeName = _translator!.Translate(player, $"map.{_mapName}.{_practices[i].PracticeName}");
                _playerStatuses[player.Slot].LocalizedPracticeNames.Add(tmpLocalizedPracticeName, i);
                items.Add(new MenuItem(tmpLocalizedPracticeName, p => OnRouteSelect(p, tmpLocalizedPracticeName))); // practice name here is split by space instead of underline. TODO: Use localized text.
            }
        }
        // The player's own active route is already listed by the loop above (RouteBlockedByOthers never
        // blocks it), so it must not be added a second time: the dictionary would throw on the duplicate key.

        return new MenuSpec(_translator!.Translate(player, "practicemenu.title"), items);
    }

    private void UnsetPrefireMode(int playerSlot)
    {
        CancelCountdown(playerSlot);
        RemoveAllGlow(playerSlot);
        _playerStatuses[playerSlot].DeathsOnRoute = 0;
        _playerStatuses[playerSlot].PendingRoute = -1;
        var previousPracticeNo = _playerStatuses[playerSlot].PracticeIndex;
        if (previousPracticeNo > -1)
        {
            StopRecording(playerSlot);
            ClearRecording(playerSlot);
            RemoveBots(playerSlot);
            DeleteGuidingLine(playerSlot);

            // Enable disabled practice routes
            for (var i = 0; i < _practices[previousPracticeNo].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.TryGetValue(_practices[previousPracticeNo].IncompatiblePractices[i], out var value))
                {
                    _pracConflictCount[value]--;
                }
            }
            _pracConflictCount[previousPracticeNo]--;

            _playerStatuses[playerSlot].PracticeIndex = -1;
            _playerCount--;

            // Drop any pending bot request so it cannot block the next player's request.
            DropBotRequest(playerSlot);
        }
    }

    private void SetMoney(CCSPlayerController player, int money)
    {
        var moneyServices = player.InGameMoneyServices;
        if (moneyServices == null)
        {
            return;
        }
        
        moneyServices.Account = money;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private void PrintProgress()
    {
        var players = Utilities.GetPlayers();

        foreach (var player in players)
        {
            if (!player.IsValid || player.IsBot || player.IsHLTV || !_playerStatuses.ContainsKey(player.Slot) || _playerStatuses[player.Slot].PracticeIndex < 0)
            {
                continue;
            }

            // If player is practicing, print timer
            string tmpPractice = _translator!.Translate(player, "map." + _mapName + "." + _practices[_playerStatuses[player.Slot].PracticeIndex].PracticeName);
            string tmpProgress = _translator!.Translate(player, "practice.progress", _playerStatuses[player.Slot].EnabledTargets.Count, _playerStatuses[player.Slot].EnabledTargets.Count - _playerStatuses[player.Slot].Progress + _playerStatuses[player.Slot].Bots.Count);
            string content = $"{tmpPractice}\u2029{tmpProgress}";

            player.PrintToCenter(content);
        }
    }

    private void LogHookErrorOnce(string hookName, System.Exception e)
    {
        if (_hookErrorsLogged.Add($"{hookName}:{e.GetType().Name}"))
        {
            Console.WriteLine($"[Freefrag] Error in {hookName} (further errors of this type are not logged): {e}");
        }
    }

    private static CCSPlayerController? ControllerOfPlayerPawn(CEntityInstance? entity)
    {
        if (entity == null || entity.DesignerName != "player")
        {
            return null;
        }

        var controller = new CCSPlayerPawn(entity.Handle).OriginalController.Value;
        return controller != null && controller.IsValid ? controller : null;
    }

    private HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo info)
    {
        if (!_modeGate.Active)
        {
            return HookResult.Continue;
        }

        try
        {
            var controller = ControllerOfPlayerPawn(info.Attacker.Value);
            if (controller == null || controller.IsBot)
            {
                return HookResult.Continue;
            }

            if (!_playerStatuses.TryGetValue(controller.Slot, out var status) || !status.HeadshotOnly || status.PracticeIndex < 0)
            {
                return HookResult.Continue;
            }

            // Only the attacker's own managed bots are affected: never other humans, unmanaged bots or self-damage.
            var victim = ControllerOfPlayerPawn(entity);
            if (victim == null || !_ownerOfBots.TryGetValue(victim.Slot, out var owner) || owner != controller.Slot)
            {
                return HookResult.Continue;
            }

            var hitGroup = info.GetHitGroup();
            if (hitGroup == HitGroup_t.HITGROUP_HEAD || hitGroup == HitGroup_t.HITGROUP_INVALID)
            {
                return HookResult.Continue;
            }

            info.Damage = 0;
            return HookResult.Changed;
        }
        catch (System.Exception e)
        {
            LogHookErrorOnce(nameof(OnTakeDamage), e);
            return HookResult.Continue;
        }
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_glowCount == 0)
        {
            return;
        }

        try
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null)
                {
                    continue;
                }

                foreach (var (ownerSlot, status) in _playerStatuses)
                {
                    if (ownerSlot == player.Slot || status.GlowEntities.Count == 0)
                    {
                        continue;
                    }

                    foreach (var indices in status.GlowEntities.Values)
                    {
                        foreach (var index in indices)
                        {
                            info.TransmitEntities.Remove(index);
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            LogHookErrorOnce(nameof(OnCheckTransmit), e);
        }
    }

    private void ApplyGlow(int ownerSlot, CCSPlayerController bot)
    {
        if (!_playerStatuses.TryGetValue(ownerSlot, out var status) || !status.Xray)
        {
            return;
        }

        // The bot may have been killed or kicked during the spawn timer.
        if (bot == null || !bot.IsValid || !bot.PawnIsAlive || !_ownerOfBots.TryGetValue(bot.Slot, out var botOwner) || botOwner != ownerSlot)
        {
            return;
        }

        var pawn = bot.PlayerPawn.Value;
        var model = pawn?.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
        if (pawn == null || string.IsNullOrEmpty(model))
        {
            return;
        }

        RemoveGlow(ownerSlot, bot.Slot);

        var relay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        var glow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (relay == null || glow == null)
        {
            relay?.AcceptInput("Kill");
            glow?.AcceptInput("Kill");
            return;
        }

        relay.SetModel(model);
        relay.Spawnflags = 256;
        relay.RenderMode = RenderMode_t.kRenderNone;
        relay.DispatchSpawn();
        relay.AcceptInput("FollowEntity", pawn, relay, "!activator");

        glow.SetModel(model);
        glow.Spawnflags = 256;
        glow.DispatchSpawn();
        glow.AcceptInput("FollowEntity", relay, glow, "!activator");
        glow.Glow.GlowColorOverride = System.Drawing.Color.FromArgb(255, 255, 60, 60);
        glow.Glow.GlowRange = 5000;
        glow.Glow.GlowRangeMin = 0;
        glow.Glow.GlowTeam = -1;
        glow.Glow.GlowType = 3;

        status.GlowEntities[bot.Slot] = new List<int> { (int)relay.Index, (int)glow.Index };
        _glowCount++;
    }

    private void RemoveGlow(int ownerSlot, int botSlot)
    {
        if (!_playerStatuses.TryGetValue(ownerSlot, out var status) || !status.GlowEntities.Remove(botSlot, out var indices))
        {
            return;
        }

        _glowCount = Math.Max(0, _glowCount - 1);
        foreach (var index in indices)
        {
            var entity = Utilities.GetEntityFromIndex<CBaseEntity>(index);
            if (entity != null && entity.IsValid && entity.DesignerName == "prop_dynamic")
            {
                entity.AcceptInput("Kill");
            }
        }
    }

    private void RemoveAllGlow(int ownerSlot)
    {
        if (!_playerStatuses.TryGetValue(ownerSlot, out var status) || status.GlowEntities.Count == 0)
        {
            return;
        }

        foreach (var botSlot in status.GlowEntities.Keys.ToList())
        {
            RemoveGlow(ownerSlot, botSlot);
        }
    }

    private void KickBot(int botSlot)
    {

        if (_ownerOfBots.ContainsKey(botSlot))
        {
            int ownerSlot = _ownerOfBots[botSlot];
            RemoveGlow(ownerSlot, botSlot);
            if (_playerStatuses[ownerSlot].Bots.Contains(botSlot))
            {
                _playerStatuses[ownerSlot].Bots.Remove(botSlot);
            }
            _ownerOfBots.Remove(botSlot);
        }

        var bot = Utilities.GetPlayerFromSlot(botSlot);

        if (bot == null || !bot.IsValid || !bot.IsBot)
        {
            return;
        }

        Server.ExecuteCommand($"bot_kick {bot.PlayerName}");
        Console.WriteLine($"[Freefrag] Exec command: bot_kick {bot.PlayerName}");
    }

    // Thanks to B3none
    // Code borrowed from cs2-retake/RetakesPlugin/Modules/Managers/BreakerManager.cs
    private void BreakBreakables()
    {
        // Enable this feature only on nuke and mirage. (mirage is disabled because of the crash issue on Windows)
        if (Server.MapName != "de_nuke") // && Server.MapName != "de_mirage")
        {
            Console.WriteLine($"[Freefrag] Map {Server.MapName} doesn't have breakables to break.");
            return;
        }

        Console.WriteLine($"[Freefrag] Map {Server.MapName} have breakables to break.");

        // Enable certain breakables on certain maps to avoid game crash
        List<string> enabled_breakables =
        [
            // Common breakables
            "func_breakable",
            "func_breakable_surf",
            "prop.breakable.01",
            "prop.breakable.02",
        ];

        if (Server.MapName == "de_nuke")
        {
            enabled_breakables.Add("prop_door_rotating");
            enabled_breakables.Add("prop_dynamic");
        }

        if (Server.MapName == "de_mirage")
        {
            enabled_breakables.Add("prop_dynamic");
        }

        // Console.WriteLine($"[Freefrag] DEBUG: Have breakables: {enabled_breakables}");

        // Loop to find breakables
        CEntityIdentity ?pEntity = new CEntityIdentity(EntitySystem.FirstActiveEntity);
        while (pEntity != null && pEntity.Handle != IntPtr.Zero)
        {
            if (!enabled_breakables.Contains(pEntity.DesignerName))
            {
                pEntity = pEntity.Next;
                continue;
            }

            switch (pEntity.DesignerName)
            {
                case "func_breakable":
                case "func_breakable_surf":
                case "prop.breakable.01":
                case "prop.breakable.02":
                case "prop_dynamic":
                    CBreakable breakableEntity = new PointerTo<CBreakable>(pEntity.Handle).Value;
                    if (breakableEntity.IsValid)
                    {
                        breakableEntity.AcceptInput("Break");
                    }
                    break;
                case "func_button":
                    CBaseButton button = new PointerTo<CBaseButton>(pEntity.Handle).Value;
                    if (button.IsValid)
                    {
                        button.AcceptInput("Kill");
                    }
                    break;
                case "prop_door_rotating":
                    CPropDoorRotating propDoorRotating = new PointerTo<CPropDoorRotating>(pEntity.Handle).Value;
                    if (propDoorRotating.IsValid)
                    {
                        propDoorRotating.AcceptInput("Open");
                    }
                    break;
                default:
                    break;
            }

            // Get next entity
            pEntity = pEntity.Next;
        }
    }

    private void OnTickHandler()
    {
        if (_screenMenu != null)
        {
            foreach (var slot in _screenMenu.OpenSlots())
            {
                var menuPlayer = Utilities.GetPlayerFromSlot(slot);
                if (menuPlayer != null && menuPlayer.IsValid && !menuPlayer.IsBot)
                {
                    _screenMenu.Tick(menuPlayer);
                }
            }
        }

        if (_defaultPlayerSettings!.BotAimLock == 1)
        {
            foreach (int playerSlot in _playerStatuses.Keys)
            {
                var player = Utilities.GetPlayerFromSlot(playerSlot);

                if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value == null)
                {
                    continue;
                }

                // Aimlock bots
                Vector ownerEyePos = new Vector(player.PlayerPawn.Value.AbsOrigin!.X, player.PlayerPawn.Value.AbsOrigin!.Y, player.PlayerPawn.Value.AbsOrigin!.Z + player.PlayerPawn.Value.ViewmodelOffsetZ);

                foreach(var botSlot in _playerStatuses[playerSlot].Bots)
                {
                    var bot = Utilities.GetPlayerFromSlot(botSlot);

                    if (bot == null || !bot.IsValid || !bot.PawnIsAlive || bot.PlayerPawn.Value == null)
                    {
                        continue;
                    }

                    Vector botEyePosition = new Vector(bot.PlayerPawn.Value.AbsOrigin!.X, bot.PlayerPawn.Value.AbsOrigin!.Y, bot.PlayerPawn.Value.AbsOrigin!.Z + bot.PlayerPawn.Value.ViewmodelOffsetZ);

                    // calculate angle
                    float deltaX = ownerEyePos.X - botEyePosition.X;
                    float deltaY = ownerEyePos.Y - botEyePosition.Y;
                    float deltaZ = ownerEyePos.Z - botEyePosition.Z;
                    double yaw = 180 * Math.Atan2(deltaY, deltaX) / Math.PI;
                    double tmp = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    double pitch= 180 * Math.Atan2(-1 * deltaZ, tmp) / Math.PI;
                    QAngle angle = new QAngle((float)pitch, (float)yaw, 0);

                    Server.NextFrame(() => {
                        if (pitch < 15 && pitch > -15)
                        {
                            bot.PlayerPawn.Value.Teleport(null, angle, null);
                        }
                        bot.PlayerPawn.Value.EyeAngles.X = (float)pitch;
                        bot.PlayerPawn.Value.EyeAngles.Y = (float)yaw;
                    });
                }
            }
        }
    }

}
