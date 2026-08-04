using Supervisor.Core;

namespace Supervisor.Tests;

public sealed class RepositoryResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "supervisor-repo-" + Guid.NewGuid().ToString("n"));

    public RepositoryResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string MakeRepo(string? remoteUrl, string subPath = "")
    {
        var work = Path.Combine(_root, subPath);
        Directory.CreateDirectory(Path.Combine(work, ".git"));

        var config = remoteUrl is null
            ? "[core]\n\trepositoryformatversion = 0\n"
            : $"[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = {remoteUrl}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n";

        File.WriteAllText(Path.Combine(work, ".git", "config"), config);
        return work;
    }

    [Theory]
    [InlineData("git@github.com:RyanParsell/TheSupervisor.git")]
    [InlineData("https://github.com/RyanParsell/TheSupervisor.git")]
    [InlineData("https://github.com/RyanParsell/TheSupervisor")]
    [InlineData("ssh://git@github.com/RyanParsell/TheSupervisor.git")]
    public void NormalizesEveryRemoteFormToOneIdentity(string remote)
    {
        // The same repository reached over SSH on one Machine and HTTPS on another must resolve to
        // one identity, or a Workstream silently splits in two the moment you switch machines.
        var work = MakeRepo(remote);

        var identity = RepositoryResolver.Resolve(work, "machine-a");

        Assert.Equal("github.com/ryanparsell/thesupervisor", identity.Id);
        Assert.False(identity.IsMachineLocal);
    }

    [Fact]
    public void TheSameRemoteResolvesIdenticallyOnDifferentMachines()
    {
        var work = MakeRepo("git@github.com:RyanParsell/TheSupervisor.git");

        var here = RepositoryResolver.Resolve(work, "machine-a");
        var there = RepositoryResolver.Resolve(work, "machine-b");

        Assert.Equal(here.Id, there.Id);
    }

    [Fact]
    public void FindsTheRemoteFromASubdirectory()
    {
        // Agents run in subdirectories constantly. Resolving only at the repo root would file the
        // same effort under two identities depending on where the session happened to start.
        MakeRepo("git@github.com:RyanParsell/TheSupervisor.git");
        var nested = Path.Combine(_root, "src", "deep");
        Directory.CreateDirectory(nested);

        var identity = RepositoryResolver.Resolve(nested, "machine-a");

        Assert.Equal("github.com/ryanparsell/thesupervisor", identity.Id);
    }

    [Fact]
    public void FallsBackToMachineAndPathWithoutARemote()
    {
        var work = MakeRepo(remoteUrl: null);

        var identity = RepositoryResolver.Resolve(work, "machine-a");

        Assert.StartsWith("machine:machine-a/", identity.Id, StringComparison.Ordinal);
        Assert.True(identity.IsMachineLocal);
    }

    [Fact]
    public void FallsBackToMachineAndPathOutsideAnyRepository()
    {
        // A scratch directory is still a place an Agent works. It simply cannot span Machines.
        Directory.CreateDirectory(_root);

        var identity = RepositoryResolver.Resolve(_root, "machine-a");

        Assert.StartsWith("machine:machine-a/", identity.Id, StringComparison.Ordinal);
        Assert.True(identity.IsMachineLocal);
    }

    [Fact]
    public void KeepsTheLocalPathAsDisplayEvenWhenIdentityIsShared()
    {
        var work = MakeRepo("git@github.com:RyanParsell/TheSupervisor.git");

        var identity = RepositoryResolver.Resolve(work, "machine-a");

        Assert.Equal(Path.GetFullPath(work), identity.DisplayPath);
    }

    [Fact]
    public void ResolvesAnSshHostAliasToItsRealHost()
    {
        // Real case from this repo: the personal GitHub account is reached through a `github-personal`
        // SSH alias, so the remote is git@github-personal:Owner/Repo.git. Taken literally that yields
        // a different identity than the same repo cloned over https://github.com/... on another
        // machine — two Workstreams for one effort, which is the exact split remote-based identity
        // was chosen to avoid.
        var sshConfig = Path.Combine(_root, "ssh-config");
        File.WriteAllText(sshConfig,
            "Host github-personal\n    HostName github.com\n    User git\n    IdentitiesOnly yes\n");

        var work = MakeRepo("git@github-personal:RyanParsell/TheSupervisor.git");

        var identity = RepositoryResolver.Resolve(work, "machine-a", sshConfig);

        Assert.Equal("github.com/ryanparsell/thesupervisor", identity.Id);
    }

    [Fact]
    public void KeepsAnUnknownHostAsWrittenWhenNoAliasMatches()
    {
        var sshConfig = Path.Combine(_root, "ssh-config");
        File.WriteAllText(sshConfig, "Host something-else\n    HostName example.com\n");

        var work = MakeRepo("git@internal-git:Team/Repo.git");

        var identity = RepositoryResolver.Resolve(work, "machine-a", sshConfig);

        Assert.Equal("internal-git/team/repo", identity.Id);
    }

    [Fact]
    public void ToleratesAMissingSshConfig()
    {
        var work = MakeRepo("git@github.com:RyanParsell/TheSupervisor.git");

        var identity = RepositoryResolver.Resolve(
            work, "machine-a", Path.Combine(_root, "does-not-exist"));

        Assert.Equal("github.com/ryanparsell/thesupervisor", identity.Id);
    }
}
