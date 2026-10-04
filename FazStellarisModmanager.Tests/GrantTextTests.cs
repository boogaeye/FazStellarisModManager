using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class GrantTextTests
{
    static TechGrant G(GrantKind kind, double? progress = null, EventPart part = EventPart.Option, int? option = 0, string? condition = null, string? via = null) =>
        new(kind, progress, part, option, condition, via);

    static readonly TechSourceRef Src = new("Base game", true, "events/x.txt");

    static GrantSource Event(params TechGrant[] grants) => new("Event", GrantSource.EventsFolder, "x.1", "X", Src, grants);

    [Fact]
    public void Badges_summarise_the_grants()
    {
        Assert.Equal(("Gives", "give"), (GrantText.Badge([G(GrantKind.Gives), G(GrantKind.Progress, 0.5)]), GrantText.BadgeClass([G(GrantKind.Gives)])));
        Assert.Equal(("+25 %", "prog"), (GrantText.Badge([G(GrantKind.Progress, 0.25)]), GrantText.BadgeClass([G(GrantKind.Progress, 0.25)])));
        Assert.Equal("+12.5 %", GrantText.Badge([G(GrantKind.Progress, 0.125)]));
        Assert.Equal(("Option +20 %", "mix"),
            (GrantText.Badge([G(GrantKind.ResearchOption), G(GrantKind.Progress, 0.2)]), GrantText.BadgeClass([G(GrantKind.ResearchOption), G(GrantKind.Progress, 0.2)])));
        Assert.Equal(("Research option", "opt"), (GrantText.Badge([G(GrantKind.ResearchOption)]), GrantText.BadgeClass([G(GrantKind.ResearchOption)])));
        Assert.Equal("+?", GrantText.Badge([G(GrantKind.Progress)]));
    }

    [Fact]
    public void Where_names_the_option_or_the_part()
    {
        var ev = new GameEvent("x.1", "country_event", "X", null, false, null, false, false,
            [new EventOption("Accept", null), new EventOption("Refuse", null)], Src);

        Assert.Equal("option “Refuse”", GrantText.Where(Event(G(GrantKind.Gives, option: 1)), ev));
        Assert.Equal("option 2", GrantText.Where(Event(G(GrantKind.Gives, option: 1)), null));
        Assert.Equal("when the event fires", GrantText.Where(Event(G(GrantKind.Gives, part: EventPart.Immediate, option: null)), ev));
        Assert.Equal("after any option", GrantText.Where(Event(G(GrantKind.Gives, part: EventPart.After, option: null)), ev));
        Assert.Equal("option “Accept”", GrantText.Where(Event(G(GrantKind.Gives), G(GrantKind.Progress, 0.1)), ev));
        Assert.Equal("2 places", GrantText.Where(Event(G(GrantKind.Gives), G(GrantKind.Gives, part: EventPart.After, option: null)), ev));
        Assert.Equal("", GrantText.Where(new GrantSource("Traditions", "traditions", "tr_x", "X", Src, [G(GrantKind.Gives)]), null));
    }

    [Fact]
    public void Effect_lines_include_how_and_when()
    {
        Assert.Equal("Gives Psionic Theory (via small_artifact_reward) — when: Civic: X",
            GrantText.Effect(G(GrantKind.Gives, condition: "Civic: X", via: "small_artifact_reward"), "Psionic Theory"));
        Assert.Equal("+25 % Psionic Theory", GrantText.Effect(G(GrantKind.Progress, 0.25), "Psionic Theory"));
        Assert.Equal("Research option: Psionic Theory", GrantText.Effect(G(GrantKind.ResearchOption), "Psionic Theory"));
    }
}
