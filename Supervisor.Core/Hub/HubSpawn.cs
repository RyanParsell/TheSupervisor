using System.Diagnostics;

namespace Supervisor.Core.Hub;

/// <summary>
/// Builds the <see cref="ProcessStartInfo"/> used to spawn a detached Hub.
/// </summary>
public static class HubSpawn
{
    /// <summary>
    /// The internal verb the detached Hub runs. Not part of the public command surface.
    /// </summary>
    public const string ServeVerb = "hub serve";

    /// <summary>
    /// Builds the start info for a detached Hub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UseShellExecute=true</c> + <c>WindowStyle=Hidden</c> is load-bearing and deliberately
    /// <em>not</em> the obvious <c>CreateNoWindow=true</c> (D16). A console-less process cannot bind
    /// a pseudoconsole child, so the naive form yields a Hub that runs fine and whose in-app terminal
    /// is silently dead — the child spawns and no output ever reaches the pipe.
    /// </para>
    /// <para>
    /// Because <c>UseShellExecute=true</c>, standard streams cannot be redirected. That is fine: the
    /// Hub talks over loopback HTTP and the rendezvous file, never stdio.
    /// </para>
    /// </remarks>
    public static ProcessStartInfo CreateStartInfo(string executablePath, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        return new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }
}
