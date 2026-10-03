using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class TechParserTests
{
    const string Sample = """
        @local_cost = 500
        # comment
        tech_a = {
        	area = physics
        	tier = 0
        	category = { particles }
        	cost = @local_cost
        	start_tech = yes
        }
        tech_b = {
        	area = society
        	tier = @someTier
        	category = { biology }
        	cost = 300
        	prerequisites = { "tech_a" tech_x }
        	is_rare = yes
        	is_dangerous = yes
        	levels = -1
        	icon = "tech_a"
        	potential = { NOT = { host_has_dlc = "Apocalypse" } }
        	weight_modifier = { modifier = { factor = 2 has_dlc = "Utopia" } }
        }
        """;

    [Fact]
    public void Parses_all_fields_and_skips_variables()
    {
        var defs = TechParser.Parse(Sample);

        Assert.Equal(new[] { "tech_a", "tech_b" }, defs.Select(d => d.Key));
        var a = defs[0];
        Assert.Equal(("physics", "0", "particles", "@local_cost"), (a.Area, a.Tier, a.Category, a.Cost));
        Assert.True(a.IsStart);
        Assert.False(a.IsRepeatable);
        Assert.Empty(a.Prerequisites);
        var b = defs[1];
        Assert.Equal(new[] { "tech_a", "tech_x" }, b.Prerequisites);
        Assert.True(b.IsRare && b.IsDangerous && b.IsRepeatable);
        Assert.Equal("tech_a", b.Icon);
        Assert.Equal(new[] { "Apocalypse", "Utopia" }, b.Dlcs);
    }

    [Fact]
    public void Levels_of_one_is_not_repeatable() =>
        Assert.False(TechParser.Parse("t = { levels = 1 }").Single().IsRepeatable);

    [Fact]
    public void Parses_and_resolves_scripted_variables()
    {
        var locals = ScriptedVariables.Parse("@a = 10\n@b = @a\n  @c = @missing # note\n");
        var globals = ScriptedVariables.Parse("@g = 7");

        Assert.Equal("10", ScriptedVariables.Resolve("@b", locals, globals));
        Assert.Equal("7", ScriptedVariables.Resolve("@g", locals, globals));
        Assert.Equal("@missing", ScriptedVariables.Resolve("@c", locals, globals));
        Assert.Equal("42", ScriptedVariables.Resolve("42", locals, globals));
        Assert.Equal("(formula)", ScriptedVariables.Resolve("@[ a * 2 ]", locals, globals));
    }
}
