using System.Collections.Concurrent;

namespace Supervisor.Core.Enrollment;

/// <summary>
/// An Agent as the Hub knows it.
/// </summary>
public sealed record EnrolledAgent
{
    public required string AgentId { get; init; }
    public required AgentRegistration Registration { get; init; }
    public required DateTimeOffset EnrolledAt { get; init; }
}

/// <summary>
/// The Hub's live set of enrolled Agents.
/// </summary>
/// <remarks>
/// In-memory only (L10). Runtime state does not survive a Hub restart by design — an Agent whose
/// Hub restarted re-enrols when its shim reconnects, and anything persisted would just be a stale
/// copy racing the live one.
/// </remarks>
public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<string, EnrolledAgent> _agents = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public AgentRegistry(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public int Count => _agents.Count;

    /// <summary>
    /// Registers an Agent, or refreshes it if that session is already known.
    /// </summary>
    /// <remarks>
    /// Keyed by <c>machineId/sessionId</c>: session ids are unique within a Machine, and qualifying
    /// them is what stops two Machines' Agents colliding once Peers merge rosters.
    /// </remarks>
    public EnrolledAgent Register(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var agentId = $"{registration.MachineId}/{registration.SessionId}";

        return _agents.AddOrUpdate(
            agentId,
            _ => new EnrolledAgent
            {
                AgentId = agentId,
                Registration = registration,
                EnrolledAt = _time.GetUtcNow(),
            },
            // A reconnecting shim re-announces the same session. Refresh in place rather than
            // adding a second row — double-counting would misreport the fleet and, via the Hub's
            // stop policy, make it unstoppable without --force.
            (_, existing) => existing with { Registration = registration });
    }

    public bool Deregister(string agentId) => _agents.TryRemove(agentId, out _);

    public IReadOnlyList<EnrolledAgent> All() => _agents.Values.ToList();

    public EnrolledAgent? Find(string agentId) =>
        _agents.TryGetValue(agentId, out var agent) ? agent : null;
}
