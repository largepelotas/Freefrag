using CounterStrikeSharp.API.Core;

namespace OpenPrefirePrac;

/// <summary>
/// One entry of a menu. Either OnChoose runs when picked, or Submenu (built lazily, when picked)
/// is opened. Submenu exists so the WASD backend can wire the Tab-to-go-back link to this entry.
/// </summary>
public sealed record MenuItem(string Label, Action<CCSPlayerController> OnChoose, Func<MenuSpec>? Submenu = null)
{
    public static MenuItem Opens(string label, Func<MenuSpec> build) => new(label, _ => { }, build);
}

/// <summary>Backend-neutral description of a menu; rendered as a ChatMenu or a WASD menu.</summary>
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
    public const int Wasd = 1;

    /// <summary>
    /// Config 0 always uses chat menus. Anything else asks for WASD menus and gets them only when
    /// the WASDMenuAPI plugin is present; otherwise chat menus.
    /// </summary>
    public static int Choose(int configured, bool wasdAvailable)
    {
        if (configured == Chat) return Chat;
        return wasdAvailable ? Wasd : Chat;
    }
}
