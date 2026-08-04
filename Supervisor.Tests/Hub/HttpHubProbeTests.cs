using Supervisor.Core.Hub;
using Supervisor.Web;

namespace Supervisor.Tests.Hub;

public sealed class HttpHubProbeTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-tests-" + Guid.NewGuid().ToString("n"));

    private HubRendezvousStore _store = null!;
    private HubHost _host = null!;

    public async Task InitializeAsync()
    {
        _store = new HubRendezvousStore(_dir);
        _host = await HubHost.StartAsync(_store);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ReportsAliveForARunningHub()
    {
        using var probe = new HttpHubProbe();

        Assert.True(await probe.IsAliveAsync(_host.Rendezvous));
    }

    [Fact]
    public async Task ReportsDeadWhenNothingIsListening()
    {
        using var probe = new HttpHubProbe();
        var ghost = _host.Rendezvous with { Endpoint = "http://127.0.0.1:1" };

        Assert.False(await probe.IsAliveAsync(ghost));
    }

    [Fact]
    public async Task ReportsDeadForAMalformedEndpoint()
    {
        using var probe = new HttpHubProbe();
        var broken = _host.Rendezvous with { Endpoint = "not a url" };

        // A corrupt rendezvous means "no Hub", not "crash the caller who asked".
        Assert.False(await probe.IsAliveAsync(broken));
    }

    [Fact]
    public async Task ReportsDeadWhenTheSecretDoesNotMatch()
    {
        using var probe = new HttpHubProbe();
        var wrongSecret = _host.Rendezvous with { HostSecret = new string('b', 64) };

        Assert.False(await probe.IsAliveAsync(wrongSecret));
    }

    [Fact]
    public async Task ReportsDeadWhenAnotherHubHasTakenThePort()
    {
        // The rendezvous records a port, and the OS reuses ports freely. If a *different* Hub is
        // now listening there, attaching to it would silently mix two Hubs' state — so identity,
        // not reachability, is what the probe has to confirm.
        using var probe = new HttpHubProbe();
        var impostor = _host.Rendezvous with { HostId = "hub-deadbeef" };

        Assert.False(await probe.IsAliveAsync(impostor));
    }

    [Fact]
    public async Task ReportsDeadOnProtocolMismatch()
    {
        // D28: the enrollment contract is versioned separately from the build precisely so a
        // mismatch can be detected and refused. A shim that attached anyway would speak a contract
        // the Hub does not implement, and the failure would surface far from its cause.
        using var probe = new HttpHubProbe();
        var future = _host.Rendezvous with { ProtocolVersion = SupervisorProtocol.EnrollmentVersion + 1 };

        Assert.False(await probe.IsAliveAsync(future));
    }
}
