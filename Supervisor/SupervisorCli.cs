using Spectre.Console.Cli;
using Supervisor.Commands.Hub;
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

        config.AddBranch("hub", hub =>
        {
            hub.SetDescription("Inspect and control the Hub for this machine.");

            hub.AddCommand<HubStatusCommand>("status")
                .WithDescription("Report whether a Hub is running, and what is attached to it.")
                .WithExample("hub", "status", "--json");

            hub.AddCommand<HubStopCommand>("stop")
                .WithDescription("Stop the running Hub. Refuses while clients are attached unless --force.")
                .WithExample("hub", "stop", "--force");

            hub.AddCommand<HubServeCommand>("serve")
                .WithDescription("Run the Hub in this process. Normally spawned by start-or-attach.")
                .WithExample("hub", "serve", "--foreground");
        });
    }
}
