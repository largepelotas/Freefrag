namespace Freefrag;

public static class BotLoadout
{
    public static int? ParseBotWeapon(string token) => token.ToLowerInvariant() switch
    {
        "rand" => 0,
        "ump" => 1,
        "ak" => 2,
        "sct" => 3,
        "awp" => 4,
        "pistol" => 5,
        "eco" => 6,
        "force" => 7,
        _ => null
    };

    public static IReadOnlyList<string> ItemsFor(int botWeapon, Random rnd) => botWeapon switch
    {
        0 => Array.Empty<string>(),
        1 => new[] { "weapon_ump45" },
        2 => new[] { "weapon_ak47" },
        3 => new[] { "weapon_ssg08" },
        4 => new[] { "weapon_awp" },
        5 => new[] { new[] { "weapon_glock", "weapon_usp_silencer", "weapon_p250" }[rnd.Next(3)] },
        6 => new[] { new[] { "weapon_tec9", "weapon_fiveseven", "weapon_p250", "weapon_cz75a" }[rnd.Next(4)] },
        7 => new[] { new[] { "weapon_galilar", "weapon_famas", "weapon_mac10", "weapon_mp9" }[rnd.Next(4)], "item_assaultsuit" },
        _ => new[] { "weapon_ak47" }
    };

    public static string DisplayName(int botWeapon) => botWeapon switch
    {
        0 => "Random",
        1 => "UMP-45",
        2 => "AK47",
        3 => "SSG08",
        4 => "AWP",
        5 => "Pistol",
        6 => "Eco",
        7 => "Force",
        _ => "AK47"
    };
}
