using System.Text.Json;
using Spectre.Console.Testing;
using Supervisor.Commands;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Fleet;

public sealed class ListCommandTests
{
    private static readonly DateTimeOffset Observed = FakeFleetSource.Observed;

    private static RosterEntry Entry(
        string name,
        AgentStatus status,
        string cwd = @"C:\Code\Personal\TheSupervisor",
        AgentTier tier = AgentTier.Foreign,
        int subagents = 0,
        DateTimeOffset? activityAt = null) => new()
    {
        AgentId = $"machine-a/{name}",
        DisplayName = name,
        RepositoryId = "github.com/ryanparsell/thesupervisor",
        WorkingDirectory = cwd,
        MachineId = "machine-a",
        MachineName = "DESKTOP-RYAN",
        Status = status,
        Tier = tier,
        ActivitySummary = "Running Edit",
        ActivityAt = activityAt ?? Observed.AddMinutes(-3),
        SubagentCount = subagents,
    };

    private static (ListCommand Command, TestConsole Console) Build(FakeFleetSource source)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        return (new ListCommand(console, source), console);
    }

    [Fact]
    public async Task TableAndJsonCarryTheSameRows()
    {
        // D14/L4's anti-drift claim at the output level: two renderings of one query must not
        // disagree about who is running. A row present in one and missing from the other is the
        // failure this pins, and it is invisible until someone compares them by hand.
        var agents = new[]
        {
            Entry("waiting-agent", AgentStatus.Waiting),
            Entry("busy-agent", AgentStatus.Busy),
            Entry("stranger", AgentStatus.Unenrolled, @"C:\Code\Other", AgentTier.Unenrolled),
        };

        var (table, tableConsole) = Build(new FakeFleetSource(agents));
        Assert.Equal(0, await table.ExecuteAsync(null!, new ListSettings()));

        var (json, jsonConsole) = Build(new FakeFleetSource(agents));
        Assert.Equal(0, await json.ExecuteAsync(null!, new ListSettings { Json = true }));

        var names = JsonDocument.Parse(jsonConsole.Output)
            .RootElement.GetProperty("agents")
            .EnumerateArray()
            .Select(a => a.GetProperty("displayName").GetString())
            .ToList();

        Assert.Equal(["waiting-agent", "busy-agent", "stranger"], names);
        Assert.All(names, name => Assert.Contains(name!, tableConsole.Output, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CwdFilterMatchesClaudeAgentsSemantics()
    {
        // `claude agents --cwd <path>` shows sessions started *under* <path>. The verb passes the
        // filter to the service rather than reimplementing it, so the two can only agree.
        var source = new FakeFleetSource([Entry("here", AgentStatus.Busy)]);
        var (command, _) = Build(source);

        await command.ExecuteAsync(null!, new ListSettings { Cwd = @"C:\Code\Personal" });

        Assert.Equal(@"C:\Code\Personal", source.LastQuery?.WorkingDirectory);
    }

    [Fact]
    public async Task EmptyFleetIsNotAnError()
    {
        var (command, console) = Build(new FakeFleetSource([]));

        Assert.Equal(0, await command.ExecuteAsync(null!, new ListSettings()));
        Assert.Contains("No agents", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyFleetInJsonIsAnEmptyArrayNotAMessage()
    {
        // A script parsing --json must never receive prose. "No agents are running" on stdout breaks
        // the parse, and the failure surfaces in the caller rather than here.
        var (command, console) = Build(new FakeFleetSource([]));

        await command.ExecuteAsync(null!, new ListSettings { Json = true });

        Assert.Empty(JsonDocument.Parse(console.Output).RootElement.GetProperty("agents").EnumerateArray());
    }

    [Fact]
    public async Task NoHubRunningIsNotAnError()
    {
        // Nothing has enrolled yet, which is the normal state of a machine that just booted. This
        // must read as an empty fleet, not as a broken tool — and it must not start a Hub, because
        // "what is running" and "make something run" are different questions.
        var (command, console) = Build(FakeFleetSource.NoHub());

        Assert.Equal(0, await command.ExecuteAsync(null!, new ListSettings()));
        Assert.Contains("No Hub", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonCarriesStatusAndTierAsNames()
    {
        var (command, console) = Build(new FakeFleetSource([Entry("a", AgentStatus.Waiting, subagents: 3)]));

        await command.ExecuteAsync(null!, new ListSettings { Json = true });

        var agent = JsonDocument.Parse(console.Output).RootElement.GetProperty("agents")[0];

        Assert.Equal("Waiting", agent.GetProperty("status").GetString());
        Assert.Equal("Foreign", agent.GetProperty("tier").GetString());
        Assert.Equal(3, agent.GetProperty("subagentCount").GetInt32());
    }

    [Fact]
    public async Task TheTableShowsWhatTheDeveloperNeedsToTriage()
    {
        // The pane has one job — where am I needed next — so a row has to carry the Agent, where it
        // is, what state it is in, and what it is doing. A table missing any of those sends the
        // developer to another window to find out.
        var (command, console) = Build(new FakeFleetSource(
            [Entry("waiting-agent", AgentStatus.Waiting, subagents: 2)]));

        await command.ExecuteAsync(null!, new ListSettings());

        var output = console.Output;
        Assert.Contains("waiting-agent", output, StringComparison.Ordinal);
        Assert.Contains("waiting", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Running Edit", output, StringComparison.Ordinal);
        Assert.Contains("thesupervisor", output, StringComparison.OrdinalIgnoreCase);

        // The age indicator, not a raw timestamp: the question is "has this moved", never "at what
        // instant did it move".
        Assert.Contains("3m", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnenrolledRowIsNeverMarkedStale()
    {
        // Found by running the verb against a real machine: every unenrolled row read "4d stale".
        // An unenrolled session has no Activity Summary that could advance — its age is how long it
        // has been running — so the marker fires on every such row forever and means nothing. A
        // warning that is always on is worse than no warning, because it trains the eye past it.
        var (command, console) = Build(new FakeFleetSource(
        [
            Entry("stranger", AgentStatus.Unenrolled, @"C:\Code\Other", AgentTier.Unenrolled,
                activityAt: Observed.AddDays(-4)),
        ]));

        await command.ExecuteAsync(null!, new ListSettings());

        Assert.Contains("4d", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEnrolledRowIsStillMarkedStale()
    {
        // The other half: suppressing the marker for unenrolled rows must not suppress it where it
        // is the whole point — an enrolled Agent whose activity stopped advancing.
        var (command, console) = Build(new FakeFleetSource(
            [Entry("wedged", AgentStatus.Busy, activityAt: Observed.AddHours(-2))]));

        await command.ExecuteAsync(null!, new ListSettings());

        Assert.Contains("stale", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("github.com/ryanparsell/thesupervisor", "ryanparsell/thesupervisor")]
    [InlineData("github.com/experiences-and-devices/wexpert", "experiences-and-devices/wexpert")]
    [InlineData(@"machine:b97551a204a14155a31ce1d0f7acddca/C:\Code\MS\Omni", @"C:\Code\MS\Omni")]
    [InlineData("something-else", "something-else")]
    public void RepositoryLabelDropsWhatEveryRowShares(string id, string expected)
    {
        // Also from the real run: the host and the machine hash are identical on every row, so they
        // are pure width — and width is what pushed every other column into wrapping across three
        // lines. The full id stays in --json, where something might actually key on it.
        Assert.Equal(expected, ListCommand.RepositoryLabel(id));
    }

    [Fact]
    public async Task JsonSurvivesANarrowTerminal()
    {
        // IAnsiConsole.WriteLine word-wraps to the profile width, which inserts newlines *inside*
        // JSON string values and produces a payload no parser accepts. It is width-dependent, so it
        // looks like an intermittent bug in the consumer, and redirecting output does not escape it
        // — Spectre assumes a width when stdout is not a terminal.
        var console = new TestConsole();
        console.Profile.Width = 40;

        var command = new ListCommand(console, new FakeFleetSource(
            [Entry("an-agent-with-a-long-name", AgentStatus.Busy, @"C:\Code\Personal\TheSupervisor")]));

        await command.ExecuteAsync(null!, new ListSettings { Json = true });

        var parsed = JsonDocument.Parse(console.Output);

        Assert.Equal(
            "an-agent-with-a-long-name",
            parsed.RootElement.GetProperty("agents")[0].GetProperty("displayName").GetString());
    }
}