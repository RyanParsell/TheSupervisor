using Supervisor.Core.Enrollment;
using Supervisor.Core.Fleet;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Fleet;

public sealed class RosterFleetQueryServiceTests : IDisposable
{
    private const string ThisMachine = "machine-a";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-fleet-" + Guid.NewGuid().ToString("n"));

    private readonly FakeTranscriptLocator _locator = new();
    private readonly FakeClock _clock = new(Start);
    private readonly AgentRegistry _registry;

    public RosterFleetQueryServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _registry = new AgentRegistry(_clock);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Register(string sessionId, int pid, string name, string cwd) =>
        _registry.Register(new AgentRegistration
        {
            SessionId = sessionId,
            ProcessId = pid,
            RepositoryId = "github.com/ryanparsell/thesupervisor",
            WorkingDirectory = cwd,
            MachineId = ThisMachine,
            MachineName = "DESKTOP-RYAN",
            DisplayName = name,
            Kind = "interactive",
            ProtocolVersion = 1,
        });

    private static ClaudeAgentSighting Sighting(string sessionId, int pid, string status) => new()
    {
        SessionId = sessionId,
        ProcessId = pid,
        DisplayName = "seen",
        WorkingDirectory = @"C:\Code",
        Kind = "interactive",
        ReportedStatus = status,
        StartedAt = Start,
    };

    private RosterFleetQueryService Build(params ClaudeAgentSighting[] sightings)
    {
        var probe = new FakeAgentsCliProbe(sightings);
        return new RosterFleetQueryService(
            new RosterAssembler(
                _registry,
                new UnenrolledBackstop(probe, ThisMachine, "DESKTOP-RYAN"),
                probe,
                _locator,
                ThisMachine,
                _clock),
            _clock);
    }

    [Fact]
    public async Task ReturnsEveryAgentInAttentionOrder()
    {
        Register("s-busy", 1001, "busy-agent", @"C:\Code\Personal\TheSupervisor");
        Register("s-waiting", 1002, "waiting-agent", @"C:\Code\Personal\TheSupervisor");

        var view = await Build(
                Sighting("s-busy", 1001, "busy"),
                Sighting("s-waiting", 1002, "waiting"))
            .QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Equal(["waiting-agent", "busy-agent"], view.Agents.Select(a => a.DisplayName));
        Assert.Equal(_clock.GetUtcNow(), view.ObservedAt);
    }

    [Fact]
    public async Task FiltersToAgentsUnderTheGivenDirectory()
    {
        Register("s-here", 1001, "here", @"C:\Code\Personal\TheSupervisor");
        Register("s-nested", 1002, "nested", @"C:\Code\Personal\TheSupervisor\WebUI");
        Register("s-elsewhere", 1003, "elsewhere", @"C:\Code\Other");

        var view = await Build(
                Sighting("s-here", 1001, "idle"),
                Sighting("s-nested", 1002, "idle"),
                Sighting("s-elsewhere", 1003, "idle"))
            .QueryAsync(
                new FleetQuery { WorkingDirectory = @"C:\Code\Personal\TheSupervisor" },
                CancellationToken.None);

        Assert.Equal(["here", "nested"], view.Agents.Select(a => a.DisplayName).Order());
    }

    [Fact]
    public async Task EmptyFleetIsNotAnError()
    {
        // A developer with nothing running asks a reasonable question and gets a truthful answer.
        // Treating "none" as a failure would make the verb useless in exactly the case where it is
        // cheapest to run.
        var view = await Build().QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Empty(view.Agents);
    }

    [Fact]
    public async Task CarriesTheMachineItAnswersFor()
    {
        // Once Peers merge rosters, a view has to say whose view it is. Establishing it now costs
        // nothing and means the wire shape does not change when federation lands.
        var view = await Build().QueryAsync(new FleetQuery(), CancellationToken.None);

        Assert.Equal(ThisMachine, view.MachineId);
    }
}
