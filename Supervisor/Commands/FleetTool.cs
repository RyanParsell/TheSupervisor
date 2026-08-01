using Supervisor.Core.Fleet;

namespace Supervisor.Commands;

/// <summary>
/// The Fleet as an MCP tool: lets any Agent see what else is running, from inside its own context.
/// </summary>
/// <remarks>
/// <para>
/// The same <see cref="IFleetQueryService"/> the <c>list</c> verb uses (D14, L4), returning the same
/// serialized <see cref="FleetView"/>. An Agent and a human asking the same question get the same
/// answer — and because both go through one service and one serializer, drifting apart requires
/// deleting a seam rather than merely forgetting one.
/// </para>
/// <para>
/// Returns JSON rather than a rendered table: whatever a tool returns lands in the calling Agent's
/// context as text, so a table would cost tokens and force the Agent to parse English to answer
/// "is anything blocked" — the question this exists for.
/// </para>
/// </remarks>
public sealed class FleetTool
{
    private readonly IFleetSource _source;

    public FleetTool(IFleetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Lists every Agent this Machine knows about, most urgent first.</summary>
    /// <param name="cwd">Optional: only Agents working under this directory.</param>
    public async Task<string> ListAsync(string? cwd = null, CancellationToken cancellationToken = default)
    {
        var service = await _source.ResolveAsync(cancellationToken).ConfigureAwait(false);

        if (service is null)
        {
            // No Hub is an empty Fleet, never an error. A tool that throws puts a failure into the
            // Agent's context, where it reads as something the Agent did wrong and invites a retry
            // loop against a Hub that was never going to be there.
            return FleetJson.Serialize(new FleetView
            {
                Agents = [],
                ObservedAt = DateTimeOffset.UtcNow,
                MachineId = "",
            });
        }

        var view = await service
            .QueryAsync(new FleetQuery { WorkingDirectory = cwd }, cancellationToken)
            .ConfigureAwait(false);

        return FleetJson.Serialize(view);
    }
}
