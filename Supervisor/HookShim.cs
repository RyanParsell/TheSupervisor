using Supervisor.Core;
using Supervisor.Core.Enrollment;
using Supervisor.Core.Hooks;
using Supervisor.Core.Hub;

namespace Supervisor;

/// <summary>
/// The <c>hook</c> verb: Claude Code's session lifecycle events.
/// </summary>
/// <remarks>
/// <para>
/// On the per-session fast path (NFR-2, D10), alongside <c>mcp</c> — so nothing heavy may be
/// constructed here. It is also <strong>fail-open</strong> (NFR-1, D19): every path exits 0, and
/// anything that went wrong is written to the shim log for <c>doctor</c> to find.
/// </para>
/// <para>
/// Unlike the MCP shim, this knows the session id up front — Claude Code puts it on stdin — so it
/// has none of the race ADR-0007 works around, and nothing to wait for.
/// </para>
/// </remarks>
internal static class HookShim
{
    /// <summary>
    /// Bounded because a hook holds up the event that triggered it. A slow Hub must cost the
    /// developer a missing Roster row, never a session that hangs on startup.
    /// </summary>
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync(string[] args, bool probeOnly)
    {
        if (probeOnly)
        {
            // Startup-budget probe. Nothing above this line does I/O or builds a dependency graph,
            // which is exactly the property the budget guard exists to keep true.
            return 0;
        }

        var failures = new FileFailureRecorder();

        try
        {
            var verb = args.Length > 1 ? args[1] : "";
            var payload = await ReadStdinAsync().ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(_budget);

            var machine = MachineIdentityProvider.Resolve();
            var store = HubRendezvousStore.Default();
            using var probe = new HttpHubProbe();

            var hub = await HubBootstrap
                .StartOrAttachAsync(store, probe, timeout.Token)
                .ConfigureAwait(false);

            using var enroller = new HttpAgentEnroller(hub);
            var runner = new HookRunner(new ShimEnrollment(enroller, failures), machine, failures);

            // The pid Claude Code runs under: this process's parent. The payload names the session
            // but not the process, and the Roster matches on both.
            var processId = ParentProcess.TryGetParentId() ?? 0;

            return await runner.RunAsync(verb, payload, processId, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Catch-all, cancellation included. Nothing here is worth showing a developer
            // mid-session, and a non-zero exit from a hook is something Claude Code will surface.
            failures.Record("hook.unexpected", $"{e.GetType().Name}: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Reads the hook payload from stdin, or gives up rather than blocking.
    /// </summary>
    /// <remarks>
    /// Claude Code writes the payload and closes the pipe. If it does not — a shape change, a
    /// different caller, a developer running the verb by hand — a blocking read would hang the
    /// session, which is the one outcome fail-open exists to prevent.
    /// </remarks>
    private static async Task<string?> ReadStdinAsync()
    {
        if (Console.IsInputRedirected is false)
        {
            return null;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return await Console.In.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
