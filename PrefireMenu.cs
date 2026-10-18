using CounterStrikeSharp.API.Core;

namespace Freefrag;

/// <summary>
/// One entry of a menu. Either OnChoose runs when picked, or Submenu (built lazily, when picked)
/// is opened. Submenu exists so the screen menu can push it as a level that A / Backspace returns from.
/// Done marks an entry the player has already completed this session; it is drawn dimmed but stays selectable.
/// </summary>
public sealed record MenuItem(string Label, Action<CCSPlayerController> OnChoose, Func<MenuSpec>? Submenu = null, bool Done = false)
{
    public static MenuItem Opens(string label, Func<MenuSpec> build) => new(label, _ => { }, build);
}

/// <summary>Backend-neutral description of a menu; rendered as a ChatMenu or a screen menu.</summary>
public sealed class MenuSpec
{
    public MenuSpec(string title, List<MenuItem> items, string? chatFooter = null)
    {
        Title = title;
        Items = items;
        ChatFooter = chatFooter;
    }

    public string Title { get; }
    public List<MenuItem> Items { get; }

    /// <summary>Extra chat line printed under the chat rendering only (the main menu's shortcut hint).</summary>
    public string? ChatFooter { get; }
}

public static class MenuBackend
{
    public const int Chat = 0;
    public const int Screen = 1;

    /// <summary>Config 0 uses chat menus; anything else uses the in-plugin screen menus.</summary>
    public static int Choose(int configured) => configured == Chat ? Chat : Screen;
}
