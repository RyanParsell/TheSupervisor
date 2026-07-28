using System.Text.Json.Serialization;

namespace Supervisor.Core.Hub;

/// <summary>What <c>GET /hub/status</c> returns.</summary>
/// <remarks>
/// Lives in Core, not Web: both sides of the wire need it, and the CLI's probe and client cannot
/// reference the host assembly.
/// </remarks>
public sealed record HubStatus
{
    public required string HostId { get; init; }
    public required string BuildVersion { get; init; }
    public required int ProtocolVersion { get; init; }
    public required int AttachedClients { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>What <c>POST /hub/stop</c> returns.</summary>
public sealed record HubStopResponse
{
    /// <summary><c>stopped</c> or <c>refused-clients-attached</c>.</summary>
    public required string Outcome { get; init; }
    public required int AttachedClients { get; init; }
}

[JsonSerializable(typeof(HubStatus))]
[JsonSerializable(typeof(HubStopResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class HubApiJsonContext : JsonSerializerContext;
