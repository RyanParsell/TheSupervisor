using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Supervisor.Commands;
using Supervisor.Core;
using Supervisor.Core.Enrollment;
using Supervisor.Core.Hub;

namespace Supervisor;

/// <summary>
/// The per-session MCP shim: serves MCP over stdio, and enrols its Agent with the Hub in parallel.
/// </summary>
/// <remarks>
/// <para>
/// Claude Code spawns this for every session. The connection <em>is</em> enrollment (ADR-0002) —
/// nothing the model does is required, and nothing the model omits can prevent it.
/// </para>
/// <para>
/// <b>Every failure path here exits 0 in silence</b> (NFR-1, D19). A broken TheSupervisor must be
/// invisible inside a working session; failures go to the shim log instead, which is what
/// <c>supervisor doctor</c> reads and what makes "silent" distinguishable from "broken".
/// </para>
/// </remarks>
internal static class McpShim
{
    /// <summary>
    /// How long to wait for Claude Code to publish the session's state file.
    /// </summary>
    /// <remarks>
    /// Measured at roughly 3.5 s: MCP servers are spawned well before the file is written. Generous,
    /// because this waits on a background task where the only cost of patience is a Roster row
    /// appearing slightly late.
    /// </remarks>
    private static readonly TimeSpan _sessionWait = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(bool probeOnly, CancellationToken cancellationToken = default)
    {
        // Built before the probe returns, deliberately. Tool descriptors are constructed by
        // reflecting over handler signatures, and that cost is paid on every session start — so it
        // belongs inside what the startup-budget guard measures. Returning earlier would let a
        // future tool blow the budget with the guard still green.
        var options = CreateServerOptions();

        if (probeOnly)
        {
            // Startup-budget probe: load the assembly graph and build the server surface, nothing else.
            return 0;
        }

        var failures = new FileFailureRecorder();

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Enrollment runs alongside the transport, never in front of it. Blocking the stdio
        // handshake to wait for a session file would make every session slower to start — the one
        // thing the shim must never do.
        var enrollment = EnrolInBackgroundAsync(failures, shutdown.Token);

        try
        {
            await ServeAsync(options, shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            failures.Record("serve", $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
            await DeregisterAsync(await enrollment.ConfigureAwait(false), failures).ConfigureAwait(false);
        }

        return 0;
    }

    private sealed record Enrolled(SessionContext Session, MachineIdentity Machine, HubRendezvous Hub);

    private static async Task<Enrolled?> EnrolInBackgroundAsync(
        IFailureRecorder failures, CancellationToken cancellationToken)
    {
        try
        {
            var resolver = new ClaudeSessionContextResolver();
            var session = await resolver.ResolveAsync(_sessionWait, cancellationToken).ConfigureAwait(false);

            if (session is null)
            {
                failures.Record("enrol.no-session", resolver.LastFailureDetail ?? "(no detail)");
                return null;
            }

            var machine = MachineIdentityProvider.Resolve();
            var repository = RepositoryResolver.Resolve(session.WorkingDirectory, machine.Id);

            var store = HubRendezvousStore.Default();
            using var probe = new HttpHubProbe();

            // Start-or-attach (L8): the first session of the day brings the Hub up, the rest attach.
            // Serialized, so several sessions launching at once converge on one Hub.
            var starter = new HubStarter(store, probe, ct => SpawnHubAsync(store, probe, ct));
            var hub = await starter.StartOrAttachAsync(cancellationToken).ConfigureAwait(false);

            using var enroller = new HttpAgentEnroller(hub);
            var result = await new ShimEnrollment(enroller, failures)
                .EnrolAsync(session, machine, repository, cancellationToken)
                .ConfigureAwait(false);

            return result.Succeeded ? new Enrolled(session, machine, hub) : null;
        }
        catch (OperationCanceledException)
        {
            // The session ended before enrollment completed. Normal for a very short session.
            return null;
        }
        catch (Exception e)
        {
            // Catch-all by design. Anything escaping here would fault a task in a process Claude
            // Code spawned, and the developer would see it instead of their session working.
            failures.Record("enrol.unexpected", $"{e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static async Task DeregisterAsync(Enrolled? enrolled, IFailureRecorder failures)
    {
        if (enrolled is null)
        {
            return;
        }

        try
        {
            using var enroller = new HttpAgentEnroller(enrolled.Hub);
            await new ShimEnrollment(enroller, failures)
                .DeregisterAsync(enrolled.Session, enrolled.Machine, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failures.Record("deregister.unexpected", $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>The one tool this shim serves, resolved lazily so nothing is built at session start.</summary>
    private static readonly FleetTool _fleet = new(new HubFleetSource());

    /// <summary>
    /// The tool descriptor, hand-written rather than reflected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>McpServerTool.Create(delegate)</c> generates this schema by reflecting over the handler's
    /// signature — which is convenient, and measured at ~150 ms on the per-session fast path, taking
    /// the startup-budget ratio from 0.52 to 0.74 against a 0.75 limit. D10 says the fix for that is
    /// to move the dependency off the fast path, never to raise the budget.
    /// </para>
    /// <para>
    /// So the schema is written out here, and only parsed when a client actually asks for the tool
    /// list. One optional string parameter does not need a reflection pipeline to describe.
    /// </para>
    /// </remarks>
    private const string ToolSchema = """
        {
          "type": "object",
          "properties": {
            "cwd": {
              "type": "string",
              "description": "Optional. Only agents working under this directory."
            }
          },
          "required": []
        }
        """;

    private const string ToolName = "list_fleet";

    private const string ToolDescription =
        "List every Claude agent running on this machine, most urgent first (waiting, then errored "
        + "or stopped, then busy, idle, and finally sessions that never enrolled). Returns JSON: "
        + "each agent's name, repository, working directory, status, one-line activity summary, how "
        + "long since that summary changed, and its subagent count. Optionally filter to agents "
        + "working under a directory.";

    /// <summary>
    /// Describes the MCP surface this shim serves: identity plus the Fleet tool (WU-F).
    /// </summary>
    /// <remarks>
    /// Handlers rather than a tool collection, so session start pays for two delegates instead of a
    /// schema-generation pass. Both resolve the Hub lazily, per call — probing loopback at startup
    /// for a tool the Agent may never invoke would be the same mistake in a different place.
    /// </remarks>
    private static McpServerOptions CreateServerOptions() => new()
    {
        ServerInfo = new Implementation
        {
            Name = "thesupervisor",
            Version = SupervisorVersion.Current,
        },
        Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools =
                [
                    new Tool
                    {
                        Name = ToolName,
                        Title = "List the fleet",
                        Description = ToolDescription,
                        InputSchema = JsonDocument.Parse(ToolSchema).RootElement.Clone(),
                        Annotations = new ToolAnnotations
                        {
                            Title = "List the fleet",
                            ReadOnlyHint = true,
                            OpenWorldHint = false,
                        },
                    },
                ],
            }),

            CallToolHandler = async (request, cancellationToken) =>
            {
                if (request.Params?.Name != ToolName)
                {
                    return Failure($"Unknown tool '{request.Params?.Name}'.");
                }

                try
                {
                    var cwd = request.Params.Arguments is { } arguments
                        && arguments.TryGetValue("cwd", out var value)
                        && value.ValueKind == JsonValueKind.String
                            ? value.GetString()
                            : null;

                    var json = await _fleet.ListAsync(cwd, cancellationToken).ConfigureAwait(false);

                    return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // An unhandled exception here would surface inside the calling Agent's context
                    // as a tool crash, which reads as something the Agent did wrong and invites a
                    // retry loop. Say what happened instead.
                    return Failure($"Could not read the fleet: {e.Message}");
                }
            },
        },
    };

    private static CallToolResult Failure(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };

    /// <summary>
    /// Serves MCP over stdio for the life of the session.
    /// </summary>
    private static async Task ServeAsync(McpServerOptions options, CancellationToken cancellationToken)
    {
        await using var transport = new StdioServerTransport("thesupervisor");
        await using var server = McpServer.Create(transport, options);

        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Spawns a detached Hub and waits for it to publish a usable rendezvous.</summary>
    private static async Task<HubRendezvous> SpawnHubAsync(
        HubRendezvousStore store, IHubProbe probe, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine this executable's path.");

        var startInfo = HubSpawn.CreateStartInfo(executable, Environment.CurrentDirectory);
        startInfo.ArgumentList.Add("hub");
        startInfo.ArgumentList.Add("serve");

        System.Diagnostics.Process.Start(startInfo);

        // Poll the rendezvous rather than the process: a Hub is usable once it has published and
        // answers a probe, which is strictly later than "the process exists".
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var published = store.Read();
            if (published is not null && await probe.IsAliveAsync(published, cancellationToken).ConfigureAwait(false))
            {
                return published;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The Hub did not publish a usable rendezvous in time.");
    }
}
