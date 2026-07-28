namespace Supervisor.Core.Hub;

/// <summary>
/// Version numbers for the wire contracts, deliberately separate from the build version
/// (<see cref="SupervisorVersion"/>).
/// </summary>
/// <remarks>
/// D28. If these tracked the build version, every routine tool upgrade would de-enrol every
/// running Agent until each session restarted — the supervisor would go blind precisely when you
/// had just improved it. These change only when a contract changes incompatibly, and changes are
/// kept additive so they rarely need to.
/// </remarks>
public static class SupervisorProtocol
{
    /// <summary>The contract between the per-session MCP shim and the Hub.</summary>
    public const int EnrollmentVersion = 1;

    /// <summary>The contract between two paired Hubs. Unused until federation (WU-5).</summary>
    public const int PeerVersion = 1;
}
