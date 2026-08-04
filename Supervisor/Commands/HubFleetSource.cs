using Supervisor.Core.Fleet;
using Supervisor.Core.Hub;

namespace Supervisor.Commands;

/// <summary>
/// Finds this Machine's Hub and returns a Fleet service pointed at it.
/// </summary>
/// <remarks>
/// <para>
/// Attaches only — it never starts a Hub. A listing verb that spawned a background process as a
/// side effect would make "is anything running?" unanswerable, since asking would create the answer.
/// Starting one is the shim's job, on enrollment (L8).
/// </para>
/// <para>
/// Returns null when no Hub is running, which is a normal state rather than an error.
/// </para>
/// </remarks>
public sealed class HubFleetSource : IFleetSource, IDisposable
{
    private readonly HubRendezvousStore _store;
    private HttpHubProbe? _probe;
    private HubClient? _client;

    public HubFleetSource(HubRendezvousStore? store = null) => _store = store ?? HubRendezvousStore.Default();

    public async Task<IFleetQueryService?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        _probe = new HttpHubProbe();

        // Probe-verified: the rendezvous file is advisory, and a stale one pointing at a dead
        // process would otherwise turn every listing into a timeout.
        var rendezvous = await new HubLocator(_store, _probe)
            .TryAttachAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rendezvous is null)
        {
            return null;
        }

        _client = new HubClient(rendezvous);
        return new HubFleetQueryService(_client);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _probe?.Dispose();
    }
}
