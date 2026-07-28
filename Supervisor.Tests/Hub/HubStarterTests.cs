using Supervisor.Core.Hub;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Hub;

public sealed class HubStarterTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-tests-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static HubRendezvous Sample(int pid = 4242) => new()
    {
        HostId = "host-abc",
        HostSecret = new string('a', 64),
        ProcessId = pid,
        Endpoint = "http://127.0.0.1:51234",
        BuildVersion = "0.1.0",
        ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task AttachesWithoutStartingWhenAHubIsAlreadyAlive()
    {
        var store = new HubRendezvousStore(_dir);
        store.Write(Sample());
        var started = 0;
        var starter = new HubStarter(
            store,
            new FakeHubProbe(alive: true),
            _ => { Interlocked.Increment(ref started); return Task.FromResult(Sample()); });

        await starter.StartOrAttachAsync();

        Assert.Equal(0, started);
    }

    [Fact]
    public async Task ConcurrentStartsConvergeOnOneHub()
    {
        // Every Claude session's shim calls start-or-attach as it launches. Open a handful of
        // sessions at once — or a fan-out that spawns several — and this path runs concurrently by
        // default, not by accident. Without serialization each caller sees "no Hub" and starts one,
        // leaving orphaned hosts fighting over the rendezvous file.
        var store = new HubRendezvousStore(_dir);
        var started = 0;

        var starter = new HubStarter(
            store,
            new FakeHubProbe(alive: true),
            async ct =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(30, ct);          // starting a real Hub is not instantaneous
                var r = Sample();
                store.Write(r);                     // a real Hub publishes its rendezvous
                return r;
            });

        var racers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => starter.StartOrAttachAsync()))
            .ToArray();
        var results = await Task.WhenAll(racers);

        Assert.Equal(1, started);
        Assert.All(results, r => Assert.Equal("host-abc", r.HostId));
    }
}
