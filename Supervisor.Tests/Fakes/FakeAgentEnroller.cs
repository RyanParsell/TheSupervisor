using Supervisor.Core.Enrollment;

namespace Supervisor.Tests.Fakes;

/// <summary>Captures enrollment attempts and returns a scripted outcome.</summary>
public sealed class FakeAgentEnroller : IAgentEnroller
{
    private readonly EnrollmentResult _result;

    public FakeAgentEnroller(EnrollmentResult result) => _result = result;

    public static FakeAgentEnroller Succeeding(string agentId = "machine-a/session-1") =>
        new(new EnrollmentResult(EnrollmentOutcome.Enrolled, agentId, null));

    public static FakeAgentEnroller Failing(EnrollmentOutcome outcome, string detail) =>
        new(new EnrollmentResult(outcome, null, detail));

    public List<AgentRegistration> Registrations { get; } = [];
    public List<(string MachineId, string SessionId)> Deregistrations { get; } = [];

    public Task<EnrollmentResult> EnrolAsync(
        AgentRegistration registration, CancellationToken cancellationToken = default)
    {
        Registrations.Add(registration);
        return Task.FromResult(_result);
    }

    public Task DeregisterAsync(string machineId, string sessionId, CancellationToken cancellationToken = default)
    {
        Deregistrations.Add((machineId, sessionId));
        return Task.CompletedTask;
    }
}

/// <summary>Captures recorded failures in memory.</summary>
public sealed class FakeFailureRecorder : IFailureRecorder
{
    public List<(string Operation, string Detail)> Records { get; } = [];

    public void Record(string operation, string detail) => Records.Add((operation, detail));
}
