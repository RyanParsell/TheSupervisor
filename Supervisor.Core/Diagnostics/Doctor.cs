using Supervisor.Core.Install;

namespace Supervisor.Core.Diagnostics;

/// <summary>How a check came out.</summary>
public enum DoctorStatus
{
    /// <summary>Working.</summary>
    Ok,

    /// <summary>Not wrong, but worth knowing.</summary>
    Note,

    /// <summary>Broken, with something the developer can do about it.</summary>
    Problem,
}

/// <summary>What the Hub looked like when asked.</summary>
public enum HubHealth
{
    /// <summary>Running and answering, on a protocol we speak.</summary>
    Reachable,

    /// <summary>Not running. Normal on a machine where no session has started today.</summary>
    NotRunning,

    /// <summary>Running, but built against a different enrollment contract (D28).</summary>
    ProtocolMismatch,
}

/// <summary>One line of the report.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Status">How it came out.</param>
/// <param name="Detail">What was actually inspected — a path, a command, a count.</param>
/// <param name="Remedy">What to do about it. Required whenever the status is a problem.</param>
public readonly record struct DoctorCheck(string Name, DoctorStatus Status, string Detail, string? Remedy);

/// <summary>
/// Answers "is TheSupervisor actually working?" on this machine.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of fail-open (NFR-1, D19). A broken shim exits 0 in silence, so a Claude
/// session starting normally is <em>no evidence at all</em> that enrollment happened — the failure
/// mode and the success case look identical from the outside. Everything that goes quiet on purpose
/// has to be visible somewhere, and this is that somewhere.
/// </para>
/// <para>
/// Every problem carries a remedy. A report that says something is wrong without saying what to do
/// leaves the developer exactly where they started.
/// </para>
/// </remarks>
public sealed class Doctor
{
    /// <summary>How many recent shim failures to quote before summarizing.</summary>
    private const int RecentFailures = 3;

    private readonly IMcpRegistrar _registrar;
    private readonly string _settingsPath;
    private readonly string _shimLogPath;
    private readonly string _command;
    private readonly Func<CancellationToken, Task<HubHealth>> _hub;

    public Doctor(
        IMcpRegistrar registrar,
        string settingsPath,
        string shimLogPath,
        string command,
        Func<CancellationToken, Task<HubHealth>> hub)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(hub);

        _registrar = registrar;
        _settingsPath = settingsPath;
        _shimLogPath = shimLogPath;
        _command = command;
        _hub = hub;
    }

    public async Task<IReadOnlyList<DoctorCheck>> RunAsync(CancellationToken cancellationToken = default)
    {
        return
        [
            await McpAsync(cancellationToken).ConfigureAwait(false),
            Hooks(),
            await HubAsync(cancellationToken).ConfigureAwait(false),
            RecentShimFailures(),
        ];
    }

    private async Task<DoctorCheck> McpAsync(CancellationToken cancellationToken)
    {
        var registered = await _registrar.IsRegisteredAsync(cancellationToken).ConfigureAwait(false);

        return registered
            ? new DoctorCheck("MCP server registration", DoctorStatus.Ok, "registered at user scope", null)
            : new DoctorCheck(
                "MCP server registration",
                DoctorStatus.Problem,
                $"'{ClaudeMcpRegistrar.ServerName}' is not in `claude mcp list`",
                "Run `supervisor install`. Until then no session will enrol, and the Roster stays empty.");
    }

    private DoctorCheck Hooks()
    {
        if (!File.Exists(_settingsPath))
        {
            return new DoctorCheck(
                "Session hooks",
                DoctorStatus.Problem,
                $"{_settingsPath} does not exist",
                "Run `supervisor install` to create it.");
        }

        try
        {
            var settings = SettingsFile.Parse(File.ReadAllText(_settingsPath), _command);

            return settings.IsInstalled
                ? new DoctorCheck("Session hooks", DoctorStatus.Ok, $"SessionStart and SessionEnd in {_settingsPath}", null)
                : new DoctorCheck(
                    "Session hooks",
                    DoctorStatus.Problem,
                    $"no entries of ours in {_settingsPath}",
                    "Run `supervisor install`.");
        }
        catch (Exception e) when (e is InvalidSettingsException or IOException or UnauthorizedAccessException)
        {
            return new DoctorCheck(
                "Session hooks",
                DoctorStatus.Problem,
                $"could not read {_settingsPath}: {e.Message}",
                "Fix the file by hand — install will not write to a settings.json it cannot parse.");
        }
    }

    private async Task<DoctorCheck> HubAsync(CancellationToken cancellationToken)
    {
        var health = await _hub(cancellationToken).ConfigureAwait(false);

        return health switch
        {
            HubHealth.Reachable =>
                new DoctorCheck("Hub", DoctorStatus.Ok, "running and answering", null),

            // A Hub starts when the first session enrols, so its absence is the normal state of a
            // machine nobody has worked on today. Flagging it red would train the eye past red.
            HubHealth.NotRunning =>
                new DoctorCheck(
                    "Hub",
                    DoctorStatus.Note,
                    "not running — expected until the first session of the day enrols",
                    null),

            _ => new DoctorCheck(
                "Hub protocol",
                DoctorStatus.Problem,
                "the running Hub speaks a different enrollment version (D28)",
                "Run `supervisor hub stop`; the next session will start one on the current build."),
        };
    }

    /// <summary>
    /// Reads the shim's failure log — the only trace a fail-open failure leaves.
    /// </summary>
    private DoctorCheck RecentShimFailures()
    {
        if (!File.Exists(_shimLogPath))
        {
            return new DoctorCheck("Recent shim failures", DoctorStatus.Ok, "none recorded", null);
        }

        try
        {
            var lines = File.ReadAllLines(_shimLogPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
            {
                return new DoctorCheck("Recent shim failures", DoctorStatus.Ok, "none recorded", null);
            }

            var recent = lines.TakeLast(RecentFailures).Select(Summarize);

            return new DoctorCheck(
                "Recent shim failures",
                DoctorStatus.Note,
                $"{lines.Count} recorded in {_shimLogPath}; latest: {string.Join("; ", recent)}",
                "These are enrollment attempts that failed silently by design. If the Roster is "
                + "missing an Agent, this is where the reason is.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DoctorCheck(
                "Recent shim failures",
                DoctorStatus.Note,
                $"could not read {_shimLogPath}: {e.Message}",
                null);
        }
    }

    /// <summary>Reduces a tab-separated log line to its operation and detail.</summary>
    private static string Summarize(string line)
    {
        var parts = line.Split('\t');
        return parts.Length >= 3 ? $"{parts[1]} ({parts[2]})" : line.Trim();
    }
}
