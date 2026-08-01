namespace Supervisor.Core.Roster;

/// <summary>Coarse lifecycle state of an Agent.</summary>
public enum AgentStatus
{
    /// <summary>Working.</summary>
    Busy,

    /// <summary>Alive, nothing in flight.</summary>
    Idle,

    /// <summary>Blocked on the developer. The reason this product exists.</summary>
    Waiting,

    /// <summary>Failed in a way it cannot continue from.</summary>
    Errored,

    /// <summary>Ended, but still shown briefly so a finished Agent does not just vanish.</summary>
    Stopped,

    /// <summary>Seen on this Machine but never enrolled — visible, never controllable.</summary>
    Unenrolled,
}

/// <summary>Whether an Agent can be steered.</summary>
public enum AgentTier
{
    /// <summary>Launched by TheSupervisor into a Hub-owned pseudoterminal. Fully controllable.</summary>
    Owned,

    /// <summary>Enrolled from elsewhere. Observable; cooperatively controllable only.</summary>
    Foreign,

    /// <summary>Not enrolled at all. Observable only.</summary>
    Unenrolled,
}

/// <summary>One row of the Roster.</summary>
public sealed record RosterEntry
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public required string RepositoryId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string MachineId { get; init; }
    public required string MachineName { get; init; }
    public required AgentStatus Status { get; init; }
    public required AgentTier Tier { get; init; }

    /// <summary>One line: what this Agent is working on. Never empty.</summary>
    public required string ActivitySummary { get; init; }

    /// <summary>When the Activity Summary last changed — drives the age indicator and tie-breaking.</summary>
    public required DateTimeOffset ActivityAt { get; init; }

    public int SubagentCount { get; init; }
}

/// <summary>
/// Orders the Roster by how much the developer is needed.
/// </summary>
/// <remarks>
/// D23. The pane has exactly one job — tell you where you are needed next — so the ordering is
/// attention-first, not alphabetical, not grouped, and not by recency. Repository, Machine and tier
/// are columns rather than structure.
/// <para>
/// This lives in Core, not in a UI layer, so the CLI and the browser cannot disagree about what
/// "most urgent" means.
/// </para>
/// </remarks>
public static class RosterOrdering
{
    /// <summary>
    /// Attention bands, most urgent first. Recency only ever breaks ties *within* a band.
    /// </summary>
    /// <remarks>
    /// Stopped sits with Errored rather than with Idle: a finished Agent is worth a glance, because
    /// it may have finished badly or be waiting to be replaced, and filing it with idle buries it.
    /// Unenrolled is always last — you cannot act on an Agent that never enrolled, so it must never
    /// outrank one you can help.
    /// </remarks>
    private static int Band(AgentStatus status) => status switch
    {
        AgentStatus.Waiting => 0,
        AgentStatus.Errored => 1,
        AgentStatus.Stopped => 1,
        AgentStatus.Busy => 2,
        AgentStatus.Idle => 3,
        AgentStatus.Unenrolled => 4,
        _ => 3,
    };

    public static IReadOnlyList<RosterEntry> Order(IEnumerable<RosterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // OrderBy is a stable sort in LINQ-to-objects, so entries identical on both keys keep their
        // input order. That stability is the point: a list that reshuffles under the cursor between
        // refreshes is unusable, and it is the bug WExpert's Recent list shipped.
        return entries
            .OrderBy(e => Band(e.Status))
            .ThenByDescending(e => e.ActivityAt)
            .ToList();
    }
}
