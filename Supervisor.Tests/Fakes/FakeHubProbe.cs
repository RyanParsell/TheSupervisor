using Supervisor.Core.Hub;

namespace Supervisor.Tests.Fakes;

/// <summary>
/// Stands in for a real liveness probe so tests never need a running Hub.
/// </summary>
public sealed class FakeHubProbe : IHubProbe
{
    private readonly bool _alive;

    public FakeHubProbe(bool alive) => _alive = alive;

    /// <summary>Rendezvous records this probe was asked about, in order.</summary>
    public List<HubRendezvous> Probed { get; } = [];

    public Task<bool> IsAliveAsync(HubRendezvous rendezvous, CancellationToken cancellationToken = default)
    {
        Probed.Add(rendezvous);
        return Task.FromResult(_alive);
    }
}
