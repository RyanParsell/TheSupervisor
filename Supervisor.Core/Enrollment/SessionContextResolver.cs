using System.Text.Json;
using System.Text.Json.Serialization;

namespace Supervisor.Core.Enrollment;

/// <summary>
/// The shape Claude Code writes to <c>~/.claude/sessions/&lt;pid&gt;.json</c>.
/// </summary>
/// <remarks>
/// An internal format of another product, read deliberately and defensively: every field is
/// optional here, and a shape change degrades enrollment rather than breaking a session (NFR-1).
/// The supported <c>claude agents --json</c> contract is what the Hub's backstop uses; this exists
/// because the shim needs the session's own identity from inside the session, cheaply.
/// </remarks>
public sealed record ClaudeSessionFile
{
    public int Pid { get; init; }
    public string? SessionId { get; init; }
    public string? Cwd { get; init; }
    public string? Name { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
}

[JsonSerializable(typeof(ClaudeSessionFile))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ClaudeSessionJsonContext : JsonSerializerContext;

/// <summary>Resolves the session that spawned this shim.</summary>
public interface ISessionContextResolver
{
    SessionContext? Resolve();

    /// <summary>
    /// Why the last <see cref="Resolve"/> returned null, for the failure record.
    /// </summary>
    /// <remarks>
    /// Fail-open means the shim cannot tell the developer anything at the time, so what it writes
    /// down is the only chance to diagnose it later. "Could not identify the session" is useless;
    /// "looked for pid 12345 at &lt;path&gt;, no file" is actionable.
    /// </remarks>
    string? LastFailureDetail { get; }
}

/// <summary>
/// Resolves session context from Claude Code's own per-session state files.
/// </summary>
public sealed class ClaudeSessionContextResolver : ISessionContextResolver
{
    private readonly string _sessionsDirectory;
    private readonly Func<int?> _parentPid;

    public ClaudeSessionContextResolver(
        string? sessionsDirectory = null,
        Func<int?>? parentPid = null)
    {
        _sessionsDirectory = sessionsDirectory ?? DefaultSessionsDirectory();
        _parentPid = parentPid ?? ParentProcess.TryGetParentId;
    }

    private static string DefaultSessionsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    public string? LastFailureDetail { get; private set; }

    /// <summary>
    /// Waits for the session file to appear, then resolves.
    /// </summary>
    /// <remarks>
    /// Claude Code spawns MCP servers <em>before</em> it writes the session's state file — measured
    /// at roughly 3.5 s on this machine. Resolving once at startup therefore fails most of the time,
    /// intermittently and silently, which would leave the Roster randomly missing rows.
    /// <para>
    /// The wait belongs on a background task, never in front of the stdio handshake: delaying that
    /// would make every session slower to start, which is exactly what NFR-1 forbids.
    /// </para>
    /// </remarks>
    public async Task<SessionContext?> ResolveAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            var resolved = Resolve();
            if (resolved is not null)
            {
                return resolved;
            }

            // A missing parent is permanent; only a missing file is worth waiting for.
            if (_parentPid() is null || DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
    }

    public SessionContext? Resolve()
    {
        LastFailureDetail = null;

        var pid = _parentPid();
        if (pid is null)
        {
            LastFailureDetail = "Could not determine the parent process id.";
            return null;
        }

        var path = Path.Combine(_sessionsDirectory, $"{pid}.json");

        try
        {
            if (!File.Exists(path))
            {
                // Name the pid and the path. A one-shot `claude -p` run, or a spawn through an
                // intermediate process, both land here and look identical without them.
                LastFailureDetail =
                    $"No session file for parent pid {pid} at '{path}'. "
                    + $"Parent chain: {DescribeParent(pid.Value)}.";
                return null;
            }

            var session = JsonSerializer.Deserialize(
                File.ReadAllText(path), ClaudeSessionJsonContext.Default.ClaudeSessionFile);

            if (session?.SessionId is null or "")
            {
                // Without a session id there is no Agent identity, and inventing one would produce a
                // Roster row that can never be matched against the real session.
                LastFailureDetail = $"Session file '{path}' carries no sessionId.";
                return null;
            }

            return new SessionContext
            {
                SessionId = session.SessionId,
                ProcessId = session.Pid != 0 ? session.Pid : pid.Value,
                WorkingDirectory = session.Cwd ?? Environment.CurrentDirectory,
                DisplayName = session.Name ?? $"session-{session.SessionId[..Math.Min(8, session.SessionId.Length)]}",
                Kind = session.Kind ?? "interactive",
            };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            LastFailureDetail = $"Reading '{path}' failed: {e.GetType().Name}: {e.Message}";
            return null;
        }
    }

    /// <summary>Best-effort description of the parent, so a wrong-parent diagnosis is possible.</summary>
    private static string DescribeParent(int pid)
    {
        try
        {
            using var parent = System.Diagnostics.Process.GetProcessById(pid);
            return $"{parent.ProcessName} (pid {pid})";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return $"pid {pid} (already exited)";
        }
    }
}
