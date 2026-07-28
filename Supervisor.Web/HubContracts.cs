using System.Text.Json.Serialization;

namespace Supervisor.Web;

/// <summary>What <c>GET /hub/status</c> returns.</summary>
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
internal sealed partial class HubApiJsonContext : JsonSerializerContext;
