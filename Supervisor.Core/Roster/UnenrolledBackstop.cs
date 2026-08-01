using System.Diagnostics;
using System.Text.Json;
using Supervisor.Core.Enrollment;

namespace Supervisor.Core.Roster;

/// <summary>
/// A Claude session seen running on this Machine, as <c>claude agents --json</c> reports it.
/// </summary>
/// <remarks>
/// A sighting is not an Agent. It is evidence that a session exists — nothing here implies the
/// session ever enrolled, and nothing here can be acted on.
/// </remarks>
public sealed record ClaudeAgentSighting
{
    public required string SessionId { get; init; }

    /// <summary>Pid of the Claude session — the secondary match key when a session id has rolled.</summary>
    public required int ProcessId { get; init; }

    public required string WorkingDirectory { get; init; }

    /// <summary>Claude Code's own derived name. Never minted by us (L6).</summary>
    public required string DisplayName { get; init; }

    /// <summary><c>interactive</c> or <c>background</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>What Claude Code says about it. Recorded, not trusted, and never acted on.</summary>
    public required string ReportedStatus { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>Lists the Claude sessions running on this Machine.</summary>
public interface IAgentsCliProbe
{
    Task<IReadOnlyList<ClaudeAgentSighting>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the <c>claude agents --json</c> payload.
/// </summary>
/// <remarks>
/// Parsing is separated from spawning so the contract can be pinned by a checked-in capture. This is
/// the one place TheSupervisor depends on another product's output shape, and `--json` is a supported
/// contract — but supported is not frozen, and a silent shape change would show up as an empty column
/// rather than an error.
/// </remarks>
public static class AgentsCliContract
{
    public static IReadOnlyList<ClaudeAgentSighting> Parse(string json)
    {
        var sightings = new List<ClaudeAgentSighting>();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return sightings;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // No session id means no way to match against the registry, and a row we cannot match is
            // a row that might duplicate an Agent already listed. Dropping one entry beats that.
            if (Text(element, "sessionId") is not { Length: > 0 } sessionId)
            {
                continue;
            }

            sightings.Add(new ClaudeAgentSighting
            {
                SessionId = sessionId,
                ProcessId = Number(element, "pid"),
                WorkingDirectory = Text(element, "cwd") ?? "",
                DisplayName = Text(element, "name") ?? sessionId,
                Kind = Text(element, "kind") ?? "interactive",
                ReportedStatus = Text(element, "status") ?? "unknown",

                // Milliseconds. Reading it as seconds would put every Agent decades in the past and
                // silently wreck every age indicator on the pane.
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(Number64(element, "startedAt")),
            });
        }

        return sightings;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static long Number64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
}

/// <summary>
/// Spawns <c>claude agents --json</c>.
/// </summary>
/// <remarks>
/// Hub-side only — never on the per-session fast path (NFR-2), where a process spawn would cost more
/// than everything the shim does put together. Failures throw and the backstop degrades; there is no
/// configuration in which a missing Claude CLI is allowed to blank the Roster.
/// </remarks>
public sealed class AgentsCliProbe : IAgentsCliProbe
{
    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public AgentsCliProbe(string executable = "claude", TimeSpan? timeout = null)
    {
        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<ClaudeAgentSighting>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("agents");
        start.ArgumentList.Add("--json");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{_executable}'.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A hung CLI must not hold the Roster's refresh open.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"'{_executable} agents --json' did not finish within {_timeout}.");
        }

        return process.ExitCode == 0
            ? AgentsCliContract.Parse(await stdout)
            : throw new InvalidOperationException(
                $"'{_executable} agents --json' exited {process.ExitCode}.");
    }
}

/// <summary>
/// Reports Claude sessions running on this Machine that never enrolled.
/// </summary>
/// <remarks>
/// <para>
/// FR-4, D6. Enrollment is involuntary but not guaranteed — a session started with
/// <c>--strict-mcp-config</c>, or before <c>supervisor install</c> ran, never reaches the Hub. Without
/// this, such a session is simply absent, and the developer reads a short Roster as a small fleet.
/// A reported gap is honest; a silent omission is a lie the UI tells confidently.
/// </para>
/// <para>
/// These rows are <see cref="AgentTier.Unenrolled"/> and can never be steered. That is the point:
/// visible, honestly labelled, never controllable.
/// </para>
/// </remarks>
public sealed class UnenrolledBackstop
{
    private const string NoActivity = "Not enrolled";

    private readonly IAgentsCliProbe _probe;
    private readonly string _machineId;
    private readonly string _machineName;
    private readonly string? _sshConfigPath;

    public UnenrolledBackstop(
        IAgentsCliProbe probe, string machineId, string machineName, string? sshConfigPath = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        _probe = probe;
        _machineId = machineId;
        _machineName = machineName;
        _sshConfigPath = sshConfigPath;
    }

    public async Task<IReadOnlyList<RosterEntry>> DetectAsync(
        IEnumerable<EnrolledAgent> enrolled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrolled);

        IReadOnlyList<ClaudeAgentSighting> sightings;

        try
        {
            sightings = await _probe.ListAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Losing the backstop costs visibility of sessions that never enrolled. Letting it throw
            // would cost the whole Roster, including every Agent that did enrol.
            return [];
        }

        return Detect(sightings, enrolled);
    }

    /// <summary>
    /// Diffs an already-collected set of sightings against the enrolled set.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DetectAsync"/> so a caller that already needs the sightings for
    /// something else — the Roster assembler, which reads Status from them — spawns the CLI once per
    /// refresh rather than twice.
    /// </remarks>
    public IReadOnlyList<RosterEntry> Detect(
        IReadOnlyList<ClaudeAgentSighting> sightings, IEnumerable<EnrolledAgent> enrolled)
    {
        ArgumentNullException.ThrowIfNull(sightings);
        ArgumentNullException.ThrowIfNull(enrolled);

        var (enrolledIds, localPids) = Index(enrolled);

        var rows = new List<RosterEntry>();

        foreach (var sighting in sightings)
        {
            var agentId = $"{_machineId}/{sighting.SessionId}";

            // Two keys, because either one alone lets a duplicate through. Identity is the primary
            // match; the pid catches a session that cleared or resumed and so took a new session id
            // while staying the same enrolled process.
            if (enrolledIds.Contains(agentId) || localPids.Contains(sighting.ProcessId))
            {
                continue;
            }

            rows.Add(new RosterEntry
            {
                AgentId = agentId,
                DisplayName = sighting.DisplayName,
                RepositoryId = Repository(sighting.WorkingDirectory),
                WorkingDirectory = sighting.WorkingDirectory,
                MachineId = _machineId,
                MachineName = _machineName,
                Status = AgentStatus.Unenrolled,
                Tier = AgentTier.Unenrolled,
                ActivitySummary = NoActivity,

                // Start time is the only timestamp a sighting carries. It is truthful — how long the
                // session has been up — and it keeps the age column populated for a row that has no
                // transcript we are entitled to read.
                ActivityAt = sighting.StartedAt,
            });
        }

        return rows;
    }

    /// <summary>
    /// Indexes the enrolled set by Machine-qualified identity, and by pid for this Machine only.
    /// </summary>
    /// <remarks>
    /// Pids are only unique within a Machine. Matching them across the Fleet would let a Peer's
    /// process number suppress a genuinely unenrolled local session — a hidden gap produced by a
    /// coincidence, which is worse than no backstop at all.
    /// </remarks>
    private (HashSet<string> Ids, HashSet<int> Pids) Index(IEnumerable<EnrolledAgent> enrolled)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pids = new HashSet<int>();

        foreach (var agent in enrolled)
        {
            var registration = agent.Registration;
            ids.Add($"{registration.MachineId}/{registration.SessionId}");

            if (string.Equals(registration.MachineId, _machineId, StringComparison.Ordinal))
            {
                pids.Add(registration.ProcessId);
            }
        }

        return (ids, pids);
    }

    private string Repository(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return $"{_machineId}:unknown";
        }

        try
        {
            return RepositoryResolver.Resolve(workingDirectory, _machineId, _sshConfigPath).Id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A path we cannot read still identifies the session well enough to show it.
            return $"{_machineId}:{workingDirectory}";
        }
    }
}
