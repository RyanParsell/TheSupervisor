using Supervisor.Core.Hub;

namespace Supervisor.Core.Fleet;

/// <summary>
/// Answers Fleet queries by asking the Hub.
/// </summary>
/// <remarks>
/// The client-side half of <see cref="IFleetQueryService"/>. The Roster lives in the Hub process —
/// it is assembled from a registry only the Hub holds — so every other surface reads it over
/// loopback. Both halves implement the same interface, which is what lets the <c>list</c> verb and
/// the MCP tool be written against one abstraction and tested against a fake.
/// </remarks>
public sealed class HubFleetQueryService : IFleetQueryService
{
    private readonly HubClient _client;

    public HubFleetQueryService(HubClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<FleetView> QueryAsync(
        FleetQuery query, CancellationToken cancellationToken = default)
    {
        var view = await _client.GetFleetAsync(query, cancellationToken).ConfigureAwait(false);

        // A Hub that stopped answering between the probe and the request is not an error worth
        // failing the verb over — the honest report is that nothing was seen.
        return view ?? new FleetView
        {
            Agents = [],
            ObservedAt = DateTimeOffset.UtcNow,
            MachineId = "",
        };
    }
}
