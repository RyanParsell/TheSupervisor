using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Supervisor.Core.Hub;

/// <summary>
/// Reads and writes the Hub's <c>host.json</c> rendezvous file.
/// </summary>
/// <remarks>
/// Source-generated JSON is deliberate: this type is reachable from the per-session fast path
/// (the shim reads the rendezvous on every session start), and reflection-based serialization
/// would put measurable startup cost on it (NFR-2).
/// </remarks>
public sealed class HubRendezvousStore
{
    private readonly string _directory;

    public HubRendezvousStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The default per-user location: <c>%LOCALAPPDATA%\TheSupervisor\hub</c>.</summary>
    public static HubRendezvousStore Default() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheSupervisor",
            "hub"));

    /// <summary>Full path to <c>host.json</c>. Named FilePath, not Path, so it cannot shadow <see cref="System.IO.Path"/>.</summary>
    public string FilePath => Path.Combine(_directory, "host.json");

    /// <summary>
    /// Lock file serializing start-or-attach across processes. Separate from the rendezvous so that
    /// holding the gate never blocks a reader who only wants to know whether a Hub exists.
    /// </summary>
    public string LockFilePath => Path.Combine(_directory, "hub.lock");

    /// <summary>Creates the containing directory if it does not exist.</summary>
    public void EnsureDirectory() => Directory.CreateDirectory(_directory);

    public void Write(HubRendezvous rendezvous)
    {
        ArgumentNullException.ThrowIfNull(rendezvous);
        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(rendezvous, HubJsonContext.Default.HubRendezvous);
        File.WriteAllText(FilePath, json);

        if (OperatingSystem.IsWindows())
        {
            RestrictToCurrentUser(FilePath);
        }
    }

    /// <summary>
    /// Replaces the file's DACL with one that grants only the current user and SYSTEM.
    /// </summary>
    /// <remarks>
    /// This file carries a 256-bit bearer secret that authenticates administrative calls to the Hub,
    /// so read access is equivalent to control of the Hub. Inheritance is explicitly severed —
    /// otherwise a permissive ACE on any ancestor directory silently widens access to the secret,
    /// and nothing in the code would show it.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        var info = new FileInfo(path);
        var security = info.GetAccessControl();

        // isProtected: stop inheriting. preserveInheritance: false, so inherited ACEs are dropped
        // rather than copied in as explicit ones.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule existing in security.GetAccessRules(
            includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRule(existing);
        }

        var me = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve the current Windows user SID.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null);

        security.AddAccessRule(new FileSystemAccessRule(
            me, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, AccessControlType.Allow));

        info.SetAccessControl(security);
    }

    /// <summary>Returns the record, or null when absent or unreadable.</summary>
    public HubRendezvous? Read()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, HubJsonContext.Default.HubRendezvous);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable rendezvous is indistinguishable from no Hub. Both mean
            // "start one" — never throw at a caller who only asked whether a Hub exists.
            return null;
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a rendezvous we cannot delete is still stale, and the probe catches it.
        }
    }
}
