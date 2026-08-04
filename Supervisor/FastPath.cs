namespace Supervisor;

/// <summary>
/// Dispatch for the verbs that run on the per-session fast path.
/// </summary>
/// <remarks>
/// <para>
/// These verbs bypass Spectre.Console.Cli entirely. That is not a micro-optimisation: constructing
/// the command tree costs roughly 160 ms on a developer machine, and the whole budget is ~200 ms
/// (NFR-2). The shim runs on every Claude session start and the hooks on every session lifecycle
/// event, so this cost is paid continuously, in the foreground, by the developer.
/// </para>
/// <para>
/// <b>Keep this path austere.</b> No update check, no telemetry, no config scan, no banner, and no
/// dependency that drags in a large assembly graph. <c>StartupBudgetTests</c> fails the build if it
/// regresses — and the fix is always to move the dependency off this path, never to raise the
/// budget, which is a plan-level decision.
/// </para>
/// </remarks>
internal static class FastPath
{
    public static bool Handles(string verb) =>
        verb is "mcp" or "hook";

    /// <summary>
    /// Argument that makes a fast-path verb initialise and exit immediately, so the startup budget
    /// can be measured without the shim blocking on stdio.
    /// </summary>
    public const string StartupProbeArgument = "--startup-probe";

    public static Task<int> RunAsync(string[] args)
    {
        var probeOnly = Array.IndexOf(args, StartupProbeArgument) >= 0;

        return args[0] switch
        {
            "mcp" => McpShim.RunAsync(probeOnly),
            "hook" => HookShim.RunAsync(args, probeOnly),
            _ => Task.FromResult(1),
        };
    }
}
