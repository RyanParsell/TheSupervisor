using Supervisor.Core.Roster;

namespace Supervisor.Tests.Roster;

public sealed class TranscriptLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "supervisor-projects-" + Guid.NewGuid().ToString("n"));

    public TranscriptLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(@"C:\Code\Personal\TheSupervisor", "C--Code-Personal-TheSupervisor")]
    [InlineData(@"C:\Code\MS\CLI", "C--Code-MS-CLI")]
    [InlineData(@"C:\Code\Personal\perspectives", "C--Code-Personal-perspectives")]
    public void SlugMatchesClaudeCodesProjectDirectoryNaming(string workingDirectory, string expected)
    {
        // Verified against the real ~/.claude/projects on a developer Machine. Case is preserved;
        // the drive colon and every separator collapse to a dash, which is why "C:\" yields "C--".
        Assert.Equal(expected, ClaudeTranscriptLocator.Slug(workingDirectory));
    }

    [Fact]
    public void FindsTheTranscriptUnderTheSluggedDirectory()
    {
        var dir = Path.Combine(_root, "C--Code-Personal-TheSupervisor");
        Directory.CreateDirectory(dir);
        var expected = Path.Combine(dir, "s-1.jsonl");
        File.WriteAllText(expected, "");

        var found = new ClaudeTranscriptLocator(_root).Locate(@"C:\Code\Personal\TheSupervisor", "s-1");

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FallsBackToSearchingWhenTheSlugDoesNotMatch()
    {
        // The slug algorithm belongs to another product and is not documented. When it disagrees
        // with ours, a scan by session id still finds the file — the session id is the part of the
        // path we actually know. Costlier, and the reason it is the fallback rather than the path.
        var dir = Path.Combine(_root, "some-other-naming-scheme");
        Directory.CreateDirectory(dir);
        var expected = Path.Combine(dir, "s-2.jsonl");
        File.WriteAllText(expected, "");

        var found = new ClaudeTranscriptLocator(_root).Locate(@"C:\Somewhere\Else", "s-2");

        Assert.Equal(expected, found);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoTranscriptYet()
    {
        // Normal for the first seconds of a session, not an error.
        Assert.Null(new ClaudeTranscriptLocator(_root).Locate(@"C:\Code\Personal\TheSupervisor", "s-missing"));
    }

    [Fact]
    public void ReturnsNullWhenTheProjectsRootDoesNotExist()
    {
        var locator = new ClaudeTranscriptLocator(Path.Combine(_root, "not-created"));

        Assert.Null(locator.Locate(@"C:\Code\Personal\TheSupervisor", "s-1"));
    }
}
