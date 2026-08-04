using Supervisor.Core.Diagnostics;
using Supervisor.Core.Install;

namespace Supervisor.Tests.Install;

public sealed class DoctorTests : IDisposable
{
    private const string Command = @"""C:\Tools\supervisor.exe""";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-doctor-" + Guid.NewGuid().ToString("n"));

    private readonly string _settings;
    private readonly string _log;

    public DoctorTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = Path.Combine(_dir, "settings.json");
        _log = Path.Combine(_dir, "shim.log");
        File.WriteAllText(_settings, "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Doctor Build(
        bool mcpRegistered = true,
        bool hooksInstalled = true,
        HubHealth hub = HubHealth.Reachable)
    {
        if (hooksInstalled)
        {
            File.WriteAllText(_settings, SettingsFile.Parse("{}", Command).Install());
        }

        return new Doctor(
            new StubRegistrar(mcpRegistered),
            _settings,
            _log,
            Command,
            _ => Task.FromResult(hub));
    }

    private static DoctorCheck Find(IReadOnlyList<DoctorCheck> checks, string fragment) =>
        checks.Single(c => c.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task AHealthyMachineReportsNoProblems()
    {
        var checks = await Build().RunAsync(CancellationToken.None);

        Assert.All(checks, c => Assert.NotEqual(DoctorStatus.Problem, c.Status));
    }

    [Theory]
    [InlineData("mcp", false, true, HubHealth.Reachable)]
    [InlineData("hook", true, false, HubHealth.Reachable)]
    [InlineData("protocol", true, true, HubHealth.ProtocolMismatch)]
    public async Task ReportsEachFailureModeWithItsRemedy(
        string fragment, bool mcp, bool hooks, HubHealth hub)
    {
        // Fail-open (D19) means nothing else will ever tell the developer any of this. A shim that
        // cannot enrol exits 0 in silence, so a check that reports a problem without naming the fix
        // leaves them exactly where they started — knowing something is wrong and not what to do.
        var checks = await Build(mcp, hooks, hub).RunAsync(CancellationToken.None);
        var check = Find(checks, fragment);

        Assert.Equal(DoctorStatus.Problem, check.Status);
        Assert.False(string.IsNullOrWhiteSpace(check.Remedy), $"{check.Name} has no remedy");
    }

    [Fact]
    public async Task NoHubRunningIsANoteNotAProblem()
    {
        // The Hub starts when the first session enrols. A machine that has not run Claude today is
        // working exactly as designed, and flagging it red would train the developer to ignore red.
        var checks = await Build(hub: HubHealth.NotRunning).RunAsync(CancellationToken.None);

        Assert.NotEqual(DoctorStatus.Problem, Find(checks, "hub").Status);
    }

    [Fact]
    public async Task SurfacesRecentShimFailures()
    {
        // The whole point of the shim log: a session that started normally proves nothing, because
        // a broken shim is *designed* to look identical to a working one.
        await File.WriteAllTextAsync(
            _log,
            $"{DateTimeOffset.UtcNow:O}\tenrol.hub-unreachable\tconnection refused{Environment.NewLine}");

        var checks = await Build().RunAsync(CancellationToken.None);
        var check = Find(checks, "recent");

        Assert.NotEqual(DoctorStatus.Ok, check.Status);
        Assert.Contains("hub-unreachable", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAbsentShimLogIsFine()
    {
        // Nothing has failed yet, which is the state a healthy machine is in.
        var checks = await Build().RunAsync(CancellationToken.None);

        Assert.Equal(DoctorStatus.Ok, Find(checks, "recent").Status);
    }

    [Fact]
    public async Task EveryCheckSaysWhatItLookedAt()
    {
        // A doctor that says "MCP: problem" and nothing else sends the developer hunting. Each line
        // names the file or the command it inspected, so the next step is obvious.
        var checks = await Build(mcpRegistered: false, hooksInstalled: false).RunAsync(CancellationToken.None);

        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Detail), $"{c.Name} has no detail"));
    }

    [Fact]
    public async Task ReportsAProblemWhenSettingsCannotBeRead()
    {
        await File.WriteAllTextAsync(_settings, "{ not json");

        var checks = await Build(hooksInstalled: false).RunAsync(CancellationToken.None);

        Assert.Equal(DoctorStatus.Problem, Find(checks, "hook").Status);
    }

    private sealed class StubRegistrar : IMcpRegistrar
    {
        private readonly bool _registered;

        public StubRegistrar(bool registered) => _registered = registered;

        public Task<bool> RegisterAsync(string executable, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> UnregisterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsRegisteredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_registered);
    }
}
