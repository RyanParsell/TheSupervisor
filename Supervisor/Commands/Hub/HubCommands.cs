using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor.Core.Hub;

namespace Supervisor.Commands.Hub;

public sealed class HubStatusSettings : GlobalSettings;

/// <summary>
/// Reports whether a Hub is running for this user, and what is attached to it.
/// </summary>
/// <remarks>
/// Deliberately does <em>not</em> start a Hub. "Is one running?" and "make sure one is running" are
/// different questions, and a status verb that silently started a background process would make the
/// first unanswerable.
/// </remarks>
public sealed class HubStatusCommand : AsyncCommand<HubStatusSettings>
{
    private readonly IAnsiConsole _console;

    public HubStatusCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, HubStatusSettings settings)
    {
        var store = HubRendezvousStore.Default();
        using var probe = new HttpHubProbe();
        var locator = new HubLocator(store, probe);

        var rendezvous = await locator.TryAttachAsync().ConfigureAwait(false);
        if (rendezvous is null)
        {
            if (settings.Json)
            {
                JsonOutput.Write(_console, JsonSerializer.Serialize(new { running = false }));
            }
            else
            {
                _console.MarkupLine("[yellow]No Hub is running.[/]");
            }

            return 0;
        }

        using var client = new HubClient(rendezvous);
        var status = await client.GetStatusAsync().ConfigureAwait(false);
        if (status is null)
        {
            _console.MarkupLine("[red]The Hub stopped answering between the probe and the request.[/]");
            return 1;
        }

        if (settings.Json)
        {
            // The host secret is never part of any output. It authenticates control of the Hub, so
            // printing it would put it into shell history, scrollback, and any log capturing stdout.
            JsonOutput.Write(_console, JsonSerializer.Serialize(new
            {
                running = true,
                hostId = status.HostId,
                endpoint = rendezvous.Endpoint,
                buildVersion = status.BuildVersion,
                protocolVersion = status.ProtocolVersion,
                attachedClients = status.AttachedClients,
                startedAt = status.StartedAt,
                processId = rendezvous.ProcessId,
            }));
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Host", status.HostId);
        table.AddRow("Endpoint", rendezvous.Endpoint);
        table.AddRow("Build", status.BuildVersion);
        table.AddRow("Protocol", status.ProtocolVersion.ToString());
        table.AddRow("Attached clients", status.AttachedClients.ToString());
        table.AddRow("Started", status.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        table.AddRow("PID", rendezvous.ProcessId.ToString());
        _console.Write(table);

        return 0;
    }
}

public sealed class HubStopSettings : GlobalSettings
{
    [CommandOption("--force")]
    [Description("Stop even while clients are attached, destroying their state.")]
    public bool Force { get; init; }
}

/// <summary>Stops the running Hub.</summary>
public sealed class HubStopCommand : AsyncCommand<HubStopSettings>
{
    private readonly IAnsiConsole _console;

    public HubStopCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, HubStopSettings settings)
    {
        var store = HubRendezvousStore.Default();
        using var probe = new HttpHubProbe();
        var locator = new HubLocator(store, probe);

        var rendezvous = await locator.TryAttachAsync().ConfigureAwait(false);
        if (rendezvous is null)
        {
            _console.MarkupLine("[yellow]No Hub is running.[/]");
            return 0;
        }

        using var client = new HubClient(rendezvous);
        var result = await client.StopAsync(settings.Force).ConfigureAwait(false);

        if (result.Refused)
        {
            // Refusal is the safe default, so it exits non-zero and names the remedy rather than
            // quietly doing nothing.
            _console.MarkupLine(
                $"[red]Refused:[/] {result.AttachedClients} client(s) attached. Re-run with [bold]--force[/] to stop anyway.");
            return 1;
        }

        _console.MarkupLine("[green]Hub stopped.[/]");
        return 0;
    }
}
