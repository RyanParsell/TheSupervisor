namespace Supervisor.Core.Hub;

/// <summary>
/// Verifies that a Hub advertised by a rendezvous file is actually alive.
/// </summary>
/// <remarks>
/// The rendezvous is advisory: it outlives a crashed Hub, and its recorded pid can be reused by an
/// unrelated process. Nothing may trust it without probing, which is why this is a seam — tests
/// substitute a fake rather than standing up a real Hub.
/// </remarks>
public interface IHubProbe
{
    Task<bool> IsAliveAsync(HubRendezvous rendezvous, CancellationToken cancellationToken = default);
}
