using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Supervisor.Core.Enrollment;

/// <summary>
/// Finds the pid of the process that spawned this one.
/// </summary>
/// <remarks>
/// The shim is spawned by Claude Code as a child process, so its own pid identifies nothing useful —
/// the Agent is the <em>parent</em>. Reporting the shim's pid would make every Agent unmatchable
/// against <c>claude agents --json</c>, which is exactly what the unenrolled backstop diffs against.
/// <para>
/// .NET has no parent-process API, and shelling out to WMI on the per-session fast path would cost
/// more than everything else the shim does. This is a direct NT query instead.
/// </para>
/// </remarks>
public static class ParentProcess
{
    public static int? TryGetParentId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return TryGetParentIdWindows();
    }

    [SupportedOSPlatform("windows")]
    private static int? TryGetParentIdWindows()
    {
        try
        {
            var information = default(ProcessBasicInformation);
            var status = NtQueryInformationProcess(
                GetCurrentProcess(),
                ProcessBasicInformationClass,
                ref information,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);

            if (status != 0)
            {
                return null;
            }

            var parent = (int)information.InheritedFromUniqueProcessId;
            return parent <= 0 ? null : parent;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Fail open: an unresolvable parent means we cannot identify the Agent, so enrollment is
            // skipped and recorded — never an error the developer sees.
            return null;
        }
    }

    private const int ProcessBasicInformationClass = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }

    // DllImport rather than the source-generated LibraryImport: the generator requires
    // AllowUnsafeBlocks across the whole project, which is a broad loosening to buy marginal
    // marshalling gains on two calls made once per process.
#pragma warning disable SYSLIB1054
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
#pragma warning restore SYSLIB1054
}
