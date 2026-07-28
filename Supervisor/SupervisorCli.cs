using Spectre.Console.Cli;
using Supervisor.Core;

namespace Supervisor;

/// <summary>
/// The single place the command tree is defined, so tests configure exactly the same tree the
/// real binary runs. Program.cs and the test harness both call <see cref="Configure"/>; neither
/// builds its own.
/// </summary>
public static class SupervisorCli
{
    public static void Configure(IConfigurator config)
    {
        config.SetApplicationName("supervisor");
        config.SetApplicationVersion(SupervisorVersion.Current);
    }
}
