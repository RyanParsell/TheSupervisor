using Supervisor.Core;
using Supervisor.Core.Enrollment;
using Supervisor.Core.Hooks;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Hooks;

public sealed class HookTests
{
    private static readonly MachineIdentity Machine = new("machine-a", "DESKTOP-RYAN");

    // Built by serializing each value rather than interpolating into a raw string: a Windows path
    // dropped into JSON verbatim is an invalid escape (`\C`), and the payload then fails to parse
    // for a reason that has nothing to do with the code under test.
    private static string J(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string Payload(string @event, string sessionId, string cwd) =>
        "{\"session_id\":" + J(sessionId)
        + ",\"transcript_path\":" + J($@"C:\t\{sessionId}.jsonl")
        + ",\"cwd\":" + J(cwd)
        + ",\"hook_event_name\":" + J(@event)
        + ",\"permission_mode\":\"acceptEdits\"}";

    private static (HookRunner Runner, FakeAgentEnroller Enroller, FakeFailureRecorder Failures) Build()
    {
        var enroller = FakeAgentEnroller.Succeeding();
        var failures = new FakeFailureRecorder();
        return (new HookRunner(new ShimEnrollment(enroller, failures), Machine, failures), enroller, failures);
    }

    [Theory]
    [InlineData("session-start", "")]
    [InlineData("session-end", "")]
    [InlineData("session-start", "not json at all")]
    [InlineData("session-end", "{\"session_id\":null}")]
    [InlineData("unknown-event", "{}")]
    public async Task AlwaysExitsZero(string verb, string payload)
    {
        // NFR-1, D19. This runs inside a session Claude Code owns. A non-zero exit or a stack trace
        // makes TheSupervisor the reason the developer cannot work — so every path here, including
        // the ones that are genuinely broken, exits 0 and writes the reason down instead.
        var (runner, _, _) = Build();

        Assert.Equal(0, await runner.RunAsync(verb, payload, processId: 4242, CancellationToken.None));
    }

    [Fact]
    public async Task ExitsZeroWhenEnrollmentItselfThrows()
    {
        var failures = new FakeFailureRecorder();
        var runner = new HookRunner(
            new ShimEnrollment(new ThrowingEnroller(), failures), Machine, failures);

        var exit = await runner.RunAsync(
            "session-start", Payload("SessionStart", "s-1", @"C:\Code"), 4242, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.NotEmpty(failures.Records);
    }

    [Fact]
    public async Task SessionStartEnrolsFromThePayloadWithoutWaitingForTheSessionFile()
    {
        // ADR-0007 exists because the MCP shim is spawned ~3.5 s before Claude Code writes the
        // session file, so the shim polls for its own identity. The hook payload *carries* the
        // session id and cwd — so this path has no race to lose, and no wait to pay.
        var (runner, enroller, _) = Build();

        await runner.RunAsync(
            "session-start",
            Payload("SessionStart", "s-abc", @"C:\Code\Personal\TheSupervisor"),
            processId: 4242,
            CancellationToken.None);

        var registration = Assert.Single(enroller.Registrations);
        Assert.Equal("s-abc", registration.SessionId);
        Assert.Equal(4242, registration.ProcessId);
        Assert.Equal("machine-a", registration.MachineId);
        Assert.Contains("thesupervisor", registration.RepositoryId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionEndDeregistersThatExactSession()
    {
        var (runner, enroller, _) = Build();

        await runner.RunAsync(
            "session-end", Payload("SessionEnd", "s-abc", @"C:\Code"), 4242, CancellationToken.None);

        Assert.Equal(("machine-a", "s-abc"), Assert.Single(enroller.Deregistrations));
        Assert.Empty(enroller.Registrations);
    }

    [Fact]
    public async Task APayloadWithNoSessionIdDoesNothingButSaysWhy()
    {
        // Fail-open makes silence ambiguous: a hook that did nothing and a hook that worked look
        // identical from the outside. The record is the only thing that makes `doctor` able to tell
        // them apart.
        var (runner, enroller, failures) = Build();

        var exit = await runner.RunAsync("session-start", "{}", 4242, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(enroller.Registrations);
        Assert.Contains(failures.Records, r => r.Operation.StartsWith("hook", StringComparison.Ordinal));
    }

    [Fact]
    public void ParsesTheDocumentedPayloadShape()
    {
        // Captured from the hook input contract: session_id, transcript_path, cwd, hook_event_name,
        // plus permission_mode. Snake case, unlike everything else we exchange.
        var payload = HookPayload.Parse(Payload("SessionStart", "s-1", @"C:\Code\Personal"));

        Assert.NotNull(payload);
        Assert.Equal("s-1", payload.SessionId);
        Assert.Equal(@"C:\Code\Personal", payload.WorkingDirectory);
        Assert.Equal("SessionStart", payload.EventName);
    }

    [Fact]
    public void AMalformedPayloadParsesToNullRatherThanThrowing()
    {
        Assert.Null(HookPayload.Parse("{ not json"));
        Assert.Null(HookPayload.Parse(""));
        Assert.Null(HookPayload.Parse("[]"));
    }

    private sealed class ThrowingEnroller : IAgentEnroller
    {
        public Task<EnrollmentResult> EnrolAsync(
            AgentRegistration registration, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the hub exploded");

        public Task DeregisterAsync(
            string machineId, string sessionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the hub exploded");
    }
}
