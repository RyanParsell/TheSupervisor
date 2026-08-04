using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Supervisor.Core.Enrollment;
using Supervisor.Core.Fleet;
using Supervisor.Core.Hub;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;
using Supervisor.Web;

namespace Supervisor.Tests.Fleet;

public sealed class FleetEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Observed = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-fleet-http-" + Guid.NewGuid().ToString("n"));

    private readonly StubFleetQueryService _fleet = new();

    private HubRendezvousStore _store = null!;
    private HubHost _host = null!;

    public async Task InitializeAsync()
    {
        _store = new HubRendezvousStore(_dir);
        _host = await HubHost.StartAsync(_store, fleet: _fleet);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private HttpClient Anonymous() => new() { BaseAddress = new Uri(_host.Endpoint) };

    private HttpClient Authorized()
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _host.Rendezvous.HostSecret);
        return client;
    }

    private static RosterEntry Entry(
        string name, AgentStatus status, string cwd, AgentTier tier = AgentTier.Foreign) => new()
    {
        AgentId = $"machine-a/{name}",
        DisplayName = name,
        RepositoryId = "github.com/ryanparsell/thesupervisor",
        WorkingDirectory = cwd,
        MachineId = "machine-a",
        MachineName = "DESKTOP-RYAN",
        Status = status,
        Tier = tier,
        ActivitySummary = "Running Edit",
        ActivityAt = Observed,
        SubagentCount = 2,
    };

    [Fact]
    public async Task FleetRequiresTheHostSecret()
    {
        // The Roster names every repository the developer is working in and what each Agent is doing.
        // It is squarely inside the boundary the host secret exists to draw.
        using var client = Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/hub/fleet")).StatusCode);
    }

    [Fact]
    public async Task ServesTheAgentsTheServiceReturns()
    {
        _fleet.Agents = [Entry("waiting-agent", AgentStatus.Waiting, @"C:\Code\Personal\TheSupervisor")];

        using var client = Authorized();
        var body = await client.GetStringAsync("/hub/fleet");

        Assert.Contains("waiting-agent", body, StringComparison.Ordinal);

        // Statuses go over the wire as names, not ordinals. A reordering of the enum would silently
        // relabel every row in every script consuming --json, and nothing would fail.
        Assert.Contains("\"waiting\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PassesTheCwdFilterThroughToTheService()
    {
        using var client = Authorized();
        await client.GetStringAsync("/hub/fleet?cwd=" + Uri.EscapeDataString(@"C:\Code\Personal"));

        Assert.Equal(@"C:\Code\Personal", _fleet.LastQuery?.WorkingDirectory);
    }

    [Fact]
    public async Task TheHostSecretNeverAppearsInTheFleetPayload()
    {
        _fleet.Agents = [Entry("agent", AgentStatus.Busy, @"C:\Code")];

        using var client = Authorized();
        var body = await client.GetStringAsync("/hub/fleet");

        Assert.DoesNotContain(_host.Rendezvous.HostSecret, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheTypedClientReadsBackWhatTheHubServed()
    {
        // The round trip is where a source-generated context and a hand-written record disagree.
        // Asserting on the deserialized object, not the string, is what pins that.
        _fleet.Agents =
        [
            Entry("waiting-agent", AgentStatus.Waiting, @"C:\Code\Personal\TheSupervisor"),
            Entry("stranger", AgentStatus.Unenrolled, @"C:\Code\Other", AgentTier.Unenrolled),
        ];

        using var client = new HubClient(_host.Rendezvous);
        var view = await new HubFleetQueryService(client).QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Equal(["waiting-agent", "stranger"], view.Agents.Select(a => a.DisplayName));
        Assert.Equal(AgentStatus.Waiting, view.Agents[0].Status);
        Assert.Equal(AgentTier.Unenrolled, view.Agents[1].Tier);
        Assert.Equal(2, view.Agents[0].SubagentCount);
        Assert.Equal("machine-a", view.MachineId);
    }

    [Fact]
    public async Task AnEmptyFleetRoundTripsAsAnEmptyList()
    {
        using var client = new HubClient(_host.Rendezvous);
        var view = await new HubFleetQueryService(client).QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Empty(view.Agents);
    }

    [Fact]
    public async Task EnrollmentReachesTheFleetTheHubServes()
    {
        // The stub proves the route; this proves the Hub's own composition — that the registry
        // enrollment writes to is the registry the Roster reads from. Without it, every enrollment
        // test could pass while `supervisor list` showed an empty fleet forever.
        //
        // Enrols over HTTP rather than against the object, so the whole path is under test, and
        // supplies a fake probe: the real one spawns another product's CLI, which is unbounded in
        // latency (it timed out here under full-suite load) and absent in CI.
        using var dir = new TempDirectory();
        var store = new HubRendezvousStore(dir.Path);
        var registry = new AgentRegistry();

        await using var host = await HubHost.StartAsync(
            store,
            registry: registry,
            fleet: HubHost.BuildFleetService(registry, new FakeAgentsCliProbe(), new FakeTranscriptLocator()));

        using var http = new HttpClient { BaseAddress = new Uri(host.Endpoint) };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", host.Rendezvous.HostSecret);

        var enrolled = await http.PostAsJsonAsync(
            "/hub/agents",
            new AgentRegistration
            {
                SessionId = "s-1",
                ProcessId = 1001,
                RepositoryId = "github.com/ryanparsell/thesupervisor",
                WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
                MachineId = "machine-a",
                MachineName = "DESKTOP-RYAN",
                DisplayName = "thesupervisor-3b",
                Kind = "interactive",
                ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
            },
            EnrollmentJsonContext.Default.AgentRegistration);

        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);

        using var client = new HubClient(host.Rendezvous);
        var view = await new HubFleetQueryService(client).QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Contains(view.Agents, a => a.DisplayName == "thesupervisor-3b");
    }

    private sealed class StubFleetQueryService : IFleetQueryService
    {
        public IReadOnlyList<RosterEntry> Agents { get; set; } = [];
        public FleetQuery? LastQuery { get; private set; }

        public Task<FleetView> QueryAsync(FleetQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(new FleetView
            {
                Agents = Agents,
                ObservedAt = Observed,
                MachineId = "machine-a",
            });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Directory.CreateDirectory(Path);

        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "supervisor-fleet-real-" + Guid.NewGuid().ToString("n"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
