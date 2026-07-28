using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor.Core.Hub;
using Supervisor.Web;

namespace Supervisor.Commands.Hub;

public sealed class HubServeSettings : GlobalSettings
{
    [CommandOption("--foreground")]
    [Description("Run in this terminal instead of detaching. Used by debugging and e2e.")]
    public bool Foreground { get; init; }
}

/// <summary>
/// Runs the Hub in this process.
/// </summary>
/// <remarks>
/// Internal by intent: users run <c>supervisor ui</c> or simply start a session, and start-or-attach
/// spawns this. It is exposed as a verb because the detached launcher needs something to spawn, and
/// because e2e and debugging need a Hub that does not detach.
/// </remarks>
public sealed class HubServeCommand : AsyncCommand<HubServeSettings>
{
    private readonly IAnsiConsole _console;

    public HubServeCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, HubServeSettings settings)
    {
        var store = HubRendezvousStore.Default();

        // Refuse rather than race: two Hubs sharing a rendezvous file would each overwrite the
        // other's endpoint, and every shim would attach to whichever wrote last.
        using (var probe = new HttpHubProbe())
        {
            var existing = await new HubLocator(store, probe).TryAttachAsync().ConfigureAwait(false);
            if (existing is not null)
            {
                _console.MarkupLine($"[yellow]A Hub is already running[/] ({existing.HostId} on {existing.Endpoint}).");
                return 1;
            }
        }

        await using var host = await HubHost.StartAsync(store).ConfigureAwait(false);

        _console.MarkupLine($"[green]Hub {host.Rendezvous.HostId}[/] listening on {host.Endpoint}");
        _console.MarkupLine("[dim]Stop it with: supervisor hub stop[/]");

        await host.WaitForShutdownAsync().ConfigureAwait(false);
        return 0;
    }
}
