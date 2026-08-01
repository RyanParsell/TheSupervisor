using Supervisor.Core.Enrollment;

namespace Supervisor.Core.Roster;

/// <summary>Finds the transcript file belonging to an Agent.</summary>
public interface ITranscriptLocator
{
    /// <summary>The transcript path, or null if there is not one yet.</summary>
    string? Locate(string workingDirectory, string sessionId);
}

/// <summary>
/// Locates transcripts under <c>~/.claude/projects</c>.
/// </summary>
/// <remarks>
/// The layout is <c>&lt;projects&gt;/&lt;slug of cwd&gt;/&lt;sessionId&gt;.jsonl</c>, verified against a real
/// installation. The slug algorithm belongs to Claude Code and is undocumented, so the slug is a
/// fast path and the search is the correctness mechanism — the same shape as ADR-0005's watcher and
/// poll. A cwd whose slug we compute wrongly costs one directory scan, not a blank row.
/// </remarks>
public sealed class ClaudeTranscriptLocator : ITranscriptLocator
{
    private readonly string _projectsRoot;

    public ClaudeTranscriptLocator(string? projectsRoot = null) =>
        _projectsRoot = projectsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>
    /// Slugs a working directory the way Claude Code names its project directories.
    /// </summary>
    /// <remarks>
    /// Every separator and the drive colon collapse to a dash, so <c>C:\Code\Personal\TheSupervisor</c>
    /// becomes <c>C--Code-Personal-TheSupervisor</c> — the doubled dash is the colon and the first
    /// backslash, not a delimiter. Case is preserved. Deliberately no other substitutions: guessing at
    /// rules we have not observed would produce a wrong slug that silently misses, and the search
    /// fallback already covers every case this does not.
    /// </remarks>
    public static string Slug(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        return string.Create(workingDirectory.Length, workingDirectory, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] is ':' or '\\' or '/' ? '-' : source[i];
            }
        }).TrimEnd('-');
    }

    public string? Locate(string workingDirectory, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!Directory.Exists(_projectsRoot))
        {
            return null;
        }

        var file = sessionId + ".jsonl";

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var direct = Path.Combine(_projectsRoot, Slug(workingDirectory), file);
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        try
        {
            // One level deep, not a recursive sweep. The layout is exactly
            // <projects>/<slug>/<sessionId>.jsonl, so this is a handful of existence checks rather
            // than an enumeration of every transcript on the machine — which matters because an
            // Agent that has not written a transcript yet misses on *every* refresh, and a recursive
            // scan on each of those would be a background cost paid forever.
            foreach (var directory in Directory.EnumerateDirectories(_projectsRoot))
            {
                var candidate = Path.Combine(directory, file);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Builds the Roster from everything the Hub knows: the enrolled set, each Agent's transcript, and
/// the sessions running on this Machine that never enrolled.
/// </summary>
/// <remarks>
/// <para>
/// The three sources answer different questions and none of them is sufficient alone. The registry
/// says who enrolled but nothing about what they are doing; the transcript says what they are doing
/// but not whether the process is still alive; <c>claude agents --json</c> says what is alive but
/// nothing about enrollment. This is where they become one list.
/// </para>
/// <para>
/// Stateful across refreshes on purpose: the tails keep their offsets, so a refresh reads only what
/// was appended, and the last-changed timestamps are what the age indicator needs.
/// </para>
/// </remarks>
public sealed class RosterAssembler
{
    private readonly AgentRegistry _registry;
    private readonly UnenrolledBackstop _backstop;
    private readonly IAgentsCliProbe _probe;
    private readonly ITranscriptLocator _locator;
    private readonly string _machineId;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Watch> _watches = new(StringComparer.Ordinal);

    public RosterAssembler(
        AgentRegistry registry,
        UnenrolledBackstop backstop,
        IAgentsCliProbe probe,
        ITranscriptLocator locator,
        string machineId,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(backstop);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        _registry = registry;
        _backstop = backstop;
        _probe = probe;
        _locator = locator;
        _machineId = machineId;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The Machine this assembler answers for.</summary>
    public string MachineId => _machineId;

    /// <summary>Total transcript bytes read since this assembler was created. Diagnostic.</summary>
    public long TranscriptBytesRead => _watches.Values.Sum(w => w.Tail?.BytesRead ?? 0);

    public async Task<IReadOnlyList<RosterEntry>> BuildAsync(CancellationToken cancellationToken = default)
    {
        var enrolled = _registry.All();

        // One spawn per refresh, shared between Status and the backstop. Null means the probe could
        // not run at all — which is different from "it ran and saw nothing", and the difference
        // decides whether an unseen Agent is Stopped or merely unobserved.
        IReadOnlyList<ClaudeAgentSighting>? sightings = null;

        try
        {
            sightings = await _probe.ListAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
        }

        var bySession = new Dictionary<string, ClaudeAgentSighting>(StringComparer.Ordinal);
        var byProcess = new Dictionary<int, ClaudeAgentSighting>();

        foreach (var sighting in sightings ?? [])
        {
            bySession[sighting.SessionId] = sighting;
            byProcess[sighting.ProcessId] = sighting;
        }

        var rows = new List<RosterEntry>(enrolled.Count);

        foreach (var agent in enrolled)
        {
            var registration = agent.Registration;
            var watch = Refresh(agent);

            var local = string.Equals(registration.MachineId, _machineId, StringComparison.Ordinal);
            var seen = local
                && (bySession.TryGetValue(registration.SessionId, out var sighting)
                    || byProcess.TryGetValue(registration.ProcessId, out sighting))
                    ? sighting
                    : null;

            rows.Add(new RosterEntry
            {
                AgentId = agent.AgentId,
                DisplayName = registration.DisplayName,
                RepositoryId = registration.RepositoryId,
                WorkingDirectory = registration.WorkingDirectory,
                MachineId = registration.MachineId,
                MachineName = registration.MachineName,
                Status = Status(seen, probeRan: sightings is not null && local),
                Tier = AgentTier.Foreign,
                ActivitySummary = watch.Summary ?? ActivitySummary.NothingYet,
                ActivityAt = watch.ChangedAt ?? agent.EnrolledAt,
                SubagentCount = watch.Subagents,
            });
        }

        if (sightings is not null)
        {
            rows.AddRange(_backstop.Detect(sightings, enrolled));
        }

        Prune(enrolled);

        return RosterOrdering.Order(rows);
    }

    /// <summary>
    /// Decides an Agent's Status from what the probe saw.
    /// </summary>
    /// <remarks>
    /// <strong>Stopped requires positive evidence.</strong> Only a probe that ran and did not list
    /// the Agent may mark it stopped; a probe that could not run leaves it as it was. Declaring the
    /// whole fleet dead because Claude is missing from PATH would be the most misleading thing this
    /// pane could do — and it would look exactly like a real outage.
    /// </remarks>
    private static AgentStatus Status(ClaudeAgentSighting? seen, bool probeRan)
    {
        if (seen is null)
        {
            return probeRan ? AgentStatus.Stopped : AgentStatus.Idle;
        }

        return seen.ReportedStatus.ToLowerInvariant() switch
        {
            "busy" or "running" or "working" => AgentStatus.Busy,
            "waiting" or "waiting_for_input" or "blocked" => AgentStatus.Waiting,
            "error" or "errored" or "failed" => AgentStatus.Errored,
            "stopped" or "exited" => AgentStatus.Stopped,
            _ => AgentStatus.Idle,
        };
    }

    /// <summary>Polls one Agent's transcript, keeping the tail and its last-changed stamp.</summary>
    private Watch Refresh(EnrolledAgent agent)
    {
        var registration = agent.Registration;

        if (!_watches.TryGetValue(agent.AgentId, out var watch))
        {
            watch = new Watch();
            _watches[agent.AgentId] = watch;
        }

        var path = _locator.Locate(registration.WorkingDirectory, registration.SessionId);

        if (path is null)
        {
            return watch;
        }

        if (watch.Tail is null || !string.Equals(watch.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            watch.Path = path;
            watch.Tail = new TranscriptTail(path);
        }

        if (watch.Tail.Poll() is not { } digest)
        {
            return watch;
        }

        watch.Subagents = digest.OutstandingSubagents;

        // The stamp tracks the summary changing, not the Roster refreshing. Stamping every poll
        // would make every row permanently fresh and the staleness marker permanently silent.
        if (digest.Summary is { Length: > 0 } summary
            && !string.Equals(summary, watch.Summary, StringComparison.Ordinal))
        {
            watch.Summary = summary;
            watch.ChangedAt = _time.GetUtcNow();
        }

        return watch;
    }

    /// <summary>Drops tails for Agents that are no longer enrolled, so watches do not accumulate.</summary>
    private void Prune(IReadOnlyList<EnrolledAgent> enrolled)
    {
        if (_watches.Count == enrolled.Count)
        {
            return;
        }

        var live = new HashSet<string>(enrolled.Select(a => a.AgentId), StringComparer.Ordinal);

        foreach (var stale in _watches.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _watches.Remove(stale);
        }
    }

    private sealed class Watch
    {
        public string? Path { get; set; }
        public TranscriptTail? Tail { get; set; }
        public string? Summary { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public int Subagents { get; set; }
    }
}
