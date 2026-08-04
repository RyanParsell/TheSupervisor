using Supervisor.Core.Roster;

namespace Supervisor.Core.Fleet;

/// <summary>What to ask the Fleet for.</summary>
/// <remarks>
/// One optional filter today. It is a record rather than a bare string so adding a dimension later —
/// Repository, Machine, Status — does not change every call site and every wire contract.
/// </remarks>
public sealed record FleetQuery
{
    /// <summary>Restrict to Agents working under this directory. Null means all of them.</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>The Fleet as of one moment.</summary>
public sealed record FleetView
{
    /// <summary>Attention-ordered (D23): waiting, then errored/stopped, then busy, idle, unenrolled.</summary>
    public required IReadOnlyList<RosterEntry> Agents { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Which Machine answered. Once Peers merge rosters a view has to say whose view it is, and
    /// establishing it now means the wire shape does not change when federation lands.
    /// </summary>
    public required string MachineId { get; init; }
}

/// <summary>
/// The one read that both the <c>list</c> verb and the Fleet MCP tool go through.
/// </summary>
/// <remarks>
/// D14, narrowed by L4. Two surfaces answering the same question is exactly how they drift: the
/// verb grows a filter the tool does not have, or the tool starts reporting a field the table
/// dropped, and nobody notices until an Agent and a human disagree about what is running. Making
/// both delegate to one method means drift requires deleting this interface, not merely forgetting.
/// </remarks>
public interface IFleetQueryService
{
    Task<FleetView> QueryAsync(FleetQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Matches working directories the way <c>claude agents --cwd</c> does.
/// </summary>
/// <remarks>
/// Its help says "show only background sessions started <em>under</em> &lt;path&gt;" — a subtree
/// match, not equality. Mirroring it matters because both commands will be run against the same
/// fleet on the same Machine, and two different answers to <c>--cwd C:\Code</c> is the kind of
/// discrepancy that costs an hour to trace.
/// </remarks>
public static class FleetFilter
{
    public static bool IsUnder(string workingDirectory, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        // A row with no working directory cannot be shown to be under the path. Excluding it keeps
        // the filter honest; it is still listed when nothing is filtering.
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return false;
        }

        var root = Normalize(filter);
        var candidate = Normalize(workingDirectory);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (candidate.Equals(root, comparison))
        {
            return true;
        }

        // The separator is load-bearing: without it "C:\Code\Foo" matches "C:\Code\FooBar", which
        // shows another repository's Agents and looks entirely plausible at a glance.
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, comparison);
    }

    private static string Normalize(string path)
    {
        try
        {
            // GetFullPath resolves a relative --cwd against the current directory, which is what a
            // developer typing `--cwd .` means. TrimEndingDirectorySeparator leaves a root alone.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }
}

/// <summary>Answers Fleet queries from the assembled Roster.</summary>
public sealed class RosterFleetQueryService : IFleetQueryService
{
    private readonly RosterAssembler _assembler;
    private readonly TimeProvider _time;

    public RosterFleetQueryService(RosterAssembler assembler, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(assembler);
        _assembler = assembler;
        _time = time ?? TimeProvider.System;
    }

    public async Task<FleetView> QueryAsync(
        FleetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var agents = await _assembler.BuildAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query.WorkingDirectory))
        {
            agents = agents
                .Where(a => FleetFilter.IsUnder(a.WorkingDirectory, query.WorkingDirectory))
                .ToList();
        }

        return new FleetView
        {
            Agents = agents,
            ObservedAt = _time.GetUtcNow(),
            MachineId = _assembler.MachineId,
        };
    }
}
