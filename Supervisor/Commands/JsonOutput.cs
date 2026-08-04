using Spectre.Console;

namespace Supervisor.Commands;

/// <summary>
/// Writes machine-readable output to stdout without letting the renderer touch it.
/// </summary>
/// <remarks>
/// <para>
/// <c>IAnsiConsole.WriteLine</c> word-wraps to the profile width. For a table that is the whole
/// point; for JSON it inserts newlines <em>inside string values</em> and produces a payload no
/// parser will accept. The failure is width-dependent — short payloads survive, so it looks like an
/// intermittent bug in whatever is consuming the output — and redirected output does not escape it,
/// because Spectre assumes a default width when stdout is not a terminal.
/// </para>
/// <para>
/// Writing through the profile's underlying writer keeps the test seam (a <c>TestConsole</c> still
/// captures it) while bypassing layout entirely.
/// </para>
/// </remarks>
internal static class JsonOutput
{
    public static void Write(IAnsiConsole console, string json) =>
        console.Profile.Out.Writer.WriteLine(json);
}
