using Supervisor.Core.Enrollment;

namespace Supervisor.Tests.Enrollment;

public sealed class SessionContextResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-sessions-" + Guid.NewGuid().ToString("n"));

    public SessionContextResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void WriteSession(int pid, string json) =>
        File.WriteAllText(Path.Combine(_dir, $"{pid}.json"), json);

    [Fact]
    public void ResolvesTheSessionThatSpawnedTheShim()
    {
        // Captured from a real session file, so a format change here shows up as a failing test
        // rather than as a blank column in the Roster.
        WriteSession(28868, """
            {"pid":28868,"sessionId":"cd835d81-1e1b-42f6-bc40-70e0266c8777",
             "cwd":"C:\\Code\\Personal\\TheSupervisor","startedAt":1785164721957,
             "version":"2.1.219","peerProtocol":1,"kind":"interactive","entrypoint":"cli",
             "name":"thesupervisor-3b","nameSource":"derived","status":"busy",
             "updatedAt":1785175542600,"statusUpdatedAt":1785175542600}
            """);

        var resolver = new ClaudeSessionContextResolver(_dir, () => 28868);
        var context = resolver.Resolve();

        Assert.NotNull(context);
        Assert.Equal("cd835d81-1e1b-42f6-bc40-70e0266c8777", context.SessionId);
        Assert.Equal(28868, context.ProcessId);
        Assert.Equal(@"C:\Code\Personal\TheSupervisor", context.WorkingDirectory);
        Assert.Equal("thesupervisor-3b", context.DisplayName);
        Assert.Equal("interactive", context.Kind);
    }

    [Fact]
    public void ReturnsNullWhenTheParentCannotBeIdentified()
    {
        var resolver = new ClaudeSessionContextResolver(_dir, () => null);

        Assert.Null(resolver.Resolve());
    }

    [Fact]
    public void ReturnsNullWhenNoSessionFileExists()
    {
        // Normal for a shim spawned by something that is not a Claude session — enrollment is
        // skipped, not failed.
        var resolver = new ClaudeSessionContextResolver(_dir, () => 99999);

        Assert.Null(resolver.Resolve());
    }

    [Fact]
    public void ReturnsNullRatherThanInventingASessionId()
    {
        // A Roster row with a fabricated id can never be matched against the real session, so it
        // would be worse than no row: permanently unreconcilable against `claude agents --json`.
        WriteSession(4242, """{"pid":4242,"cwd":"C:\\scratch","name":"scratch-a1"}""");

        var resolver = new ClaudeSessionContextResolver(_dir, () => 4242);

        Assert.Null(resolver.Resolve());
    }

    [Fact]
    public void SurvivesAMalformedSessionFile()
    {
        // Claude Code owns this format and may change it. A parse failure must degrade enrollment,
        // never disturb the session.
        WriteSession(4242, "{ this is not json");

        var resolver = new ClaudeSessionContextResolver(_dir, () => 4242);

        Assert.Null(resolver.Resolve());
    }

    [Fact]
    public void ToleratesMissingOptionalFields()
    {
        // Only sessionId is load-bearing. Everything else has a defensible default, so a trimmed
        // future format still enrols rather than vanishing.
        WriteSession(4242, """{"pid":4242,"sessionId":"abcdef01-2345-6789-abcd-ef0123456789"}""");

        var resolver = new ClaudeSessionContextResolver(_dir, () => 4242);
        var context = resolver.Resolve();

        Assert.NotNull(context);
        Assert.Equal("interactive", context.Kind);
        Assert.False(string.IsNullOrWhiteSpace(context.DisplayName));
    }

    [Fact]
    public void FindsItsOwnParentOnThisMachine()
    {
        // The one assertion that proves the NT query works rather than the fixtures. The test host
        // was itself spawned by something, so a parent must exist.
        var parent = ParentProcess.TryGetParentId();

        Assert.NotNull(parent);
        Assert.True(parent > 0);
        Assert.NotEqual(Environment.ProcessId, parent);
    }
}
