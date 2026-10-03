using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ModDescriptorTests
{
    // Real descriptor shape from Documents\Paradox Interactive\Stellaris\mod
    const string Sample = """
        path="C:/Users/SCP Fazbear/Documents/Paradox Interactive/Stellaris/mod/Fazverse_4.4_with_Flamer/8cde_010c1b4b"
        name="(Fazverse 4.4 with Flamer) Ancient Cache of Technologies: Extra Defines and Changes"
        picture="thumbnail.png"
        remote_file_id="2288335512"
        tags={
        	"Balance"
        	"Gameplay"
        }
        supported_version="v4.*"
        """;

    [Fact]
    public void Parses_real_descriptor()
    {
        var d = ModDescriptor.Parse(Sample);

        Assert.Equal("(Fazverse 4.4 with Flamer) Ancient Cache of Technologies: Extra Defines and Changes", d.Name);
        Assert.Equal("C:/Users/SCP Fazbear/Documents/Paradox Interactive/Stellaris/mod/Fazverse_4.4_with_Flamer/8cde_010c1b4b", d.Path);
        Assert.Equal("2288335512", d.RemoteFileId);
        Assert.Equal("v4.*", d.SupportedVersion);
        Assert.Equal("thumbnail.png", d.Picture);
        Assert.Equal(new[] { "Balance", "Gameplay" }, d.Tags);
        Assert.Null(d.Archive);
        Assert.Empty(d.Dependencies);
    }

    [Fact]
    public void Serialize_round_trips()
    {
        var d = ModDescriptor.Parse(Sample) with { Dependencies = ["Some \"Quoted\" Mod"] };

        var again = ModDescriptor.Parse(d.Serialize());

        Assert.Equal(d.Name, again.Name);
        Assert.Equal(d.Path, again.Path);
        Assert.Equal(d.RemoteFileId, again.RemoteFileId);
        Assert.Equal(d.SupportedVersion, again.SupportedVersion);
        Assert.Equal(d.Tags, again.Tags);
        Assert.Equal(d.Dependencies, again.Dependencies);
    }

    [Fact]
    public void Serialize_omits_null_fields()
    {
        var d = ModDescriptor.Parse("name=\"Only Name\"");

        Assert.Equal("name=\"Only Name\"\n", d.Serialize());
    }
}
