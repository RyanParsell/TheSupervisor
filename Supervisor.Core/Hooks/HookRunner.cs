using System.Text.Json;
using Supervisor.Core.Enrollment;

namespace Supervisor.Core.Hooks;

/// <summary>
/// What Claude Code writes to a hook's stdin.
/// </summary>
/// <remarks>
/// Snake case, unlike every other payload we exchange, because it is another product's contract.
/// Every field is optional here on purpose: a shape change must degrade the hook to doing nothing,
/// never break the session it runs inside (NFR-1).
/// </remarks>
public sealed record HookPayload
{
    public string? SessionId { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? TranscriptPath { get; init; }
    public string? EventName { get; init; }

    /// <summary>Reads a payload, or returns null if it is absent, empty, or not an object.</summary>
    public static HookPayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new HookPayload
            {
                SessionId = Text(document.RootElement, "session_id"),
                WorkingDirectory = Text(document.RootElement, "cwd"),
                TranscriptPath = Text(document.RootElement, "transcript_path"),
                EventName = Text(document.RootElement, "hook_event_name"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// Runs a lifecycle hook: enrol on session start, deregister on session end.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Always exits 0</strong> (NFR-1, D19). This runs inside a session Claude Code owns; a
/// non-zero exit or an escaped stack trace would make TheSupervisor the reason the developer cannot
/// work. Every failure is recorded instead, which is what makes <c>doctor</c> able to tell "working"
/// from "quietly broken".
/// </para>
/// <para>
/// These are session-lifecycle hooks, not per-tool-call ones. ADR-0005 rejected hooking every tool
/// call because it taxes latency the developer feels all day; these fire twice per session, so that
/// objection does not apply.
/// </para>
/// <para>
/// The payload carries the session id and cwd directly, so this path has none of the race
/// ADR-0007 works around — there is nothing to wait for and nothing to poll.
/// </para>
/// </remarks>
public sealed class HookRunner
{
    private readonly ShimEnrollment _enrollment;
    private readonly MachineIdentity _machine;
    private readonly IFailureRecorder _failures;
    private readonly string? _sshConfigPath;

    public HookRunner(
        ShimEnrollment enrollment,
        MachineIdentity machine,
        IFailureRecorder failures,
        string? sshConfigPath = null)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(failures);

        _enrollment = enrollment;
        _machine = machine;
        _failures = failures;
        _sshConfigPath = sshConfigPath;
    }

    /// <summary>Handles one hook invocation. The return value is always 0.</summary>
    public async Task<int> RunAsync(
        string verb, string? payloadJson, int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (verb is not ("session-start" or "session-end"))
            {
                _failures.Record("hook.unknown-verb", verb);
                return 0;
            }

            if (HookPayload.Parse(payloadJson) is not { } payload)
            {
                _failures.Record("hook.no-payload", $"{verb}: stdin was empty or not a JSON object");
                return 0;
            }

            if (payload.SessionId is not { Length: > 0 } sessionId)
            {
                _failures.Record("hook.no-session", $"{verb}: payload carried no session_id");
                return 0;
            }

            var session = new SessionContext
            {
                SessionId = sessionId,
                ProcessId = processId,
                WorkingDirectory = payload.WorkingDirectory ?? "",

                // L6: Claude Code's derived name is never minted by us. The payload does not carry
                // it, so this is left empty and the Roster fills it from `claude agents --json`,
                // which is the authority for what an Agent is called.
                DisplayName = "",
                Kind = "interactive",
            };

            if (verb == "session-end")
            {
                await _enrollment.DeregisterAsync(session, _machine, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            var repository = Repository(session.WorkingDirectory);
            await _enrollment.EnrolAsync(session, _machine, repository, cancellationToken).ConfigureAwait(false);

            return 0;
        }
        catch (Exception e)
        {
            // Catch-all by design, including cancellation: nothing this method can hit is worth
            // showing a developer mid-session.
            _failures.Record("hook.unexpected", $"{e.GetType().Name}: {e.Message}");
            return 0;
        }
    }

    private RepositoryIdentity Repository(string workingDirectory)
    {
        try
        {
            return string.IsNullOrWhiteSpace(workingDirectory)
                ? new RepositoryIdentity($"{_machine.Id}:unknown", workingDirectory, true)
                : RepositoryResolver.Resolve(workingDirectory, _machine.Id, _sshConfigPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new RepositoryIdentity($"{_machine.Id}:{workingDirectory}", workingDirectory, true);
        }
    }
}
