using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor.Core.Diagnostics;
using Supervisor.Core.Hub;
using Supervisor.Core.Install;

namespace Supervisor.Commands;

/// <summary>
/// Where TheSupervisor's integration lives on this machine.
/// </summary>
/// <remarks>
/// One place, so install, uninstall, and doctor cannot disagree about which files they are talking
/// about — a doctor checking a different path from the one install wrote would report a healthy
/// machine as broken, or worse, the reverse.
/// </remarks>
internal static class InstallPaths
{
    /// <summary>
    /// Claude Code's settings file for this user.
    /// </summary>
    /// <remarks>
    /// <strong>Resolved from the OS, not from <c>%USERPROFILE%</c>.</strong>
    /// <see cref="Environment.SpecialFolder.UserProfile"/> asks Windows for the known folder and
    /// ignores the environment variable entirely — so redirecting <c>USERPROFILE</c> to sandbox a
    /// test silently operates on the developer's real file instead. That is why every command that
    /// writes here takes an explicit <c>--settings</c> path.
    /// </remarks>
    public static string Settings => Path.Combine(Home, ".claude", "settings.json");

    public static string Backups => Path.Combine(State, "backups");

    public static string ShimLog => Path.Combine(State, "shim.log");

    /// <summary>
    /// How Claude Code should invoke us, quoted if the path needs it.
    /// </summary>
    /// <remarks>
    /// The full path, never the bare name: a hook resolved through PATH would run whichever build
    /// happens to be first, which during development is routinely not the one the developer means.
    /// </remarks>
    public static string Command
    {
        get
        {
            var path = Environment.ProcessPath ?? "supervisor";
            return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
        }
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string State => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheSupervisor");
}

/// <summary>
/// Shared by the commands that read or write Claude Code's settings.
/// </summary>
/// <remarks>
/// <c>--settings</c> exists because these are the only commands that modify a file the developer
/// maintains by hand, and pointing them at a copy is the only way to watch them do it without
/// risking the original. It is also the escape hatch for a non-standard Claude Code install.
/// </remarks>
public abstract class SettingsPathSettings : GlobalSettings
{
    [CommandOption("--settings <PATH>")]
    [Description("Operate on this settings.json instead of the one in your home directory.")]
    public string? SettingsPath { get; init; }

    public string ResolvedSettingsPath =>
        string.IsNullOrWhiteSpace(SettingsPath) ? InstallPaths.Settings : SettingsPath;
}

public sealed class InstallSettings : SettingsPathSettings
{
    [CommandOption("--dry-run")]
    [Description("Report what would change without writing anything.")]
    public bool DryRun { get; init; }
}

/// <summary>Registers TheSupervisor with Claude Code.</summary>
public sealed class InstallCommand : AsyncCommand<InstallSettings>
{
    private readonly IAnsiConsole _console;

    public InstallCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var installer = new Installer(
            settings.ResolvedSettingsPath,
            InstallPaths.Backups,
            InstallPaths.Command,
            new ClaudeMcpRegistrar());

        var report = await installer.InstallAsync(settings.DryRun).ConfigureAwait(false);

        Report(_console, settings.DryRun ? "Would install" : "Installed", report);

        if (settings.DryRun && !report.WouldChangeSettings)
        {
            _console.MarkupLine("[dim]settings.json already has our hooks — nothing to change.[/]");
        }

        return report.Succeeded ? 0 : 1;
    }

    internal static void Report(IAnsiConsole console, string title, InstallReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).Title(title);
        table.AddColumn("Step");
        table.AddColumn("");
        table.AddColumn("Detail");

        foreach (var step in report.Steps)
        {
            table.AddRow(
                Markup.Escape(step.Name),
                step.Ok ? "[green]ok[/]" : "[red]failed[/]",
                Markup.Escape(step.Detail));
        }

        console.Write(table);
    }
}

public sealed class UninstallSettings : SettingsPathSettings;

/// <summary>Removes TheSupervisor from Claude Code, leaving no trace.</summary>
public sealed class UninstallCommand : AsyncCommand<UninstallSettings>
{
    private readonly IAnsiConsole _console;

    public UninstallCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var installer = new Installer(
            settings.ResolvedSettingsPath,
            InstallPaths.Backups,
            InstallPaths.Command,
            new ClaudeMcpRegistrar());

        var report = await installer.UninstallAsync().ConfigureAwait(false);

        InstallCommand.Report(_console, "Uninstalled", report);

        if (report.Succeeded)
        {
            _console.MarkupLine(
                $"[dim]Backups of settings.json remain in {Markup.Escape(InstallPaths.Backups)}.[/]");
        }

        return report.Succeeded ? 0 : 1;
    }
}

public sealed class DoctorSettings : SettingsPathSettings;

/// <summary>
/// Reports whether TheSupervisor is actually working on this machine.
/// </summary>
/// <remarks>
/// The counterweight to fail-open (NFR-1, D19). A broken shim exits 0 in silence, so a session that
/// starts normally is no evidence at all — this is the only place the quiet failures surface.
/// </remarks>
public sealed class DoctorCommand : AsyncCommand<DoctorSettings>
{
    private readonly IAnsiConsole _console;

    public DoctorCommand(IAnsiConsole console) => _console = console;

    public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var doctor = new Doctor(
            new ClaudeMcpRegistrar(),
            settings.ResolvedSettingsPath,
            InstallPaths.ShimLog,
            InstallPaths.Command,
            HubHealthAsync);

        var checks = await doctor.RunAsync().ConfigureAwait(false);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Check");
        table.AddColumn("");
        table.AddColumn("Detail");

        foreach (var check in checks)
        {
            table.AddRow(
                Markup.Escape(check.Name),
                check.Status switch
                {
                    DoctorStatus.Ok => "[green]ok[/]",
                    DoctorStatus.Note => "[yellow]note[/]",
                    _ => "[red]problem[/]",
                },
                Markup.Escape(check.Detail));
        }

        _console.Write(table);

        foreach (var check in checks.Where(c => c.Remedy is { Length: > 0 }))
        {
            _console.MarkupLine($"[bold]{Markup.Escape(check.Name)}:[/] {Markup.Escape(check.Remedy!)}");
        }

        // Non-zero only for real problems: a Hub that is not running yet is normal, and a script
        // that treats it as failure would be wrong every morning.
        return checks.Any(c => c.Status == DoctorStatus.Problem) ? 1 : 0;
    }

    /// <summary>
    /// Asks the Hub how it is, without starting one.
    /// </summary>
    /// <remarks>
    /// Deliberately attach-only: "is it healthy" must not be the thing that makes it exist, or the
    /// question can never be answered honestly.
    /// </remarks>
    private static async Task<HubHealth> HubHealthAsync(CancellationToken cancellationToken)
    {
        using var probe = new HttpHubProbe();
        var locator = new HubLocator(HubRendezvousStore.Default(), probe);

        var rendezvous = await locator.TryAttachAsync(cancellationToken).ConfigureAwait(false);
        if (rendezvous is null)
        {
            return HubHealth.NotRunning;
        }

        using var client = new HubClient(rendezvous);
        var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        if (status is null)
        {
            return HubHealth.NotRunning;
        }

        return status.ProtocolVersion == SupervisorProtocol.EnrollmentVersion
            ? HubHealth.Reachable
            : HubHealth.ProtocolMismatch;
    }
}
