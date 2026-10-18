namespace Freefrag;

public static class PlayerLoadout
{
    public static string? ParseGun(string token)
    {
        var gun = token.ToLowerInvariant();
        return gun is "ak" or "m4" or "m4a1s" or "awp" or "deagle" or "usp" or "glock" or "none" ? gun : null;
    }

    public static IReadOnlyList<string> ItemsFor(string gun)
    {
        var items = new List<string>();
        switch (gun)
        {
            case "ak": items.Add("weapon_ak47"); break;
            case "m4": items.Add("weapon_m4a1"); break;
            case "m4a1s": items.Add("weapon_m4a1_silencer"); break;
            case "awp": items.Add("weapon_awp"); break;
        }

        items.Add(gun switch
        {
            "usp" => "weapon_usp_silencer",
            "glock" => "weapon_glock",
            _ => "weapon_deagle"
        });
        items.AddRange(new[] { "weapon_knife", "weapon_flashbang", "weapon_flashbang", "weapon_smokegrenade", "item_assaultsuit" });
        return items;
    }
}
