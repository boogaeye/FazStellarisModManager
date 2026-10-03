using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GameLauncherTests
{
    [Fact]
    public void Launch_throws_when_exe_missing()
    {
        using var tmp = new TempDir();

        var ex = Assert.Throws<FileNotFoundException>(() => GameLauncher.Launch(tmp.Path));

        Assert.Equal(Path.Combine(tmp.Path, "stellaris.exe"), ex.FileName);
    }
}
