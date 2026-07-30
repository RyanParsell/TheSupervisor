namespace Supervisor.Core;

/// <summary>
/// Identity of the Machine this process is running on.
/// </summary>
/// <param name="Id">Stable, generated once and persisted. Survives reboots and upgrades.</param>
/// <param name="DisplayName">Hostname. For humans; never used as identity.</param>
public readonly record struct MachineIdentity(string Id, string DisplayName);

/// <summary>
/// Resolves and persists this Machine's identity.
/// </summary>
/// <remarks>
/// Hostname is not identity: it changes, and two machines can briefly share one after a clone or a
/// rename. Agent names are Machine-qualified precisely because derived session names
/// (<c>thesupervisor-3b</c>) collide across boxes, so the qualifier has to be stable or the
/// collision it prevents comes back.
/// </remarks>
public static class MachineIdentityProvider
{
    public static MachineIdentity Resolve(string? stateDirectory = null)
    {
        var directory = stateDirectory ?? DefaultStateDirectory();
        var path = Path.Combine(directory, "machine-id");

        var existing = TryRead(path);
        if (existing is not null)
        {
            return new MachineIdentity(existing, Environment.MachineName);
        }

        var minted = Guid.NewGuid().ToString("n");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, minted);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // If the id cannot be persisted, enrollment still has to work. The identity is then
            // stable only for this process — degraded, not broken, and visible via doctor.
        }

        return new MachineIdentity(minted, Environment.MachineName);
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();

            // A blank or corrupt id would otherwise propagate into every Agent's identity on this
            // Machine. Treat it as absent and mint a new one.
            return value.Length == 0 ? null : value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DefaultStateDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TheSupervisor");
}
