using Supervisor.Core.Roster;

namespace Supervisor.Tests.Roster;

public sealed class TranscriptTailTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-transcript-" + Guid.NewGuid().ToString("n"));

    private readonly string _path;

    public TranscriptTailTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "session.jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Append(params string[] records) =>
        File.AppendAllLines(_path, records);

    // Shapes captured from a real transcript, so a format change surfaces as a failing test rather
    // than as a blank column in the Roster. Built by concatenation rather than raw interpolated
    // strings: the nested braces make the `$` counting a trap with no upside.
    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string UserText(string text) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":"
        + Json(text) + "}]}}";

    private static string AssistantToolUse(string tool, string id) =>
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":"
        + Json(id) + ",\"name\":" + Json(tool) + ",\"input\":{}}]}}";

    private static string AssistantText(string text) =>
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":"
        + Json(text) + "}]}}";

    private static string ToolResult(string id) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":"
        + Json(id) + "}]}}";

    [Fact]
    public void DerivesActivityFromLatestToolUse()
    {
        // What the Agent is doing right now is its most recent tool call — not what it was asked
        // forty minutes ago, which is all the prompt baseline can tell you.
        Append(
            UserText("refactor the enrollment path"),
            AssistantToolUse("Read", "t1"),
            AssistantToolUse("Edit", "t2"));

        var digest = new TranscriptTail(_path).Poll();

        Assert.NotNull(digest);
        Assert.Contains("Edit", digest.Value.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CountsOutstandingSubagentTasks()
    {
        // D24: Subagents roll up onto the parent's row as a count. Outstanding means issued and not
        // yet returned — a finished Task is not something you are waiting on.
        Append(
            UserText("fan out the review"),
            AssistantToolUse("Task", "a1"),
            AssistantToolUse("Task", "a2"),
            AssistantToolUse("Task", "a3"),
            ToolResult("a2"));

        var digest = new TranscriptTail(_path).Poll();

        Assert.Equal(2, digest!.Value.OutstandingSubagents);
    }

    [Fact]
    public void PrefersAssistantTextOverAToolNameWhenItIsTheLatestThing()
    {
        // Once the tools stop and the Agent is talking, what it says is a better summary than the
        // last tool it happened to call.
        Append(
            UserText("what changed?"),
            AssistantToolUse("Grep", "t1"),
            ToolResult("t1"),
            AssistantText("Three files changed, all in the enrollment path."));

        var digest = new TranscriptTail(_path).Poll();

        Assert.Contains("Three files changed", digest!.Value.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DegradesToPromptBaselineOnParseFailure()
    {
        // The transcript is another product's internal format. A shape change must cost detail, not
        // the Agent — a row that vanishes is far worse than one that is merely coarse.
        Append(UserText("investigate the flaky test"));
        File.AppendAllText(_path, "{ this is not json at all\n");
        File.AppendAllText(_path, "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\"}}\n");

        var digest = new TranscriptTail(_path).Poll();

        Assert.NotNull(digest);
        Assert.Contains("investigate the flaky test", digest.Value.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void PollCatchesAppendWhenWatcherEventIsDropped()
    {
        // L7's correctness claim, with no watcher in the picture at all. FSW coalesces and drops
        // events under load; if the poll did not stand alone, a missed event would freeze an
        // Agent's summary — which reads exactly like an Agent that has stopped working.
        Append(UserText("first"));
        var tail = new TranscriptTail(_path);
        tail.Poll();

        Append(AssistantToolUse("Bash", "t9"));
        var digest = tail.Poll();

        Assert.Contains("Bash", digest!.Value.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoesNotConsumeAPartiallyWrittenLine()
    {
        // Appends are not atomic. Advancing past half a record would lose the rest of it forever,
        // so the offset only moves to a real newline.
        Append(UserText("start"));
        var tail = new TranscriptTail(_path);
        tail.Poll();
        var offsetAfterComplete = tail.Offset;

        File.AppendAllText(_path, """{"type":"assistant","message":{"role":"assist""");
        tail.Poll();

        Assert.Equal(offsetAfterComplete, tail.Offset);
    }

    [Fact]
    public void RecoversWhenTheTranscriptIsTruncatedOrReplaced()
    {
        Append(UserText("original session"), AssistantToolUse("Read", "t1"));
        var tail = new TranscriptTail(_path);
        tail.Poll();

        File.WriteAllText(_path, "");
        Append(UserText("fresh session"));

        var digest = tail.Poll();

        Assert.Contains("fresh session", digest!.Value.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsNullBeforeTheTranscriptExists()
    {
        // Normal: an Agent enrols before its transcript has been written to.
        Assert.Null(new TranscriptTail(Path.Combine(_dir, "not-yet.jsonl")).Poll());
    }
}
