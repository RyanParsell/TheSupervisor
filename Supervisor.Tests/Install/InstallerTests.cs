using Supervisor.Core.Install;

namespace Supervisor.Tests.Install;

public sealed class InstallerTests : IDisposable
{
    private const string Command = @"""C:\Tools\supervisor.exe""";

    private const string HandMaintained = """
        {
          "permissions": {
            "defaultMode": "acceptEdits"
          },
          "tui": "fullscreen"
        }
        """;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "supervisor-install-" + Guid.NewGuid().ToString("n"));

    private readonly string _settings;
    private readonly string _backups;
    private readonly FakeMcpRegistrar _mcp = new();

    public InstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = Path.Combine(_dir, "settings.json");
        _backups = Path.Combine(_dir, "backups");
        File.WriteAllText(_settings, HandMaintained);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Installer Build() => new(_settings, _backups, Command, _mcp);

    private static string Backup(string directory) =>
        Directory.EnumerateFiles(directory).OrderBy(f => f).Last();

    [Fact]
    public async Task BacksUpBeforeItWritesAnything()
    {
        // The backup is the only thing standing between a bug here and a file the developer cannot
        // get back. It is taken from what is on disk, before the first byte is written.
        await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(HandMaintained, await File.ReadAllTextAsync(Backup(_backups)));
    }

    [Fact]
    public async Task RestoresTheFileByteIdenticalToThePreInstallBackup()
    {
        // Directly guards the orphaned-hook failure mode: a hook left behind fires forever against a
        // binary that may no longer exist, and the developer has no idea what put it there.
        await Build().InstallAsync(dryRun: false, CancellationToken.None);
        var backup = await File.ReadAllTextAsync(Backup(_backups));

        await Build().UninstallAsync(CancellationToken.None);

        Assert.Equal(backup, await File.ReadAllTextAsync(_settings));
    }

    [Fact]
    public async Task IsIdempotentAcrossRepeatedRuns()
    {
        // Re-running install is the documented way to repair drift, so it must be safe to run any
        // number of times. A second copy of our block would double-fire every hook.
        //
        // Idempotence is about the resulting state, not the call count: install deliberately
        // re-asserts the MCP registration every run, because that is how a registration someone
        // removed gets repaired.
        await Build().InstallAsync(dryRun: false, CancellationToken.None);
        var once = await File.ReadAllTextAsync(_settings);

        await Build().InstallAsync(dryRun: false, CancellationToken.None);
        await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(once, await File.ReadAllTextAsync(_settings));
        Assert.True(await _mcp.IsRegisteredAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ADryRunWritesNothingAtAll()
    {
        // What makes it safe to point this at a real machine to see what it would do.
        var report = await Build().InstallAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(HandMaintained, await File.ReadAllTextAsync(_settings));
        Assert.False(Directory.Exists(_backups));
        Assert.Equal(0, _mcp.Registrations);
        Assert.True(report.WouldChangeSettings);
    }

    [Fact]
    public async Task ADryRunOnAnAlreadyInstalledMachineReportsNoChange()
    {
        await Build().InstallAsync(dryRun: false, CancellationToken.None);

        var report = await Build().InstallAsync(dryRun: true, CancellationToken.None);

        Assert.False(report.WouldChangeSettings);
    }

    [Fact]
    public async Task RegistersTheMcpServerThroughTheSupportedCommand()
    {
        // `~/.claude.json` is a large state file Claude Code owns — projects, history, caches. We
        // register through `claude mcp add` rather than hand-editing it, because a bug in our JSON
        // surgery there would cost the developer far more than a hook entry.
        await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(1, _mcp.Registrations);
        Assert.Contains("supervisor.exe", _mcp.LastExecutable!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFailedMcpRegistrationIsReportedRatherThanClaimedAsSuccess()
    {
        // A half-install — hooks present, MCP missing — is the state where enrollment silently never
        // happens. Fail-open means nothing else will ever mention it, so install has to.
        _mcp.FailRegistration = true;

        var report = await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Steps, s => !s.Ok && s.Name.Contains("MCP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UninstallRemovesTheMcpRegistrationToo()
    {
        await Build().InstallAsync(dryRun: false, CancellationToken.None);

        await Build().UninstallAsync(CancellationToken.None);

        Assert.Equal(1, _mcp.Removals);
    }

    [Fact]
    public async Task UninstallOnACleanMachineIsNotAnError()
    {
        var report = await Build().UninstallAsync(CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(HandMaintained, await File.ReadAllTextAsync(_settings));
    }

    [Fact]
    public async Task AnUnparseableSettingsFileStopsBeforeWriting()
    {
        // Refusing costs the developer a sentence. Guessing costs them a file they wrote by hand.
        await File.WriteAllTextAsync(_settings, "{ this is not json");

        var report = await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(_settings));
        Assert.Equal(0, _mcp.Registrations);
    }

    [Fact]
    public async Task InstallOnAMachineWithNoSettingsFileCreatesOne()
    {
        File.Delete(_settings);

        var report = await Build().InstallAsync(dryRun: false, CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.True(SettingsFile.Parse(await File.ReadAllTextAsync(_settings), Command).IsInstalled);
    }

    private sealed class FakeMcpRegistrar : IMcpRegistrar
    {
        public int Registrations { get; private set; }
        public int Removals { get; private set; }
        public string? LastExecutable { get; private set; }
        public bool FailRegistration { get; set; }
        public bool Registered { get; private set; }

        public Task<bool> RegisterAsync(string executable, CancellationToken cancellationToken = default)
        {
            if (FailRegistration)
            {
                return Task.FromResult(false);
            }

            Registrations++;
            LastExecutable = executable;
            Registered = true;
            return Task.FromResult(true);
        }

        public Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
        {
            Removals++;
            Registered = false;
            return Task.FromResult(true);
        }

        public Task<bool> IsRegisteredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Registered);
    }
}
