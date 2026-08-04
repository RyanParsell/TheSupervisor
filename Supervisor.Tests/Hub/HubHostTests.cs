using System.Net;
using System.Net.Http.Headers;
using Supervisor.Core.Hub;
using Supervisor.Web;

namespace Supervisor.Tests.Hub;

public sealed class HubHostTests : IAsyncLifetime
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

    private HttpClient Client() => new() { BaseAddress = new Uri(_host.Endpoint) };

    [Fact]
    public void BindsLoopbackOnly()
    {
        // The Hub must never be reachable from the network. Federation does not change this —
        // Peers talk Hub-to-Hub over their own authenticated link, never directly to an Agent.
        Assert.StartsWith("http://127.0.0.1:", _host.Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishesARendezvousOnStart()
    {
        var published = _store.Read();

        Assert.NotNull(published);
        Assert.Equal(_host.Rendezvous.HostId, published.HostId);
        Assert.Equal(_host.Endpoint, published.Endpoint);
        Assert.Equal(SupervisorProtocol.EnrollmentVersion, published.ProtocolVersion);
        Assert.Equal(64, published.HostSecret.Length);
    }

    [Fact]
    public async Task StatusRequiresTheHostSecret()
    {
        // Loopback is not authorization. Any process running as this user — and any browser page
        // that can be tricked into issuing the request — can reach 127.0.0.1, so the secret is the
        // only thing separating "on this machine" from "may control the Hub".
        using var client = Client();

        var anonymous = await client.GetAsync("/hub/status");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task StatusSucceedsWithTheHostSecret()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _host.Rendezvous.HostSecret);

        var response = await client.GetAsync("/hub/status");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_host.Rendezvous.HostId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusRejectsAWrongSecret()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new string('b', 64));

        var response = await client.GetAsync("/hub/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StopRefusesWithAttachedClientsAndSaysSo()
    {
        _host.Lifecycle.ClientAttached("agent-1");

        using var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _host.Rendezvous.HostSecret);

        var response = await client.PostAsync("/hub/stop", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("refused-clients-attached", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecretNeverAppearsInAnyResponse()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _host.Rendezvous.HostSecret);

        var body = await client.GetStringAsync("/hub/status");

        Assert.DoesNotContain(_host.Rendezvous.HostSecret, body, StringComparison.OrdinalIgnoreCase);
    }
}
