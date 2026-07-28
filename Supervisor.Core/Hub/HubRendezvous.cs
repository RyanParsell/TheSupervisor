using System.Text.Json.Serialization;

namespace Supervisor.Core.Hub;

/// <summary>
/// The advisory record a running Hub publishes so launchers and the per-session shim can find it.
/// </summary>
/// <remarks>
/// Advisory, never authoritative: a reader must always probe the endpoint before trusting it, because
/// the file outlives a crashed Hub. <see cref="HostSecret"/> authenticates administrative calls and
/// must never be logged, returned by an API, or exposed to browser code.
/// </remarks>
public sealed record HubRendezvous
{
    /// <summary>Non-secret identity of this Hub instance. Safe to log.</summary>
    public required string HostId { get; init; }

    /// <summary>256-bit bearer secret for administrative calls. Never log this.</summary>
    public required string HostSecret { get; init; }

    public required int ProcessId { get; init; }

    /// <summary>Loopback endpoint, e.g. <c>http://127.0.0.1:51234</c>.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Build version of the Hub — informational, and what a version-skew message shows.</summary>
    public required string BuildVersion { get; init; }

    /// <summary>
    /// Enrollment contract version. This, not <see cref="BuildVersion"/>, decides whether a shim may
    /// attach (D28).
    /// </summary>
    public required int ProtocolVersion { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
}

[JsonSerializable(typeof(HubRendezvous))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal sealed partial class HubJsonContext : JsonSerializerContext;
