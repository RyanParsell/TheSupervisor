using System.Text.Json.Serialization;

namespace Supervisor.Core.Enrollment;

/// <summary>
/// What a shim tells the Hub about the Agent it belongs to.
/// </summary>
/// <remarks>
/// Sent once, at session start. Everything here is a fact the shim can establish cheaply — nothing
/// requires a process spawn or a network call, because this is assembled on the per-session fast
/// path (NFR-2).
/// </remarks>
public sealed record AgentRegistration
{
    /// <summary>Claude Code's session id — the Agent's identity within its Machine.</summary>
    public required string SessionId { get; init; }

    /// <summary>Pid of the Claude session, not of the shim.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Machine-independent repository identity, or a machine-local fallback.</summary>
    public required string RepositoryId { get; init; }

    /// <summary>Where this checkout lives. Location, not identity.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Stable Machine id. Qualifies the Agent, because derived names collide across boxes.</summary>
    public required string MachineId { get; init; }

    /// <summary>Hostname, for display.</summary>
    public required string MachineName { get; init; }

    /// <summary>
    /// Claude Code's own derived name (<c>thesupervisor-3b</c>), never minted by us (L6).
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary><c>interactive</c> or <c>background</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Enrollment contract version, checked by the Hub (D28).</summary>
    public required int ProtocolVersion { get; init; }
}

/// <summary>The Hub's answer to a registration.</summary>
public sealed record AgentRegistrationResponse
{
    /// <summary>Hub-assigned handle used for subsequent calls about this Agent.</summary>
    public required string AgentId { get; init; }

    public required int ProtocolVersion { get; init; }
}

/// <summary>Returned when a registration is refused.</summary>
public sealed record EnrollmentError
{
    public required string Error { get; init; }
    public required int ExpectedProtocolVersion { get; init; }
}

[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(AgentRegistrationResponse))]
[JsonSerializable(typeof(EnrollmentError))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class EnrollmentJsonContext : JsonSerializerContext;
