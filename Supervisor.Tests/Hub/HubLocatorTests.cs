using Supervisor.Core.Hub;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Hub;

public sealed class HubLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-tests-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static HubRendezvous Sample() => new()
    {
        HostId = "host-abc",
        HostSecret = new string('a', 64),
        ProcessId = 4242,
        Endpoint = "http://127.0.0.1:51234",
        BuildVersion = "0.1.0",
        ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task AttachesWhenTheProbeSaysAlive()
    {
        var store = new HubRendezvousStore(_dir);
        store.Write(Sample());
        var locator = new HubLocator(store, new FakeHubProbe(alive: true));

        var attached = await locator.TryAttachAsync();

        Assert.NotNull(attached);
        Assert.Equal("host-abc", attached.HostId);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task StaleFileRemovedWhenProbeFails()
    {
        var store = new HubRendezvousStore(_dir);
        store.Write(Sample());
        var locator = new HubLocator(store, new FakeHubProbe(alive: false));

        var attached = await locator.TryAttachAsync();

        Assert.Null(attached);

        // Deleting is the point, not a tidy-up. A rendezvous left behind by a crashed Hub keeps
        // pointing at a dead endpoint — or worse, at a port some unrelated process has since taken.
        // Every later start-or-attach would re-probe it and fail the same way.
        Assert.False(File.Exists(store.FilePath),
            "A rendezvous whose Hub failed its probe must be removed, not merely ignored.");
    }

    [Fact]
    public async Task ReturnsNullWhenThereIsNoRendezvousAtAll()
    {
        var store = new HubRendezvousStore(_dir);
        var probe = new FakeHubProbe(alive: true);
        var locator = new HubLocator(store, probe);

        var attached = await locator.TryAttachAsync();

        Assert.Null(attached);
        Assert.Empty(probe.Probed);
    }
}
