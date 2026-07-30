using Supervisor.Core.Hub;

namespace Supervisor.Core.Enrollment;

/// <summary>
/// The session facts a shim needs about the Agent that spawned it.
/// </summary>
public sealed record SessionContext
{
    public required string SessionId { get; init; }
    public required int ProcessId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
}

/// <summary>
/// Runs the enrollment half of the shim.
/// </summary>
/// <remarks>
/// <para>
/// <b>This never throws and never returns non-zero.</b> The shim is spawned by Claude Code on every
/// session start; anything it does that disturbs the session makes TheSupervisor the reason a
/// developer cannot work (NFR-1, D19). Every failure path exits 0 and is recorded instead.
/// </para>
/// </remarks>
public sealed class ShimEnrollment
{
    private readonly IAgentEnroller _enroller;
    private readonly IFailureRecorder _failures;

    public ShimEnrollment(IAgentEnroller enroller, IFailureRecorder failures)
    {
        ArgumentNullException.ThrowIfNull(enroller);
        ArgumentNullException.ThrowIfNull(failures);
        _enroller = enroller;
        _failures = failures;
    }

    /// <summary>
    /// Attempts enrollment. Returns the result for the caller's information; the caller exits 0
    /// regardless.
    /// </summary>
    public async Task<EnrollmentResult> EnrolAsync(
        SessionContext session,
        MachineIdentity machine,
        RepositoryIdentity repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var registration = new AgentRegistration
        {
            SessionId = session.SessionId,
            ProcessId = session.ProcessId,
            RepositoryId = repository.Id,
            WorkingDirectory = repository.DisplayPath,
            MachineId = machine.Id,
            MachineName = machine.DisplayName,
            DisplayName = session.DisplayName,
            Kind = session.Kind,
            ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
        };

        EnrollmentResult result;

        try
        {
            result = await _enroller.EnrolAsync(registration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Deliberately catch-all. An unexpected exception here would otherwise crash a process
            // Claude Code spawned, and the developer would see the shim's stack trace instead of
            // their session starting.
            _failures.Record("enrol", $"Unexpected: {e.GetType().Name}: {e.Message}");
            return new EnrollmentResult(EnrollmentOutcome.Refused, null, e.Message);
        }

        if (!result.Succeeded)
        {
            // The record is what makes a silent failure diagnosable. Without it, doctor has nothing
            // to report and "working" is indistinguishable from "broken".
            _failures.Record(
                result.Outcome switch
                {
                    EnrollmentOutcome.HubUnreachable => "enrol.hub-unreachable",
                    EnrollmentOutcome.ProtocolMismatch => "enrol.protocol-mismatch",
                    _ => "enrol.refused",
                },
                result.Detail ?? "(no detail)");
        }

        return result;
    }

    public Task DeregisterAsync(SessionContext session, MachineIdentity machine, CancellationToken cancellationToken = default)
    {
        try
        {
            return _enroller.DeregisterAsync(machine.Id, session.SessionId, cancellationToken);
        }
        catch (Exception e)
        {
            _failures.Record("deregister", $"Unexpected: {e.GetType().Name}: {e.Message}");
            return Task.CompletedTask;
        }
    }
}
