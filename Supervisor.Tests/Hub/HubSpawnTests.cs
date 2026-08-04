using System.Diagnostics;
using Supervisor.Core.Hub;

namespace Supervisor.Tests.Hub;

public sealed class HubSpawnTests
{
    [Fact]
    public void UsesShellExecuteWithHiddenWindow()
    {
        var info = HubSpawn.CreateStartInfo(@"C:\tools\supervisor.exe", @"C:\repo");

        // D16. A console-LESS process cannot bind a pseudoconsole child, so a Hub spawned with
        // UseShellExecute=false + CreateNoWindow=true comes up alive but with a dead terminal —
        // the child spawns and no output ever reaches the pipe. UseShellExecute=true gives the Hub
        // its own real console; WindowStyle=Hidden keeps that console invisible.
        //
        // WExpert shipped the broken form and spent a debugging cycle on it before finding that the
        // fix is one ProcessStartInfo property. Nothing in WU-B binds a pseudoconsole, so this test
        // is the only thing standing between us and rediscovering it in WU-3.
        Assert.True(info.UseShellExecute,
            "UseShellExecute must be true so the Hub gets a real console it can bind a ConPTY child to.");
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);
        Assert.False(info.CreateNoWindow,
            "CreateNoWindow produces a console-less process, which is exactly the broken form.");
    }

    [Fact]
    public void CarriesTheWorkingDirectory()
    {
        var info = HubSpawn.CreateStartInfo(@"C:\tools\supervisor.exe", @"C:\repo");

        Assert.Equal(@"C:\repo", info.WorkingDirectory);
    }

    [Fact]
    public void RejectsAnEmptyExecutablePath()
    {
        Assert.Throws<ArgumentException>(() => HubSpawn.CreateStartInfo("  ", @"C:\repo"));
    }
}
