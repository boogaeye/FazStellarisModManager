using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class DiploDisplayTests
{
    [Theory]
    [InlineData(0.425, "+42%")]
    [InlineData(0.715291, "+71%")]
    [InlineData(1.70, "+170%")]
    [InlineData(0.29, "+29%")]
    [InlineData(0.05, "+5%")]
    [InlineData(-0.205, "-20%")]
    [InlineData(-0.2, "-20%")]
    [InlineData(0, "+0%")]
    public void Percentages_are_truncated_like_the_game(double fraction, string expected) =>
        Assert.Equal(expected, DiploBonus.FormatPercent(fraction));

    [Fact]
    public void Names_resolve_nested_references_and_drop_colour_codes()
    {
        var loc = new Localisation();
        loc.AddText("""
            l_english:
             achievement_01:0 "$giga_achievement_01_title$"
             giga_achievement_01_title:0 "§E$giga_achievement_01_group_header$ $giga_achievement_01$§!"
             giga_achievement_01_group_header:0 "Kardashev's"
             giga_achievement_01:0 "Type 2"
             councilor_secret_societies: "Conspirator Liaison"
            """);
        var context = new DiploContext(ModifierCatalog.Empty, DiploDefines.Vanilla, loc);
        Assert.Equal("Kardashev's Type 2", context.Name(DiploSource.StaticModifier, "achievement_01"));
        Assert.Equal("Conspirator Liaison", context.Name(DiploSource.Councilor, "councilor_secret_societies"));
        Assert.Equal("unknown key", context.Name(DiploSource.Perk, "unknown_key"));
    }
}
