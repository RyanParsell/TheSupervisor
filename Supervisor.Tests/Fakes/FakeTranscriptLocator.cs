using Supervisor.Core.Roster;

namespace Supervisor.Tests.Fakes;

/// <summary>
/// Points at transcript files a test wrote, instead of at <c>~/.claude/projects</c>.
/// </summary>
public sealed class FakeTranscriptLocator : ITranscriptLocator
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    public FakeTranscriptLocator Add(string sessionId, string path)
    {
        _paths[sessionId] = path;
        return this;
    }

    public string? Locate(string workingDirectory, string sessionId) =>
        _paths.TryGetValue(sessionId, out var path) ? path : null;
}
