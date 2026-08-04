using System.Text.Json;
using System.Text.Json.Serialization;

namespace Supervisor.Core.Fleet;

/// <summary>
/// The wire shape for the Fleet, source-generated because the Hub runs on
/// <c>CreateSlimBuilder</c> — reflection-based serialization is not available there, and an
/// unregistered type fails at runtime on the one path nobody exercises by hand.
/// </summary>
[JsonSerializable(typeof(FleetView))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class FleetJsonContext : JsonSerializerContext;

/// <summary>
/// The single rendering of a <see cref="FleetView"/> as JSON.
/// </summary>
/// <remarks>
/// The Hub's HTTP response, <c>supervisor list --json</c>, and the Fleet MCP tool all go through
/// here. Three call sites each serializing "the same" object is how three dialects appear — one
/// camel-cased, one with ordinal enums, one with an extra wrapper — and the drift is only visible
/// to whoever is parsing the odd one out. D14/L4 applied to the payload as well as the query.
/// </remarks>
public static class FleetJson
{
    public static string Serialize(FleetView view) =>
        JsonSerializer.Serialize(view, FleetJsonContext.Default.FleetView);
}
