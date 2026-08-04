using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor.Commands;
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
    /// <summary>
    /// Builds the dependency registrar the CLI runs with.
    /// </summary>
    /// <remarks>
    /// Shared with the tests deliberately. A command whose constructor takes something nobody
    /// registered still <em>compiles</em>, and the registrar hands it a null — so the failure lands
    /// on the developer's machine at dispatch time, not here. Having one definition means the test
    /// that checks constructibility is checking the real composition root rather than a copy of it
    /// that has quietly fallen behind.
    /// </remarks>
    public static ITypeRegistrar CreateRegistrar()
    {
        var registrar = new SimpleTypeRegistrar();
        registrar.RegisterInstance(typeof(IAnsiConsole), AnsiConsole.Console);
        registrar.RegisterInstance(typeof(IFleetSource), new HubFleetSource());
        return registrar;
    }

    public static void Configure(IConfigurator config)
    {
        config.SetApplicationName("supervisor");
        config.SetApplicationVersion(SupervisorVersion.Current);

        config.AddCommand<ListCommand>("list")
            .WithDescription("List every Agent this machine knows about, most urgent first.")
            .WithExample("list")
            .WithExample("list", "--json")
            .WithExample("list", "--cwd", @"C:\Code\Personal");

        config.AddCommand<InstallCommand>("install")
            .WithDescription("Register TheSupervisor with Claude Code. Safe to re-run.")
            .WithExample("install")
            .WithExample("install", "--dry-run");

        config.AddCommand<UninstallCommand>("uninstall")
            .WithDescription("Remove TheSupervisor from Claude Code, leaving settings.json as it was.")
            .WithExample("uninstall");

        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Report whether enrollment is actually working — fail-open hides this.")
            .WithExample("doctor");

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
