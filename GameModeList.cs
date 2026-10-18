using System.Text.Json;

namespace Freefrag;

/// <summary>One entry of GameModeManager's mode list: what the menu shows and the cfg that switches to it.</summary>
public sealed record GameMode(string Name, string Config);

/// <summary>
/// Reads the server's game modes from GameModeManager.json and finds one by a loose name. Pure: the caller does
/// the file access, so none of this touches the engine.
/// </summary>
public static class GameModeList
{
    private const string ConfigFileName = "GameModeManager.json";
    private const int MaxLevelsUp = 4;

    /// <summary>
    /// The entries of GameModes.List in file order. Entries without a Name, without a Config, or with a Config that
    /// is not <see cref="IsSafeConfig"/> are skipped. Malformed JSON throws a <see cref="JsonException"/>.
    /// </summary>
    public static IReadOnlyList<GameMode> Parse(string json)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(json, options);

        var modes = new List<GameMode>();
        if (!TryGetProperty(document.RootElement, "GameModes", out var section)
            || !TryGetProperty(section, "List", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return modes;
        }

        foreach (var entry in list.EnumerateArray())
        {
            var name = ReadString(entry, "Name");
            var config = ReadString(entry, "Config");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(config) || !IsSafeConfig(config))
            {
                continue;
            }

            modes.Add(new GameMode(name, config));
        }

        return modes;
    }

    /// <summary>True for a plain relative cfg path (letters, digits, _ - . /) that is safe to put on a console line.</summary>
    public static bool IsSafeConfig(string config)
    {
        if (string.IsNullOrEmpty(config) || config.StartsWith('/') || config.Contains(".."))
        {
            return false;
        }

        foreach (var c in config)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.' && c != '/')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The console line that switches to the mode: exactly what an admin types by rcon.</summary>
    public static string SwitchCommand(GameMode mode) => "exec " + mode.Config;

    /// <summary>
    /// Case-insensitive lookup: by name, then by cfg file (with or without .cfg), then by name with the spaces
    /// removed. Null when nothing matches.
    /// </summary>
    public static GameMode? Find(IReadOnlyList<GameMode> modes, string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0)
        {
            return null;
        }

        var byName = modes.FirstOrDefault(m => m.Name.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (byName != null)
        {
            return byName;
        }

        var byConfig = modes.FirstOrDefault(m =>
            m.Config.Equals(query, StringComparison.OrdinalIgnoreCase)
            || m.Config.Equals(query + ".cfg", StringComparison.OrdinalIgnoreCase));
        if (byConfig != null)
        {
            return byConfig;
        }

        var squashed = RemoveSpaces(query);
        return modes.FirstOrDefault(m => RemoveSpaces(m.Name).Equals(squashed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Looks for configs/plugins/GameModeManager/GameModeManager.json in the module directory and up to four
    /// parents (the plugin sits in plugins/Freefrag or plugins/disabled/Freefrag, the configs next to plugins).
    /// </summary>
    public static string? FindConfigPath(string moduleDirectory, Func<string, bool> fileExists)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(moduleDirectory));
        for (var level = 0; level <= MaxLevelsUp; level++)
        {
            var candidate = Path.Combine(dir, "configs", "plugins", "GameModeManager", ConfigFileName);
            if (fileExists(candidate))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            dir = parent;
        }

        return null;
    }

    private static string RemoveSpaces(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
