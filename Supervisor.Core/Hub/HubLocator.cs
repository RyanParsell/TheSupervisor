namespace Supervisor.Core.Hub;

/// <summary>
/// Resolves whether a live Hub exists for this user, cleaning up after one that does not.
/// </summary>
public sealed class HubLocator
{
    private readonly HubRendezvousStore _store;
    private readonly IHubProbe _probe;

    public HubLocator(HubRendezvousStore store, IHubProbe probe)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(probe);
        _store = store;
        _probe = probe;
    }

    /// <summary>
    /// Returns the rendezvous of a live Hub, or null if there is none.
    /// </summary>
    /// <remarks>
    /// A rendezvous that fails its probe is <em>deleted</em>, not merely ignored. Leaving it behind
    /// means every later attempt re-probes a dead endpoint — or, once the OS reuses the port, probes
    /// something else entirely.
    /// </remarks>
    public async Task<HubRendezvous?> TryAttachAsync(CancellationToken cancellationToken = default)
    {
        var rendezvous = _store.Read();
        if (rendezvous is null)
        {
            return null;
        }

        var alive = await _probe.IsAliveAsync(rendezvous, cancellationToken).ConfigureAwait(false);
        if (alive)
        {
            return rendezvous;
        }

        _store.Delete();
        return null;
    }
}
