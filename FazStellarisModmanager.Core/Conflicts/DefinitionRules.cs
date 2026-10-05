namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>What the game does with two definitions of the same name in different files of one folder.</summary>
public enum OverrideRule
{
    /// <summary>The definition loaded last is used.</summary>
    LastWins,
    /// <summary>The definition loaded first is used.</summary>
    FirstWins,
    /// <summary>Both are loaded (usually broken); only replacing the whole file overrides.</summary>
    Duplicated,
    /// <summary>Not documented (events): reported as a duplicate.</summary>
    Unknown,
}

/// <summary>Which files hold definitions and how duplicates resolve, after the Stellaris wiki's "Overwriting specific elements" table.</summary>
public static class DefinitionRules
{
    static readonly HashSet<string> FirstWins = new(StringComparer.OrdinalIgnoreCase)
    {
        "component_sets", "component_templates", "event_chains", "global_ship_designs", "scripted_loc",
        "scripted_variables", "solar_system_initializers", "special_projects", "start_screen_messages",
    };

    static readonly HashSet<string> Duplicated = new(StringComparer.OrdinalIgnoreCase)
    {
        "name_lists", "observation_station_missions", "strategic_resources", "terraform", "traits",
    };

    // on_actions merge; inline_scripts are pasted in by path rather than defined by name.
    static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "on_actions", "inline_scripts" };

    /// <summary>The folder ("common/buildings", "events") and kind of a scanned script file, or null when the file is not scanned.</summary>
    public static (string Folder, DefinitionKind Kind)? Classify(string relativePath)
    {
        var parts = relativePath.Replace((char)92, '/').Split('/');
        if (!parts[^1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
        if (parts.Length >= 2 && parts[0].Equals("events", StringComparison.OrdinalIgnoreCase)) return ("events", DefinitionKind.Events);
        if (parts.Length >= 3 && parts[0].Equals("common", StringComparison.OrdinalIgnoreCase) && !Skipped.Contains(parts[1]))
        {
            var type = parts[1].ToLowerInvariant();
            return ("common/" + type, type == "defines" ? DefinitionKind.Defines : DefinitionKind.TopLevel);
        }
        return null;
    }

    public static OverrideRule RuleFor(string folder)
    {
        if (folder.Equals("events", StringComparison.OrdinalIgnoreCase)) return OverrideRule.Unknown;
        var type = folder.StartsWith("common/", StringComparison.OrdinalIgnoreCase) ? folder["common/".Length..] : folder;
        if (FirstWins.Contains(type)) return OverrideRule.FirstWins;
        if (Duplicated.Contains(type)) return OverrideRule.Duplicated;
        return OverrideRule.LastWins;
    }
}
