using Supervisor.Core;

namespace Supervisor.Tests;

public sealed class MachineIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-machine-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void IsStableAcrossCalls()
    {
        // Agent identity is Machine-qualified because derived session names collide across boxes.
        // If the qualifier changed per call, every enrollment would look like a different Machine
        // and the collision it exists to prevent would come straight back.
        var first = MachineIdentityProvider.Resolve(_dir);
        var second = MachineIdentityProvider.Resolve(_dir);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void PersistsAcrossProcessesViaDisk()
    {
        var first = MachineIdentityProvider.Resolve(_dir);

        // A second process — or this one after a reboot or an upgrade — must resolve the same id.
        var reread = File.ReadAllText(Path.Combine(_dir, "machine-id")).Trim();

        Assert.Equal(first.Id, reread);
    }

    [Fact]
    public void UsesHostnameForDisplayOnly()
    {
        var identity = MachineIdentityProvider.Resolve(_dir);

        Assert.Equal(Environment.MachineName, identity.DisplayName);
        Assert.NotEqual(identity.DisplayName, identity.Id);
    }

    [Fact]
    public void RegeneratesWhenTheStoredIdIsCorrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "machine-id"), "   ");

        var identity = MachineIdentityProvider.Resolve(_dir);

        // A blank or corrupt id must not propagate into every Agent's identity. Recover rather than
        // fail — enrollment has to work.
        Assert.False(string.IsNullOrWhiteSpace(identity.Id));
    }
}
