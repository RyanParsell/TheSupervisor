using System.Text.Json;

namespace Supervisor.Core.Roster;

/// <summary>What a transcript read yields about an Agent's current activity.</summary>
/// <param name="Summary">One line: what the Agent is doing. Never empty.</param>
/// <param name="OutstandingSubagents">Task calls issued but not yet returned.</param>
public readonly record struct ActivityDigest(string Summary, int OutstandingSubagents);

/// <summary>
/// Follows an Agent's transcript and derives its Activity Summary.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0005. Chosen over per-tool-call hooks because tailing costs the Agent nothing, while a hook
/// costs ~130–200 ms on <em>every</em> tool call — latency the developer feels all day to populate a
/// pane they consult occasionally.
/// </para>
/// <para>
/// L7: a <see cref="FileSystemWatcher"/> may nudge <see cref="Poll"/> to run sooner, but the poll is
/// the correctness mechanism. FSW coalesces and drops events under load, and a missed append would
/// silently freeze an Agent's summary — indistinguishable, in the UI, from an Agent that has stopped
/// doing anything.
/// </para>
/// <para>
/// The transcript is another product's internal format, so every parse failure degrades to the
/// prompt-derived baseline. An Agent never disappears because its transcript changed shape.
/// </para>
/// </remarks>
public sealed class TranscriptTail
{
    private readonly string _path;
    private readonly HashSet<string> _openSubagents = new(StringComparer.Ordinal);
    private long _offset;
    private string _baseline = "";
    private string? _latest;

    public TranscriptTail(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Bytes consumed so far. Only ever advances to a complete line boundary.</summary>
    public long Offset => _offset;

    /// <summary>
    /// Total bytes actually pulled off disk by this instance, across every poll.
    /// </summary>
    /// <remarks>
    /// Diagnostic, and the only way to tell incremental tailing from a full re-read each poll — the
    /// offset alone cannot, since a freshly-constructed tail ends up at the same place. A live
    /// session's transcript was measured at 5.3 MB, so the difference matters once the Roster holds
    /// a dozen of them.
    /// </remarks>
    public long BytesRead { get; private set; }

    /// <summary>
    /// Reads whatever has been appended since the last call and returns the current digest,
    /// or null if the transcript does not exist yet.
    /// </summary>
    public ActivityDigest? Poll()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        foreach (var line in ReadNewLines())
        {
            Consume(line);
        }

        // The latest activity wins; the prompt is the floor. An Agent forty minutes into a task
        // should read as what it is doing now, not as what it was asked — but it must always read
        // as something.
        var summary = _latest is { Length: > 0 } ? _latest : _baseline;

        return new ActivityDigest(summary, _openSubagents.Count);
    }

    private void Consume(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("role", out var roleElement)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var role = roleElement.GetString();

            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                switch (typeElement.GetString())
                {
                    case "text" when block.TryGetProperty("text", out var text):
                        if (role == "user")
                        {
                            // The prompt: the involuntary baseline that guarantees a summary exists.
                            _baseline = FirstLine(text.GetString());
                            _latest = null;
                        }
                        else
                        {
                            _latest = FirstLine(text.GetString());
                        }

                        break;

                    case "tool_use" when block.TryGetProperty("name", out var name):
                        var tool = name.GetString() ?? "";
                        _latest = tool.Length > 0 ? $"Running {tool}" : _latest;

                        // Task is how an Agent fans out. Outstanding ones are what the Subagent
                        // badge counts (D24).
                        if (tool == "Task" && block.TryGetProperty("id", out var useId)
                            && useId.GetString() is { Length: > 0 } openId)
                        {
                            _openSubagents.Add(openId);
                        }

                        break;

                    case "tool_result" when block.TryGetProperty("tool_use_id", out var resultId):
                        if (resultId.GetString() is { Length: > 0 } closedId)
                        {
                            _openSubagents.Remove(closedId);
                        }

                        break;
                }
            }
        }
        catch (JsonException)
        {
            // A malformed or partially-written line is skipped, never fatal.
        }
    }

    private static string FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var newline = value.IndexOfAny(['\r', '\n']);
        var line = (newline >= 0 ? value[..newline] : value).Trim();
        return line.Length > 120 ? line[..117] + "…" : line;
    }

    /// <summary>
    /// Yields complete lines appended since the last read, leaving a partial trailing line
    /// unconsumed.
    /// </summary>
    /// <remarks>
    /// Appends are not atomic: a poll can land mid-write and see half a JSON object. Advancing the
    /// offset past that half would lose the rest of the record forever, so the offset only ever
    /// moves to the last newline actually seen.
    /// </remarks>
    private IEnumerable<string> ReadNewLines()
    {
        var lines = new List<string>();

        try
        {
            using var stream = new FileStream(
                _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < _offset)
            {
                // Truncated or replaced — start over rather than reading from a stale offset.
                _offset = 0;
            }

            stream.Seek(_offset, SeekOrigin.Begin);

            using var reader = new StreamReader(stream);
            var buffer = reader.ReadToEnd();

            BytesRead += System.Text.Encoding.UTF8.GetByteCount(buffer);

            var lastNewline = buffer.LastIndexOf('\n');
            if (lastNewline < 0)
            {
                return lines;
            }

            var complete = buffer[..lastNewline];
            _offset += System.Text.Encoding.UTF8.GetByteCount(buffer[..(lastNewline + 1)]);

            foreach (var line in complete.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    lines.Add(trimmed);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked or vanished mid-read. The next poll picks up from the same offset.
        }

        return lines;
    }
}
