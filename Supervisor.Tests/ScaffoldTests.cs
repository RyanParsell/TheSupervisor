using Spectre.Console.Testing;

namespace Supervisor.Tests;

/// <summary>
/// WU-A's tracer bullet. Proves the solution builds, the command tree is reachable from tests
/// through the same <see cref="SupervisorCli.Configure"/> seam the real binary uses, and the CLI
/// reports a version.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void SolutionBuildsAndCliReportsVersion()
    {
        var app = new CommandAppTester();
        app.Configure(SupervisorCli.Configure);

        var result = app.Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.Matches(@"\d+\.\d+\.\d+", result.Output);
    }
}
