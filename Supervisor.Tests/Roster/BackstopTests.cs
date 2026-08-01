using Supervisor.Core.Enrollment;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Roster;

public sealed class BackstopTests
{
    private const string ThisMachine = "machine-a";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeAgentSighting Sighting(
        string sessionId,
        int pid,
        string name = "thesupervisor-40",
        string cwd = @"C:\Code\Personal\TheSupervisor",
        string status = "idle") => new()
    {
        SessionId = sessionId,
        ProcessId = pid,
        DisplayName = name,
        WorkingDirectory = cwd,
        Kind = "interactive",
        ReportedStatus = status,
        StartedAt = Start,
    };

    private static AgentRegistration Registration(string sessionId, int pid) => new()
    {
        SessionId = sessionId,
        ProcessId = pid,
        RepositoryId = "github.com/ryanparsell/thesupervisor",
        WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
        MachineId = ThisMachine,
        MachineName = "DESKTOP-RYAN",
        DisplayName = "thesupervisor-3b",
        Kind = "interactive",
        ProtocolVersion = 1,
    };

    private static UnenrolledBackstop Build(IAgentsCliProbe probe) =>
        new(probe, ThisMachine, "DESKTOP-RYAN");

    [Fact]
    public async Task LocalSessionNotEnrolledAppearsAsUnenrolled()
    {
        // FR-4. A session the shim never reached must be reported as a gap, not omitted — silence
        // would tell the developer their fleet is smaller than it is, which is the one thing the
        // Roster must never do.
        var backstop = Build(new FakeAgentsCliProbe(Sighting("s-unenrolled", 4242)));

        var rows = await backstop.DetectAsync([], CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(AgentStatus.Unenrolled, row.Status);
        Assert.Equal(AgentTier.Unenrolled, row.Tier);
        Assert.Equal("machine-a/s-unenrolled", row.AgentId);
        Assert.Equal("thesupervisor-40", row.DisplayName);
    }

    [Fact]
    public async Task EnrolledAgentIsNotDuplicatedByTheProbe()
    {
        // An enrolled Agent appears in BOTH sources. Two rows for one Agent misreports the fleet and
        // makes the duplicate uncontrollable, since only one of them carries an enrollment.
        var registry = new AgentRegistry();
        var enrolled = registry.Register(Registration("s-enrolled", 1001));

        var backstop = Build(new FakeAgentsCliProbe(
            Sighting("s-enrolled", 1001),
            Sighting("s-other", 2002)));

        var rows = await backstop.DetectAsync([enrolled], CancellationToken.None);

        Assert.Equal(["machine-a/s-other"], rows.Select(r => r.AgentId));
    }

    [Fact]
    public async Task MatchesOnMachineQualifiedSessionIdNotOnSessionIdAlone()
    {
        // Session ids are unique within a Machine, not across the Fleet. Once Peers merge rosters, a
        // bare-sessionId match would let another Machine's Agent suppress a genuinely unenrolled
        // local one — a gap hidden by a coincidence.
        var registry = new AgentRegistry();
        var elsewhere = registry.Register(Registration("s-shared", 1001) with { MachineId = "machine-b" });

        var backstop = Build(new FakeAgentsCliProbe(Sighting("s-shared", 7777)));

        var rows = await backstop.DetectAsync([elsewhere], CancellationToken.None);

        Assert.Single(rows);
    }

    [Fact]
    public async Task ASessionThatChangedItsIdIsStillMatchedByProcess()
    {
        // A session that clears or resumes keeps its process but takes a new session id, so the
        // registry's key goes stale while the Agent is still enrolled and still controllable.
        // Matching on identity alone would list it a second time as unenrolled.
        var registry = new AgentRegistry();
        var enrolled = registry.Register(Registration("s-before-clear", 3003));

        var backstop = Build(new FakeAgentsCliProbe(Sighting("s-after-clear", 3003)));

        var rows = await backstop.DetectAsync([enrolled], CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task AFailingProbeCostsTheBackstopNotTheRoster()
    {
        // The probe shells out to another product. If it is missing, unauthenticated, or changes its
        // contract, the Roster must still show every enrolled Agent — losing the backstop is a
        // degraded view; throwing here would blank the pane entirely.
        var backstop = Build(FakeAgentsCliProbe.Failing());

        var rows = await backstop.DetectAsync([], CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task RowsCarryRepositoryAndMachineIdentity()
    {
        // The Roster shows Repository and Machine as columns. An unenrolled row with neither is a
        // row the developer cannot place, which makes it noise rather than a reported gap.
        var backstop = Build(new FakeAgentsCliProbe(Sighting("s-unenrolled", 4242)));

        var row = Assert.Single(await backstop.DetectAsync([], CancellationToken.None));

        Assert.Equal(ThisMachine, row.MachineId);
        Assert.Equal("DESKTOP-RYAN", row.MachineName);
        Assert.Equal(@"C:\Code\Personal\TheSupervisor", row.WorkingDirectory);
        Assert.False(string.IsNullOrWhiteSpace(row.RepositoryId));
        Assert.False(string.IsNullOrWhiteSpace(row.ActivitySummary));
        Assert.Equal(Start, row.ActivityAt);
    }

    [Fact]
    public async Task UnenrolledRowsSortBelowEverythingActionable()
    {
        // The two halves have to compose: a backstop row is only correct if it also lands last once
        // merged with the enrolled rows.
        var backstop = Build(new FakeAgentsCliProbe(Sighting("s-unenrolled", 4242)));
        var unenrolled = await backstop.DetectAsync([], CancellationToken.None);

        var idle = new RosterEntry
        {
            AgentId = "machine-a/s-idle",
            DisplayName = "idle-agent",
            RepositoryId = "github.com/ryanparsell/thesupervisor",
            WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
            MachineId = ThisMachine,
            MachineName = "DESKTOP-RYAN",
            Status = AgentStatus.Idle,
            Tier = AgentTier.Foreign,
            ActivitySummary = "waiting for work",
            ActivityAt = Start.AddDays(-3),
        };

        var ordered = RosterOrdering.Order([.. unenrolled, idle]);

        Assert.Equal(["idle-agent", "thesupervisor-40"], ordered.Select(r => r.DisplayName));
    }
}
