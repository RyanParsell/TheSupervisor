using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
        var failures = new FileFailureRecorder();

        if (probeOnly)
        {
            // Startup-budget probe: load the assembly graph, do nothing else.
            return 0;
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Enrollment runs alongside the transport, never in front of it. Blocking the stdio
        // handshake to wait for a session file would make every session slower to start — the one
        // thing the shim must never do.
        var enrollment = EnrolInBackgroundAsync(failures, shutdown.Token);

        try
        {
            await ServeAsync(shutdown.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Serves MCP over stdio for the life of the session.
    /// </summary>
    /// <remarks>
    /// No tools yet — the Fleet-listing tool is WU-F. An MCP server advertising nothing is valid,
    /// and enrollment is the job this verb exists to do.
    /// </remarks>
    private static async Task ServeAsync(CancellationToken cancellationToken)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "thesupervisor",
                Version = SupervisorVersion.Current,
            },
        };

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
