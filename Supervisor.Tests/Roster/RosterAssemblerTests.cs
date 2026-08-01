using Supervisor.Core.Enrollment;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Roster;

public sealed class RosterAssemblerTests : IDisposable
{
    private const string ThisMachine = "machine-a";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-roster-" + Guid.NewGuid().ToString("n"));

    private readonly FakeTranscriptLocator _locator = new();
    private readonly FakeClock _clock = new(Start);
    private readonly AgentRegistry _registry;

    public RosterAssemblerTests()
    {
        Directory.CreateDirectory(_dir);
        _registry = new AgentRegistry(_clock);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string UserText(string text) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":"
        + Json(text) + "}]}}";

    private static string AssistantToolUse(string tool, string id) =>
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":"
        + Json(id) + ",\"name\":" + Json(tool) + ",\"input\":{}}]}}";

    /// <summary>Writes a transcript for <paramref name="sessionId"/> and registers its location.</summary>
    private string Transcript(string sessionId, params string[] records)
    {
        var path = Path.Combine(_dir, sessionId + ".jsonl");
        File.AppendAllLines(path, records);
        _locator.Add(sessionId, path);
        return path;
    }

    private AgentRegistration Registration(string sessionId, int pid, string name = "thesupervisor-3b") => new()
    {
        SessionId = sessionId,
        ProcessId = pid,
        RepositoryId = "github.com/ryanparsell/thesupervisor",
        WorkingDirectory = _dir,
        MachineId = ThisMachine,
        MachineName = "DESKTOP-RYAN",
        DisplayName = name,
        Kind = "interactive",
        ProtocolVersion = 1,
    };

    private static ClaudeAgentSighting Sighting(
        string sessionId, int pid, string status = "idle", string name = "thesupervisor-3b") => new()
    {
        SessionId = sessionId,
        ProcessId = pid,
        DisplayName = name,
        WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
        Kind = "interactive",
        ReportedStatus = status,
        StartedAt = Start,
    };

    private RosterAssembler Build(IAgentsCliProbe probe) => new(
        _registry,
        new UnenrolledBackstop(probe, ThisMachine, "DESKTOP-RYAN"),
        probe,
        _locator,
        ThisMachine,
        _clock);

    [Fact]
    public async Task MergesEnrolledAgentsWithUnenrolledSessions()
    {
        // The Roster is one list over the whole Fleet, not two lists the developer has to reconcile.
        _registry.Register(Registration("s-enrolled", 1001));
        Transcript("s-enrolled", UserText("fix the flaky test"), AssistantToolUse("Edit", "t1"));

        var assembler = Build(new FakeAgentsCliProbe(
            Sighting("s-enrolled", 1001, "busy"),
            Sighting("s-stranger", 2002, "idle", "stranger-7")));

        var rows = await assembler.BuildAsync(CancellationToken.None);

        Assert.Equal(["thesupervisor-3b", "stranger-7"], rows.Select(r => r.DisplayName));
        Assert.Equal(AgentTier.Foreign, rows[0].Tier);
        Assert.Equal(AgentTier.Unenrolled, rows[1].Tier);
    }

    [Fact]
    public async Task DerivesActivitySummaryAndSubagentCountFromTheTranscript()
    {
        _registry.Register(Registration("s-1", 1001));
        Transcript("s-1",
            UserText("fan out the review"),
            AssistantToolUse("Task", "a1"),
            AssistantToolUse("Task", "a2"));

        var rows = await Build(new FakeAgentsCliProbe(Sighting("s-1", 1001, "busy")))
            .BuildAsync(CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Contains("Task", row.ActivitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, row.SubagentCount);
    }

    [Fact]
    public async Task ActivityAtAdvancesOnlyWhenTheSummaryChanges()
    {
        // The age indicator answers "has this Agent moved", so the timestamp must track the summary
        // changing, not the Roster refreshing. Stamping every poll would make every row permanently
        // fresh and the staleness marker permanently silent.
        _registry.Register(Registration("s-1", 1001));
        Transcript("s-1", UserText("start"), AssistantToolUse("Read", "t1"));

        var assembler = Build(new FakeAgentsCliProbe(Sighting("s-1", 1001, "busy")));

        var first = await assembler.BuildAsync(CancellationToken.None);
        var changedAt = first[0].ActivityAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        var unchanged = await assembler.BuildAsync(CancellationToken.None);
        Assert.Equal(changedAt, unchanged[0].ActivityAt);

        _clock.Advance(TimeSpan.FromMinutes(5));
        File.AppendAllLines(Path.Combine(_dir, "s-1.jsonl"), [AssistantToolUse("Bash", "t2")]);
        var moved = await assembler.BuildAsync(CancellationToken.None);

        Assert.Equal(_clock.GetUtcNow(), moved[0].ActivityAt);
    }

    [Fact]
    public async Task StatusForALocalAgentComesFromTheProbe()
    {
        _registry.Register(Registration("s-busy", 1001, "busy-agent"));
        _registry.Register(Registration("s-idle", 1002, "idle-agent"));
        Transcript("s-busy", UserText("work"));
        Transcript("s-idle", UserText("work"));

        var rows = await Build(new FakeAgentsCliProbe(
                Sighting("s-busy", 1001, "busy"),
                Sighting("s-idle", 1002, "idle")))
            .BuildAsync(CancellationToken.None);

        Assert.Equal(AgentStatus.Busy, rows.Single(r => r.DisplayName == "busy-agent").Status);
        Assert.Equal(AgentStatus.Idle, rows.Single(r => r.DisplayName == "idle-agent").Status);
    }

    [Fact]
    public async Task AnEnrolledAgentTheProbeNoLongerSeesReadsAsStopped()
    {
        // A session that exited without deregistering — closed window, killed terminal — must not sit
        // in the Roster looking idle and available. Shown briefly as stopped, so it does not just vanish.
        _registry.Register(Registration("s-gone", 1001));
        Transcript("s-gone", UserText("was working on this"));

        var rows = await Build(new FakeAgentsCliProbe()).BuildAsync(CancellationToken.None);

        Assert.Equal(AgentStatus.Stopped, Assert.Single(rows).Status);
    }

    [Fact]
    public async Task AFailedProbeNeverMarksTheFleetStopped()
    {
        // Absence of evidence is not evidence of absence. If the probe cannot run, every Agent is
        // still enrolled and still there — declaring the whole fleet dead because we could not ask
        // would be the single most misleading thing this pane could do.
        _registry.Register(Registration("s-1", 1001));
        Transcript("s-1", UserText("still going"));

        var rows = await Build(FakeAgentsCliProbe.Failing()).BuildAsync(CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.NotEqual(AgentStatus.Stopped, row.Status);
        Assert.Contains("still going", row.ActivitySummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAgentWithNoTranscriptYetStillGetsARowThatSaysSomething()
    {
        // Enrollment beats the first transcript write by seconds. A blank row in that window reads
        // as a broken Agent rather than a new one.
        _registry.Register(Registration("s-new", 1001));

        var rows = await Build(new FakeAgentsCliProbe(Sighting("s-new", 1001, "idle")))
            .BuildAsync(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(rows).ActivitySummary));
    }

    [Fact]
    public async Task ReadsOnlyWhatWasAppendedSinceTheLastRefresh()
    {
        // Tail state is kept across refreshes. Rebuilding it every poll would re-read every
        // transcript in full each time — measured at 5.3 MB for a single live session.
        //
        // The starting file has to be substantially larger than the append for this to discriminate:
        // with a one-record file, a full re-read and an incremental read differ by less than the size
        // of a single record, and the assertion passes either way.
        _registry.Register(Registration("s-1", 1001));
        var path = Transcript("s-1", UserText("start"));
        File.AppendAllLines(path, Enumerable.Range(0, 50).Select(i => AssistantToolUse("Read", $"t{i}")));

        var assembler = Build(new FakeAgentsCliProbe(Sighting("s-1", 1001, "busy")));
        await assembler.BuildAsync(CancellationToken.None);

        var whole = assembler.TranscriptBytesRead;
        Assert.True(whole > 1000, $"fixture too small to be meaningful: {whole} bytes");

        File.AppendAllLines(path, [AssistantToolUse("Grep", "t-last")]);
        await assembler.BuildAsync(CancellationToken.None);

        var second = assembler.TranscriptBytesRead - whole;

        Assert.True(
            second < whole / 2,
            $"second refresh read {second} bytes of a {whole}-byte transcript — that is a re-read, not a tail");
    }

    [Fact]
    public async Task ADeregisteredAgentThatIsStillRunningBecomesUnenrolled()
    {
        // Deregistration ends the enrollment, not the session. If the process is still there, the
        // honest report is a gap — the one thing it must not do is disappear while still running.
        _registry.Register(Registration("s-1", 1001));
        Transcript("s-1", UserText("work"));

        var assembler = Build(new FakeAgentsCliProbe(Sighting("s-1", 1001, "busy")));
        Assert.Equal(AgentTier.Foreign, Assert.Single(await assembler.BuildAsync(CancellationToken.None)).Tier);

        _registry.Deregister($"{ThisMachine}/s-1");

        var after = Assert.Single(await assembler.BuildAsync(CancellationToken.None));
        Assert.Equal(AgentTier.Unenrolled, after.Tier);
        Assert.Equal(AgentStatus.Unenrolled, after.Status);
    }
}
