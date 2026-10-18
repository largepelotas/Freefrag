using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace OpenPrefirePrac;

/// <summary>
/// Renders MenuSpecs in the centre-screen HTML area and handles navigation.
/// Buttons W/S/D/A/Tab need no setup; the same actions are also exposed to the plugin so
/// console commands bound to arrow keys, Enter and Backspace can drive them. This is the only menu
/// code that touches the engine.
/// </summary>
public sealed class ScreenMenu
{
    private const long ScoreboardButton = 1L << 33;     // Tab; not a named PlayerButtons member.

    private sealed class PlayerMenu
    {
        public readonly Stack<(MenuSpec Spec, MenuCursor Cursor)> Levels = new();
        public string? Html;                            // Cached; null when it has to be rebuilt.
        public long PreviousButtons;
    }

    private readonly Dictionary<int, PlayerMenu> _players = new();
    private readonly Func<int> _rows;
    private readonly Func<CCSPlayerController, string> _footer;

    public ScreenMenu(Func<int> rows, Func<CCSPlayerController, string> footer)
    {
        _rows = rows;
        _footer = footer;
    }

    public bool IsOpen(CCSPlayerController player) =>
        _players.TryGetValue(player.Slot, out var menu) && menu.Levels.Count > 0;

    /// <summary>Slots with a menu open; a copy, so callers may open or close menus while iterating.</summary>
    public IReadOnlyList<int> OpenSlots()
    {
        List<int>? slots = null;
        foreach (var (slot, menu) in _players)
        {
            if (menu.Levels.Count > 0)
                (slots ??= new List<int>()).Add(slot);
        }
        return slots ?? (IReadOnlyList<int>)Array.Empty<int>();
    }

    public void Open(CCSPlayerController player, MenuSpec spec)
    {
        var menu = GetOrAdd(player);
        menu.Levels.Clear();
        menu.PreviousButtons = ReadButtons(player);     // Keys already held must not count as presses.
        PushLevel(menu, spec);
    }

    public void Push(CCSPlayerController player, MenuSpec spec)
    {
        PushLevel(GetOrAdd(player), spec);
    }

    /// <summary>Up one level; at the main menu this closes.</summary>
    public void Back(CCSPlayerController player)
    {
        if (!_players.TryGetValue(player.Slot, out var menu) || menu.Levels.Count == 0)
            return;

        menu.Levels.Pop();
        menu.Html = null;
    }

    public void Close(CCSPlayerController player)
    {
        if (!_players.TryGetValue(player.Slot, out var menu))
            return;

        menu.Levels.Clear();
        menu.Html = null;
    }

    public void Up(CCSPlayerController player)
    {
        if (TryTop(player, out var menu, out _, out var cursor))
        {
            cursor.Up();
            menu.Html = null;
        }
    }

    public void Down(CCSPlayerController player)
    {
        if (TryTop(player, out var menu, out _, out var cursor))
        {
            cursor.Down();
            menu.Html = null;
        }
    }

    public void Select(CCSPlayerController player)
    {
        if (!TryTop(player, out _, out var spec, out var cursor) || cursor.Index >= spec.Items.Count)
            return;

        var item = spec.Items[cursor.Index];
        if (item.Submenu != null)
            Push(player, item.Submenu());
        else
            item.OnChoose(player);
    }

    /// <summary>Per-tick: turns button presses into actions and keeps the centre text on screen.</summary>
    public void Tick(CCSPlayerController player)
    {
        if (!_players.TryGetValue(player.Slot, out var menu) || menu.Levels.Count == 0)
            return;

        var buttons = ReadButtons(player);
        var pressed = buttons & ~menu.PreviousButtons;
        menu.PreviousButtons = buttons;

        if ((pressed & ScoreboardButton) != 0)
            Close(player);
        else if ((pressed & (long)PlayerButtons.Forward) != 0)
            Up(player);
        else if ((pressed & (long)PlayerButtons.Back) != 0)
            Down(player);
        else if ((pressed & (long)PlayerButtons.Moveright) != 0)
            Select(player);
        else if ((pressed & (long)PlayerButtons.Moveleft) != 0)
            Back(player);

        if (menu.Levels.Count == 0)
            return;

        // The centre text fades, so it is re-sent every tick.
        var html = menu.Html ??= BuildHtml(player, menu);
        Server.NextFrame(() =>
        {
            if (player.IsValid && IsOpen(player))
                player.PrintToCenterHtml(html);
        });
    }

    public void ForgetPlayer(int slot) => _players.Remove(slot);

    public void Clear() => _players.Clear();

    private PlayerMenu GetOrAdd(CCSPlayerController player)
    {
        if (!_players.TryGetValue(player.Slot, out var menu))
        {
            menu = new PlayerMenu();
            _players[player.Slot] = menu;
        }
        return menu;
    }

    private void PushLevel(PlayerMenu menu, MenuSpec spec)
    {
        var rows = Math.Clamp(_rows(), 1, 8);
        menu.Levels.Push((spec, new MenuCursor(Math.Max(1, spec.Items.Count), rows)));
        menu.Html = null;
    }

    private bool TryTop(CCSPlayerController player, out PlayerMenu menu, out MenuSpec spec, out MenuCursor cursor)
    {
        spec = null!;
        cursor = null!;
        if (!_players.TryGetValue(player.Slot, out menu!) || menu.Levels.Count == 0)
            return false;

        (spec, cursor) = menu.Levels.Peek();
        return true;
    }

    private string BuildHtml(CCSPlayerController player, PlayerMenu menu)
    {
        var (spec, cursor) = menu.Levels.Peek();
        var labels = spec.Items.Select(item => item.Label).ToList();
        return ScreenMenuHtml.Render(spec.Title, labels, cursor, _footer(player));
    }

    private static long ReadButtons(CCSPlayerController player)
    {
        try
        {
            return (long)player.Buttons;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
