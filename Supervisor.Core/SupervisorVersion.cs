using System.Reflection;

namespace Supervisor.Core;

/// <summary>
/// The build version of TheSupervisor, shared by every assembly.
/// </summary>
/// <remarks>
/// This is the <em>build</em> version and is deliberately not the enrollment or Peer protocol
/// version (D28). Those are versioned independently and additively, so upgrading the tool does not
/// de-enrol running Agents.
/// </remarks>
public static class SupervisorVersion
{
    private static readonly string _current = Resolve();

    /// <summary>The build version, e.g. <c>0.1.0</c>. Never null or empty.</summary>
    public static string Current => _current;

    private static string Resolve()
    {
        var informational = typeof(SupervisorVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(SupervisorVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // Strip SourceLink build metadata ("0.1.0+abc123") — it is noise in `--version` output.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
