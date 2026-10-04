using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

public enum GrantKind { Gives, Progress, ResearchOption }

/// <summary>Where in an event a grant runs. Grants of common/ objects (traditions, perks, …) use Immediate.</summary>
public enum EventPart { Immediate, Option, After }

/// <summary>One tech-granting effect. Progress is the add_tech_progress fraction (0.25 = 25 %), null when not a number or not progress.</summary>
public sealed record TechGrant(GrantKind Kind, double? Progress, EventPart Part, int? OptionIndex, string? Condition, string? Via);

/// <summary>Something that grants a tech: an event (KindFolder "events") or a common/ object.</summary>
public sealed record GrantSource(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, IReadOnlyList<TechGrant> Grants)
{
    public const string EventsFolder = "events";
    public bool IsEvent => KindFolder == EventsFolder;
}

public sealed record EventOption(string Name, string? Condition);

/// <summary>An event that grants a tech, as shown in the event window. Varies = the game picks the text or picture by conditions; the first is kept.</summary>
public sealed record GameEvent(string Id, string Type, string Title, string? Description, bool DescriptionVaries,
    string? Picture, bool PictureVaries, bool Hidden, IReadOnlyList<EventOption> Options, TechSourceRef Source);

/// <summary>Short texts for grants, shared by the sidebar and the event window.</summary>
public static class GrantText
{
    public static string Percent(double? progress) =>
        progress is { } p ? "+" + Math.Round(p * 100, 1).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "+?";

    /// <summary>"Gives" wins; otherwise progress and/or research option, e.g. "Option +25 %".</summary>
    public static string Badge(IReadOnlyList<TechGrant> grants)
    {
        if (grants.Any(g => g.Kind == GrantKind.Gives)) return "Gives";
        var progress = grants.FirstOrDefault(g => g.Kind == GrantKind.Progress);
        var option = grants.Any(g => g.Kind == GrantKind.ResearchOption);
        if (progress is not null) return option ? "Option " + Percent(progress.Progress) : Percent(progress.Progress);
        return "Research option";
    }

    /// <summary>give, prog, mix (option + progress) or opt; matches the badge colours in site.css.</summary>
    public static string BadgeClass(IReadOnlyList<TechGrant> grants)
    {
        if (grants.Any(g => g.Kind == GrantKind.Gives)) return "give";
        var progress = grants.Any(g => g.Kind == GrantKind.Progress);
        var option = grants.Any(g => g.Kind == GrantKind.ResearchOption);
        return progress ? (option ? "mix" : "prog") : "opt";
    }

    /// <summary>Where in an event the grants are: one place in words, or "N places". Empty for non-events.</summary>
    public static string Where(GrantSource source, GameEvent? ev)
    {
        if (!source.IsEvent) return "";
        var places = source.Grants.Select(g => (g.Part, g.OptionIndex)).Distinct().ToList();
        if (places.Count != 1) return $"{places.Count} places";
        var (part, index) = places[0];
        return part switch
        {
            EventPart.Immediate => "when the event fires",
            EventPart.After => "after any option",
            _ => index is { } i && ev is not null && i < ev.Options.Count ? $"option “{ev.Options[i].Name}”" : $"option {(index ?? 0) + 1}",
        };
    }

    /// <summary>"Gives Psionic Theory", "+25 % Psionic Theory" or "Research option: Psionic Theory", plus " (via effect)" and " — when: condition".</summary>
    public static string Effect(TechGrant grant, string techName)
    {
        var text = grant.Kind switch
        {
            GrantKind.Gives => "Gives " + techName,
            GrantKind.Progress => Percent(grant.Progress) + " " + techName,
            _ => "Research option: " + techName,
        };
        if (grant.Via is not null) text += $" (via {grant.Via})";
        if (grant.Condition is not null) text += " — when: " + grant.Condition;
        return text;
    }
}
