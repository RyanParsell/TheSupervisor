using Supervisor.Core.Hub;

namespace Supervisor;

/// <summary>
/// Finds this Machine's Hub, starting one if there isn't one.
/// </summary>
/// <remarks>
/// L8, D19. Extracted so the MCP shim and the session hooks — both of which run at session start and
/// both of which need a Hub — do it identically. <see cref="HubStarter"/> serializes concurrent
/// starts, so several sessions launching at once converge on one Hub rather than racing.
/// </remarks>
internal static class HubBootstrap
{
    public static Task<HubRendezvous> StartOrAttachAsync(
        HubRendezvousStore store, IHubProbe probe, CancellationToken cancellationToken) =>
        new HubStarter(store, probe, ct => SpawnAsync(store, probe, ct))
            .StartOrAttachAsync(cancellationToken);

    /// <summary>Spawns a detached Hub and waits for it to publish a usable rendezvous.</summary>
    private static async Task<HubRendezvous> SpawnAsync(
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
