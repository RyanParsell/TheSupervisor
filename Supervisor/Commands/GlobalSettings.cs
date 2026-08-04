using Spectre.Console.Cli;

namespace Supervisor.Commands;

/// <summary>
/// Base for every command's settings.
/// </summary>
/// <remarks>
/// <c>--json</c> lives here rather than being declared per-leaf so no verb can accidentally ship
/// without it. Spectre.Console.Cli does not push a branch's settings onto its leaves, so the
/// inheritance chain is what makes the option universal — <c>CommandTreeTests</c> pins it.
/// </remarks>
public abstract class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [System.ComponentModel.Description("Emit machine-readable JSON instead of a table.")]
    public bool Json { get; init; }
}
