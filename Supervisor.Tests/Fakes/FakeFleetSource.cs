using Supervisor.Commands;
using Supervisor.Core.Fleet;
using Supervisor.Core.Roster;

namespace Supervisor.Tests.Fakes;

/// <summary>
/// A Fleet source backed by a fixed set of rows, or by no Hub at all.
/// </summary>
/// <remarks>
/// Shared between the verb's tests and the MCP tool's, deliberately: the parity test is only
/// meaningful if both surfaces are driven from the <em>same</em> service returning the
/// <em>same</em> rows at the <em>same</em> observed instant.
/// </remarks>
public sealed class FakeFleetSource : IFleetSource
{
    /// <summary>Fixed so two runs of the same rows produce byte-identical payloads.</summary>
    public static readonly DateTimeOffset Observed = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IReadOnlyList<RosterEntry>? _agents;

    public FakeFleetSource(IReadOnlyList<RosterEntry> agents) => _agents = agents;

    private FakeFleetSource() => _agents = null;

    /// <summary>No Hub is running — a normal state, not a failure.</summary>
    public static FakeFleetSource NoHub() => new();

    public FleetQuery? LastQuery { get; private set; }

    public Task<IFleetQueryService?> ResolveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IFleetQueryService?>(_agents is null ? null : new Service(this));

    private sealed class Service : IFleetQueryService
    {
        private readonly FakeFleetSource _owner;

        public Service(FakeFleetSource owner) => _owner = owner;

        public Task<FleetView> QueryAsync(FleetQuery query, CancellationToken cancellationToken = default)
        {
            _owner.LastQuery = query;
            return Task.FromResult(new FleetView
            {
                Agents = _owner._agents!,
                ObservedAt = Observed,
                MachineId = "machine-a",
            });
        }
    }
}
