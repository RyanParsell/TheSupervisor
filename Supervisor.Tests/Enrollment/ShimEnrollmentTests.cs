using Supervisor.Core;
using Supervisor.Core.Enrollment;
using Supervisor.Core.Hub;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Enrollment;

public sealed class ShimEnrollmentTests
{
    private static SessionContext Session() => new()
    {
        SessionId = "cd835d81-1e1b-42f6-bc40-70e0266c8777",
        ProcessId = 28868,
        WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
        DisplayName = "thesupervisor-3b",
        Kind = "interactive",
    };

    private static MachineIdentity Machine() => new("machine-a", "DESKTOP-RYAN");

    private static RepositoryIdentity Repository() =>
        new("github.com/ryanparsell/thesupervisor", @"C:\Code\Personal\TheSupervisor", IsMachineLocal: false);

    [Fact]
    public async Task ConnectRegistersAgentWithRepositoryAndMachine()
    {
        var enroller = FakeAgentEnroller.Succeeding();
        var shim = new ShimEnrollment(enroller, new FakeFailureRecorder());

        var result = await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.True(result.Succeeded);

        var sent = Assert.Single(enroller.Registrations);
        Assert.Equal("cd835d81-1e1b-42f6-bc40-70e0266c8777", sent.SessionId);
        Assert.Equal("github.com/ryanparsell/thesupervisor", sent.RepositoryId);
        Assert.Equal("machine-a", sent.MachineId);
        Assert.Equal("DESKTOP-RYAN", sent.MachineName);
        Assert.Equal(SupervisorProtocol.EnrollmentVersion, sent.ProtocolVersion);
    }

    [Fact]
    public async Task SendsClaudesOwnDisplayNameRatherThanMintingOne()
    {
        // L6: a Roster that disagrees with `claude agents` about what a session is called is worse
        // than one with no names at all.
        var enroller = FakeAgentEnroller.Succeeding();
        var shim = new ShimEnrollment(enroller, new FakeFailureRecorder());

        await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.Equal("thesupervisor-3b", Assert.Single(enroller.Registrations).DisplayName);
    }

    [Fact]
    public async Task ReportsTheProcessIdOfTheSessionNotTheShim()
    {
        // The shim is a child process. Reporting its own pid would make every Agent unmatchable
        // against `claude agents --json`, which is what the unenrolled backstop diffs against.
        var enroller = FakeAgentEnroller.Succeeding();
        var shim = new ShimEnrollment(enroller, new FakeFailureRecorder());

        await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.Equal(28868, Assert.Single(enroller.Registrations).ProcessId);
        Assert.NotEqual(Environment.ProcessId, Assert.Single(enroller.Registrations).ProcessId);
    }
}

public sealed class ShimFailOpenTests
{
    private static SessionContext Session() => new()
    {
        SessionId = "session-1",
        ProcessId = 1234,
        WorkingDirectory = @"C:\scratch",
        DisplayName = "scratch-a1",
        Kind = "interactive",
    };

    private static MachineIdentity Machine() => new("machine-a", "DESKTOP-RYAN");
    private static RepositoryIdentity Repository() => new("machine:machine-a/C:\\scratch", @"C:\scratch", true);

    [Fact]
    public async Task ExitsZeroWhenHubUnreachable()
    {
        var shim = new ShimEnrollment(
            FakeAgentEnroller.Failing(EnrollmentOutcome.HubUnreachable, "connection refused"),
            new FakeFailureRecorder());

        var result = await shim.EnrolAsync(Session(), Machine(), Repository());

        // The contract is that the session is undisturbed: no throw, and a result the caller can
        // ignore on its way to exit 0.
        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentOutcome.HubUnreachable, result.Outcome);
    }

    [Fact]
    public async Task ExitsZeroOnProtocolMismatch()
    {
        var shim = new ShimEnrollment(
            FakeAgentEnroller.Failing(EnrollmentOutcome.ProtocolMismatch, "hub speaks v2"),
            new FakeFailureRecorder());

        var result = await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.Equal(EnrollmentOutcome.ProtocolMismatch, result.Outcome);
    }

    [Fact]
    public async Task ExitsZeroWhenTheEnrollerThrowsUnexpectedly()
    {
        // Catch-all rather than a known-exception list: an unhandled exception here crashes a
        // process Claude Code spawned, and the developer sees a stack trace instead of a session.
        var shim = new ShimEnrollment(new ThrowingEnroller(), new FakeFailureRecorder());

        var result = await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FailureIsRecordedEvenThoughExitIsZero()
    {
        // The load-bearing test for fail-open. Exiting 0 silently makes a broken shim and a working
        // one observationally identical — so asserting the exit code alone would pass against a
        // component that does nothing at all. The record is what makes the failure diagnosable, and
        // what gives `supervisor doctor` something to report.
        var recorder = new FakeFailureRecorder();
        var shim = new ShimEnrollment(
            FakeAgentEnroller.Failing(EnrollmentOutcome.HubUnreachable, "connection refused"),
            recorder);

        await shim.EnrolAsync(Session(), Machine(), Repository());

        var record = Assert.Single(recorder.Records);
        Assert.Equal("enrol.hub-unreachable", record.Operation);
        Assert.Contains("connection refused", record.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsNothingOnSuccess()
    {
        // A log that fills up on the happy path is a log nobody reads.
        var recorder = new FakeFailureRecorder();
        var shim = new ShimEnrollment(FakeAgentEnroller.Succeeding(), recorder);

        await shim.EnrolAsync(Session(), Machine(), Repository());

        Assert.Empty(recorder.Records);
    }

    private sealed class ThrowingEnroller : IAgentEnroller
    {
        public Task<EnrollmentResult> EnrolAsync(AgentRegistration registration, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("something entirely unexpected");

        public Task DeregisterAsync(string machineId, string sessionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
