using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor.Core.Fleet;
using Supervisor.Core.Roster;

namespace Supervisor.Commands;

/// <summary>
/// Resolves something that can answer Fleet queries, or reports that nothing can.
/// </summary>
/// <remarks>
/// The Roster lives in the Hub process, so answering "what is running" means finding a Hub first.
/// Null is a normal outcome — a machine with no Hub has an empty Fleet, not a broken tool — and
/// keeping that decision behind this seam is what lets the verb be tested without one.
/// </remarks>
public interface IFleetSource
{
    Task<IFleetQueryService?> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed class ListSettings : GlobalSettings
{
    [CommandOption("--cwd <PATH>")]
    [Description("Show only Agents working under this directory, matching `claude agents --cwd`.")]
    public string? Cwd { get; init; }
}

/// <summary>
/// Lists the Fleet: every Agent this Machine knows about, ordered by who needs attention.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does <em>not</em> start a Hub. "What is running?" and "make something run" are
/// different questions, and a listing verb that quietly spawned a background process would make the
/// first unanswerable — the same reason <c>hub status</c> refuses to.
/// </para>
/// <para>
/// Both this and the Fleet MCP tool go through <see cref="IFleetQueryService"/> (D14, L4), so a
/// human and an Agent asking the same question cannot get different answers.
/// </para>
/// </remarks>
public sealed class ListCommand : AsyncCommand<ListSettings>
{
    private readonly IAnsiConsole _console;
    private readonly IFleetSource _source;

    public ListCommand(IAnsiConsole console, IFleetSource source)
    {
        _console = console;
        _source = source;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var service = await _source.ResolveAsync().ConfigureAwait(false);

            if (service is null)
            {
                // Nothing has enrolled yet — the normal state of a machine that just booted, not a
                // failure. Exit 0 either way; a script must not have to special-case a quiet morning.
                if (settings.Json)
                {
                    Emit(Empty());
                }
                else
                {
                    _console.MarkupLine("[yellow]No Hub is running — nothing has enrolled yet.[/]");
                }

                return 0;
            }

            var view = await service
                .QueryAsync(new FleetQuery { WorkingDirectory = settings.Cwd })
                .ConfigureAwait(false);

            if (settings.Json)
            {
                Emit(view);
                return 0;
            }

            if (view.Agents.Count == 0)
            {
                _console.MarkupLine("[yellow]No agents are running.[/]");
                return 0;
            }

            _console.Write(Render(view));
            return 0;
        }
        finally
        {
            (_source as IDisposable)?.Dispose();
        }
    }

    private static FleetView Empty() => new()
    {
        Agents = [],
        ObservedAt = DateTimeOffset.UtcNow,
        MachineId = "",
    };

    /// <summary>
    /// Emits exactly the Hub's own wire shape.
    /// </summary>
    /// <remarks>
    /// Not a separate CLI-shaped payload: one serializer means <c>--json</c>, the HTTP response, and
    /// the MCP tool cannot drift into three dialects of the same object.
    /// </remarks>
    private void Emit(FleetView view) => JsonOutput.Write(_console, FleetJson.Serialize(view));

    private static Table Render(FleetView view)
    {
        // Ages are relative to when the Fleet was observed, not to now. The difference is
        // milliseconds today and the whole correctness of the column once a Peer's rows arrive
        // carrying their own Machine's clock.
        var presenter = new ActivitySummary();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Agent");
        table.AddColumn("Repository");
        table.AddColumn("Status");
        table.AddColumn("Age");
        table.AddColumn("Subagents");
        table.AddColumn("Activity");

        foreach (var agent in view.Agents)
        {
            var activity = presenter.PresentAsOf(agent.ActivitySummary, agent.ActivityAt, view.ObservedAt);

            // An unenrolled row has no Activity Summary that could advance — its age is simply how
            // long the session has been running — so the stale marker would fire on every such row
            // forever. A warning that is always on trains the eye straight past it.
            var stale = activity.IsStale && agent.Tier != AgentTier.Unenrolled;

            table.AddRow(
                Escape(agent.DisplayName),
                Escape(RepositoryLabel(agent.RepositoryId)),
                StatusMarkup(agent.Status),
                stale ? $"[yellow]{Escape(activity.AgeLabel)} stale[/]" : Escape(activity.AgeLabel),
                agent.SubagentCount > 0 ? agent.SubagentCount.ToString() : "-",
                Escape(activity.Text));
        }

        return table;
    }

    /// <summary>
    /// Shortens a Repository id to the part that differs between rows.
    /// </summary>
    /// <remarks>
    /// The host (<c>github.com/</c>) and the machine hash are identical on every row of a real
    /// listing, so they are pure width — and width is what pushes every other column into wrapping.
    /// The full id stays in <c>--json</c>, where something might key on it.
    /// </remarks>
    internal static string RepositoryLabel(string id)
    {
        const string machinePrefix = "machine:";

        if (id.StartsWith(machinePrefix, StringComparison.Ordinal))
        {
            // machine:<hash>/<path> — the path is the part a developer recognizes.
            var slash = id.IndexOf('/', machinePrefix.Length);
            return slash >= 0 && slash < id.Length - 1 ? id[(slash + 1)..] : id;
        }

        // A leading segment with a dot is a host (github.com, dev.azure.com). Anything else is
        // already as short as it gets, so leave it alone rather than guessing.
        var first = id.IndexOf('/');
        return first > 0 && id[..first].Contains('.', StringComparison.Ordinal)
            ? id[(first + 1)..]
            : id;
    }

    /// <summary>
    /// Colours by how much the developer is needed, matching the sort.
    /// </summary>
    /// <remarks>
    /// The colour and the ordering answer the same question, so they must not disagree — a red row
    /// halfway down the list would read as a bug in one of them.
    /// </remarks>
    private static string StatusMarkup(AgentStatus status) => status switch
    {
        AgentStatus.Waiting => "[bold yellow]waiting[/]",
        AgentStatus.Errored => "[red]errored[/]",
        AgentStatus.Stopped => "[red]stopped[/]",
        AgentStatus.Busy => "[green]busy[/]",
        AgentStatus.Idle => "[dim]idle[/]",
        AgentStatus.Unenrolled => "[grey]unenrolled[/]",
        _ => "[dim]unknown[/]",
    };

    /// <summary>
    /// Escapes content that came from a transcript.
    /// </summary>
    /// <remarks>
    /// An Activity Summary is whatever the Agent last did, which routinely contains square brackets
    /// — a file glob, an array literal, a markdown link. Unescaped, Spectre reads those as markup
    /// and the row either renders wrongly or throws mid-table.
    /// </remarks>
    private static string Escape(string value) => Markup.Escape(value);
}
