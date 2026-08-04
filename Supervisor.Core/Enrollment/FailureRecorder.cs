namespace Supervisor.Core.Enrollment;

/// <summary>
/// Records fast-path failures that are deliberately invisible to the user.
/// </summary>
/// <remarks>
/// <para>
/// Fail-open (NFR-1) means a broken shim exits 0 and says nothing, so a working session proves
/// nothing about enrollment. Without a record, "silent" and "broken" are indistinguishable — not
/// just to the developer, but to <c>supervisor doctor</c>, which would have nothing to report.
/// </para>
/// <para>
/// This is the other half of the fail-open contract: never disturb the session, always leave a trail.
/// </para>
/// </remarks>
public interface IFailureRecorder
{
    void Record(string operation, string detail);
}

/// <summary>Appends failures to a log under the state directory.</summary>
public sealed class FileFailureRecorder : IFailureRecorder
{
    private readonly string _path;

    public FileFailureRecorder(string? stateDirectory = null)
    {
        var directory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheSupervisor");

        _path = Path.Combine(directory, "shim.log");
    }

    public string LogPath => _path;

    public void Record(string operation, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(
                _path,
                $"{DateTimeOffset.UtcNow:O}\t{operation}\t{detail}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Recording a failure must never itself fail the session. If even the log is
            // unwritable there is nothing further to do — and nothing that justifies disturbing
            // the developer's work.
        }
    }
}
