using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Supervisor.Core.Install;

/// <summary>Registers TheSupervisor's MCP server with Claude Code.</summary>
public interface IMcpRegistrar
{
    Task<bool> RegisterAsync(string executable, CancellationToken cancellationToken = default);
    Task<bool> UnregisterAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRegisteredAsync(CancellationToken cancellationToken = default);
}

/// <summary>One thing install did, and whether it worked.</summary>
public readonly record struct InstallStep(string Name, bool Ok, string Detail);

/// <summary>What install or uninstall did.</summary>
public sealed record InstallReport
{
    public required IReadOnlyList<InstallStep> Steps { get; init; }

    /// <summary>Whether a dry run found work to do. Meaningless outside a dry run.</summary>
    public required bool WouldChangeSettings { get; init; }

    public bool Succeeded => Steps.All(s => s.Ok);
}

/// <summary>
/// Installs and removes TheSupervisor's integration with Claude Code.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, in two places. The hooks go into <c>~/.claude/settings.json</c>, which the developer
/// maintains by hand — edited surgically by <see cref="SettingsFile"/> and backed up first. The MCP
/// registration goes through <c>claude mcp add</c> rather than into <c>~/.claude.json</c> directly,
/// because that file is Claude Code's own state — projects, history, caches — and a bug in our JSON
/// surgery there would cost far more than a hook entry.
/// </para>
/// <para>
/// Order matters: settings first, MCP second. If the second fails the developer is told, and the
/// first is repaired by re-running — install is idempotent precisely so that is a safe instruction.
/// </para>
/// </remarks>
public sealed class Installer
{
    private readonly string _settingsPath;
    private readonly string _backupDirectory;
    private readonly string _command;
    private readonly IMcpRegistrar _registrar;
    private readonly TimeProvider _time;

    public Installer(
        string settingsPath,
        string backupDirectory,
        string command,
        IMcpRegistrar registrar,
        TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(registrar);

        _settingsPath = settingsPath;
        _backupDirectory = backupDirectory;
        _command = command;
        _registrar = registrar;
        _time = time ?? TimeProvider.System;
    }

    public async Task<InstallReport> InstallAsync(
        bool dryRun, CancellationToken cancellationToken = default)
    {
        var steps = new List<InstallStep>();
        var current = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);

        SettingsFile settings;

        try
        {
            settings = SettingsFile.Parse(current, _command);
        }
        catch (InvalidSettingsException e)
        {
            // Stop before touching anything. Everything in that file is hand-written.
            steps.Add(new InstallStep("Read settings.json", false, e.Message));
            return new InstallReport { Steps = steps, WouldChangeSettings = false };
        }

        var updated = settings.Install();
        var changed = !string.Equals(updated, current, StringComparison.Ordinal);

        if (dryRun)
        {
            steps.Add(new InstallStep(
                "Hooks in settings.json",
                true,
                changed ? "would be added" : "already present — nothing to do"));
            steps.Add(new InstallStep("MCP server registration", true, "would be registered"));

            return new InstallReport { Steps = steps, WouldChangeSettings = changed };
        }

        if (changed)
        {
            try
            {
                var backup = await BackUpAsync(current, cancellationToken).ConfigureAwait(false);
                steps.Add(new InstallStep("Backed up settings.json", true, backup));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No backup means no way back, so this is where it stops.
                steps.Add(new InstallStep("Backed up settings.json", false, e.Message));
                return new InstallReport { Steps = steps, WouldChangeSettings = true };
            }

            try
            {
                await WriteSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
                steps.Add(new InstallStep("Hooks in settings.json", true, "SessionStart, SessionEnd"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                steps.Add(new InstallStep("Hooks in settings.json", false, e.Message));
                return new InstallReport { Steps = steps, WouldChangeSettings = true };
            }
        }
        else
        {
            steps.Add(new InstallStep("Hooks in settings.json", true, "already present"));
        }

        var registered = await _registrar.RegisterAsync(_command, cancellationToken).ConfigureAwait(false);

        steps.Add(new InstallStep(
            "MCP server registration",
            registered,
            registered
                ? "registered at user scope"
                : "`claude mcp add` failed — enrollment will not happen until this is fixed"));

        return new InstallReport { Steps = steps, WouldChangeSettings = changed };
    }

    public async Task<InstallReport> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<InstallStep>();
        var current = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var updated = SettingsFile.Parse(current, _command).Uninstall();

            if (string.Equals(updated, current, StringComparison.Ordinal))
            {
                steps.Add(new InstallStep("Hooks in settings.json", true, "none of ours were present"));
            }
            else
            {
                await WriteSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
                steps.Add(new InstallStep("Hooks in settings.json", true, "removed"));
            }
        }
        catch (Exception e) when (e is InvalidSettingsException or IOException or UnauthorizedAccessException)
        {
            steps.Add(new InstallStep("Hooks in settings.json", false, e.Message));
        }

        var removed = await _registrar.UnregisterAsync(cancellationToken).ConfigureAwait(false);
        steps.Add(new InstallStep(
            "MCP server registration",
            removed,
            removed ? "removed" : "`claude mcp remove` failed"));

        return new InstallReport { Steps = steps, WouldChangeSettings = false };
    }

    private async Task<string> ReadSettingsAsync(CancellationToken cancellationToken) =>
        File.Exists(_settingsPath)
            ? await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false)
            : "";

    private async Task WriteSettingsAsync(string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_settingsPath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies what is on disk to a timestamped file, before the first byte is written.
    /// </summary>
    /// <remarks>
    /// Timestamped rather than a single <c>.bak</c>: the second install would otherwise overwrite
    /// the backup of the original with a backup of our own output, which is exactly the copy nobody
    /// needs and destroys the one they do.
    /// </remarks>
    private async Task<string> BackUpAsync(string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDirectory);

        var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(_backupDirectory, $"settings.{stamp}.json");

        for (var i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(_backupDirectory, $"settings.{stamp}-{i}.json");
        }

        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }
}

/// <summary>
/// Registers the MCP server by driving <c>claude mcp</c>.
/// </summary>
/// <remarks>
/// The supported contract, deliberately chosen over editing <c>~/.claude.json</c>: that file holds
/// Claude Code's own state and is far more expensive to corrupt than anything we gain by writing it
/// ourselves.
/// </remarks>
public sealed class ClaudeMcpRegistrar : IMcpRegistrar
{
    /// <summary>The server name Claude Code will know us by.</summary>
    public const string ServerName = "thesupervisor";

    private readonly string _claude;
    private readonly TimeSpan _timeout;

    public ClaudeMcpRegistrar(string claude = "claude", TimeSpan? timeout = null)
    {
        _claude = claude;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<bool> RegisterAsync(string executable, CancellationToken cancellationToken = default)
    {
        // Registering over an existing entry fails, so removal first makes this idempotent.
        await UnregisterAsync(cancellationToken).ConfigureAwait(false);

        var (ok, _) = await RunAsync(
            ["mcp", "add", "-s", "user", ServerName, "--", executable.Trim('"'), "mcp"],
            cancellationToken).ConfigureAwait(false);

        return ok;
    }

    public async Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
    {
        // Removing something that is not there is a success: uninstall on a clean machine is a
        // perfectly reasonable thing to run.
        await RunAsync(["mcp", "remove", "-s", "user", ServerName], cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> IsRegisteredAsync(CancellationToken cancellationToken = default)
    {
        var (ok, output) = await RunAsync(["mcp", "list"], cancellationToken).ConfigureAwait(false);
        return ok && output.Contains(ServerName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(bool Ok, string Output)> RunAsync(
        string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo(_claude)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);

            if (process is null)
            {
                return (false, $"could not start '{_claude}'");
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);

            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);

            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (false, "timed out");
            }

            return (process.ExitCode == 0, await stdout.ConfigureAwait(false));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (false, e.Message);
        }
    }
}
