using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using WASDMenuAPI.Shared;
using WASDMenuAPI.Shared.Models;

namespace OpenPrefirePrac;

/// <summary>
/// Renders MenuSpecs through the WASDMenuAPI plugin. This is the only class that touches
/// WASDMenuAPI.Shared types, and none of them appear in its public surface: when WASDMenuAPI is not
/// installed the shared assembly cannot be loaded, the failure surfaces as an exception when
/// TryCreate is called (JIT time), and the caller catches it and falls back to chat menus.
/// </summary>
public sealed class WasdBackend
{
    private readonly IWasdMenuManager _manager;

    private WasdBackend(IWasdMenuManager manager)
    {
        _manager = manager;
    }

    /// <summary>Returns null when no plugin registered the capability.</summary>
    public static WasdBackend? TryCreate()
    {
        try
        {
            var manager = new PluginCapability<IWasdMenuManager>("wasdmenu:manager").Get();
            return manager == null ? null : new WasdBackend(manager);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Opens spec as the player's main menu. replace: close whatever is open first.</summary>
    public void OpenMain(CCSPlayerController player, MenuSpec spec, bool replace)
    {
        if (replace)
        {
            _manager.CloseMenu(player);
        }
        _manager.OpenMainMenu(player, Build(spec));
    }

    public void CloseAll(CCSPlayerController player)
    {
        _manager.CloseMenu(player);
    }

    private IWasdMenu Build(MenuSpec spec)
    {
        var menu = _manager.CreateMenu(spec.Title);
        foreach (var item in spec.Items)
        {
            LinkedListNode<IWasdMenuOption>? node = null;
            node = menu.Add(item.Label, (p, _) =>
            {
                if (item.Submenu != null)
                {
                    var sub = Build(item.Submenu());
                    sub.Prev = node;    // Tab in the submenu returns to this entry of the parent.
                    _manager.OpenSubMenu(p, sub);
                }
                else
                {
                    item.OnChoose(p);
                }
            });
        }
        return menu;
    }
}
