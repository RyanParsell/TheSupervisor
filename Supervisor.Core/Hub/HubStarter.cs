namespace Supervisor.Core.Hub;

/// <summary>
/// Start-or-attach: returns the live Hub for this user, starting one only if none exists.
/// </summary>
/// <remarks>
/// Concurrency here is the normal case, not an edge case: every Claude session's shim calls this as
/// it launches, so opening several sessions at once — or a fan-out that spawns them — runs this path
/// in parallel by default. Without serialization each caller sees "no Hub" and starts one, leaving
/// orphaned hosts contending over the rendezvous file.
/// </remarks>
public sealed class HubStarter
{
    private static readonly TimeSpan _gateTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _gateRetry = TimeSpan.FromMilliseconds(25);

    private readonly HubRendezvousStore _store;
    private readonly HubLocator _locator;
    private readonly Func<CancellationToken, Task<HubRendezvous>> _start;

    public HubStarter(
        HubRendezvousStore store,
        IHubProbe probe,
        Func<CancellationToken, Task<HubRendezvous>> start)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(start);

        _store = store;
        _locator = new HubLocator(store, probe);
        _start = start;
    }

    public async Task<HubRendezvous> StartOrAttachAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: a live Hub almost always already exists, and the common case should not pay
        // for the gate.
        var existing = await _locator.TryAttachAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        _store.EnsureDirectory();
        using var gate = await AcquireGateAsync(_store.LockFilePath, cancellationToken).ConfigureAwait(false);

        // Re-check inside the gate. Whoever held it before us has very likely just started the Hub,
        // and starting a second one would be exactly the bug this gate exists to prevent.
        var again = await _locator.TryAttachAsync(cancellationToken).ConfigureAwait(false);
        if (again is not null)
        {
            return again;
        }

        return await _start(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes an exclusive lock on a file. A file lock rather than a named mutex or semaphore: it is
    /// cross-process and cross-thread without being thread-affine, so it is safe to hold across an
    /// <c>await</c> — which a <see cref="Mutex"/> is not.
    /// </summary>
    private static async Task<FileStream> AcquireGateAsync(string lockPath, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _gateTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(_gateRetry, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
