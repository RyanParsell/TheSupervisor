namespace Supervisor.Core.Hub;

public enum HubStopOutcome
{
    /// <summary>The Hub is shutting down.</summary>
    Stopped,

    /// <summary>
    /// Refused: clients are attached and the caller did not force. Stopping would destroy their
    /// state without warning.
    /// </summary>
    RefusedClientsAttached,
}

/// <summary>
/// Tracks what is attached to a running Hub and decides whether it may stop.
/// </summary>
/// <remarks>
/// Deliberately separate from the HTTP surface so the rule is testable without standing up Kestrel,
/// and so exactly one place decides it — the CLI verb, an endpoint, and a future UI action all route
/// here rather than each re-deriving "is it safe to stop".
/// </remarks>
public sealed class HubLifecycle
{
    private readonly HashSet<string> _clients = [];
    private readonly Lock _gate = new();

    /// <summary>Number of currently attached clients (enrolled Agents, UI clients).</summary>
    public int AttachedClients
    {
        get { lock (_gate) { return _clients.Count; } }
    }

    public void ClientAttached(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        lock (_gate) { _clients.Add(clientId); }
    }

    public void ClientDetached(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        lock (_gate) { _clients.Remove(clientId); }
    }

    /// <summary>
    /// Decides whether the Hub may stop.
    /// </summary>
    /// <param name="force">
    /// Proceed even with clients attached. Required to be explicit because stopping destroys every
    /// attached client's state — once terminals exist (WU-3), that means killing live Agents
    /// mid-task.
    /// </param>
    public HubStopOutcome RequestStop(bool force = false)
    {
        lock (_gate)
        {
            if (_clients.Count > 0 && !force)
            {
                return HubStopOutcome.RefusedClientsAttached;
            }

            return HubStopOutcome.Stopped;
        }
    }
}
