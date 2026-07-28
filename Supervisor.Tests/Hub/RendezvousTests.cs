using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Supervisor.Core.Hub;

namespace Supervisor.Tests.Hub;

public sealed class RendezvousTests : IDisposable
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
        HostSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        ProcessId = pid,
        Endpoint = "http://127.0.0.1:51234",
        BuildVersion = "0.1.0",
        ProtocolVersion = SupervisorProtocol.EnrollmentVersion,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void RoundTripsEveryField()
    {
        var store = new HubRendezvousStore(_dir);
        var written = Sample();

        store.Write(written);
        var read = store.Read();

        Assert.Equal(written, read);
    }

    [Fact]
    public void CarriesProtocolVersionSeparatelyFromBuildVersion()
    {
        // D28: a reader decides whether it may attach from ProtocolVersion, never from BuildVersion.
        // If these were ever the same field, every tool upgrade would de-enrol every running Agent.
        var store = new HubRendezvousStore(_dir);
        store.Write(Sample());

        var json = File.ReadAllText(store.FilePath);

        Assert.Contains("\"protocolVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"buildVersion\"", json, StringComparison.Ordinal);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WritesHostJsonWithUserOnlyAcl()
    {
        Assert.True(OperatingSystem.IsWindows(), "The rendezvous ACL contract is Windows-only.");

        var store = new HubRendezvousStore(_dir);
        store.Write(Sample());

        var security = new FileInfo(store.FilePath).GetAccessControl();
        var rules = security.GetAccessRules(
            includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        var me = WindowsIdentity.GetCurrent().User!;
        var granted = rules
            .Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .Distinct()
            .ToList();

        // The file carries a 256-bit bearer secret. Anyone who can read it can authenticate to the
        // Hub as an administrator, so "only this user" is the whole security property.
        Assert.Contains(me, granted);
        Assert.All(granted, sid => Assert.True(
            sid == me || sid.IsWellKnown(WellKnownSidType.LocalSystemSid),
            $"Unexpected principal granted access to the rendezvous: {sid.Translate(typeof(NTAccount))}"));

        Assert.False(security.AreAccessRulesProtected is false,
            "Inherited ACEs must be stripped — inheritance is how a parent directory silently widens access.");
    }
}
