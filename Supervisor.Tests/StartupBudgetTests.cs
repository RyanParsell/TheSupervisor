using System.Diagnostics;

namespace Supervisor.Tests;

/// <summary>
/// Guards the per-session fast path (NFR-2, D10).
/// </summary>
/// <remarks>
/// <para>
/// The <c>mcp</c> and <c>hook</c> verbs run on every Claude session start and every lifecycle event.
/// Their startup cost is paid continuously, in the foreground, by the developer — so a dependency
/// creeping onto that path is a tax, not a detail.
/// </para>
/// <para>
/// <b>If this fails, move the dependency off the fast path.</b> Raising the threshold is a
/// plan-level decision, not an implementation one.
/// </para>
/// </remarks>
public sealed class StartupBudgetTests
{
    private const int Samples = 7;
    private const int WarmupRuns = 4;

    /// <summary>
    /// The fast path must cost meaningfully less than the full command-tree path.
    /// </summary>
    /// <remarks>
    /// A relative budget rather than an absolute millisecond count, deliberately: absolute timings
    /// depend on the machine, and a threshold tuned on a dev box either flakes on a slower CI runner
    /// or is set so loose it stops guarding anything. The invariant that actually matters is
    /// structural — the fast path must not be paying for Spectre's command tree — and a ratio
    /// measures exactly that on any hardware.
    /// </remarks>
    private const double MaxFractionOfFullPath = 0.75;

    /// <summary>
    /// Secondary absolute ceiling, set generously. Catches a catastrophic regression that slows
    /// *both* paths equally (which the ratio alone would miss) without flaking on slow hardware.
    /// </summary>
    private const int AbsoluteCeilingMs = 900;

    [Fact]
    public void McpVerbStaysUnderBudget() => AssertFastPathBudget("mcp");

    [Fact]
    public void HookVerbStaysUnderBudget() => AssertFastPathBudget("hook");

    private static void AssertFastPathBudget(string verb)
    {
        var exe = SupervisorBinary();
        Assert.True(File.Exists(exe), $"Supervisor binary not found at {exe} — build the solution first.");

        var fast = FloorMs(exe, [verb, "--startup-probe"]);
        var full = FloorMs(exe, ["--version"]);

#if !DEBUG
        // The ratio is only meaningful in an optimized build.
        //
        // Measured on a dev machine: Release gives mcp 129 ms vs --version 260 ms (ratio 0.50),
        // while Debug gives 275 ms vs 282 ms (ratio 0.97). The difference is unoptimized JIT of the
        // MCP SDK's serialization-heavy generics, which swamps the structural saving the fast path
        // exists to produce. Release is what ships and what CI builds, so that is where this is
        // enforced — loosening the threshold until Debug passed would have left a guard that no
        // longer detects the regression it was written for.
        Assert.True(
            fast < full * MaxFractionOfFullPath,
            $"'{verb}' took {fast:N0} ms vs {full:N0} ms for the full path — it is paying for the "
            + $"command tree. Something on the fast path now pulls in Spectre or an equally heavy graph.");
#endif

        // Always enforced: catches a catastrophic regression that slows both paths equally, which
        // the ratio alone would miss.
        Assert.True(
            fast < AbsoluteCeilingMs,
            $"'{verb}' took {fast:N0} ms, over the {AbsoluteCeilingMs} ms ceiling.");
    }

    private static double FloorMs(string exe, string[] args)
    {
        // Warm up generously: this measures process startup, and running straight after a build
        // means a cold file cache and an antivirus scanner working through freshly-written
        // binaries. Under-warming here produced a real false failure in CI-style back-to-back
        // build-then-test.
        for (var i = 0; i < WarmupRuns; i++)
        {
            Run(exe, args);
        }

        var timings = new List<double>(Samples);
        for (var i = 0; i < Samples; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            Run(exe, args);
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();

        // The minimum, not the median. This is a floor measurement: the question is what startup
        // costs when nothing else interferes, and contention can only ever add time. Using the
        // median lets one scheduling hiccup drag the number up and fail a build for reasons that
        // have nothing to do with the code.
        return timings[0];
    }

    private static void Run(string exe, string[] args)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        process.WaitForExit();
    }

    /// <summary>
    /// Locates the CLI binary beside the test output, matching the current configuration.
    /// </summary>
    private static string SupervisorBinary()
    {
        // .../Supervisor.Tests/bin/<config>/<tfm>/  ->  .../Supervisor/bin/<config>/<tfm>/
        var testDir = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = testDir.Name;
        var configuration = testDir.Parent!.Name;
        var repoRoot = testDir.Parent!.Parent!.Parent!.Parent!.FullName;

        var name = OperatingSystem.IsWindows() ? "supervisor.exe" : "supervisor";
        return Path.Combine(repoRoot, "Supervisor", "bin", configuration, tfm, name);
    }
}
