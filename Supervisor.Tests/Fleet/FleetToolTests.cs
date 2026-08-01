using Supervisor.Commands;
using Supervisor.Core.Fleet;
using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;
using Spectre.Console.Testing;

namespace Supervisor.Tests.Fleet;

public sealed class FleetToolTests
{
    private static RosterEntry Entry(string name, AgentStatus status, string cwd, AgentTier tier) => new()
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
        ActivityAt = FakeFleetSource.Observed.AddMinutes(-3),
        SubagentCount = 1,
    };

    private static RosterEntry[] Fleet() =>
    [
        Entry("waiting-agent", AgentStatus.Waiting, @"C:\Code\Personal\TheSupervisor", AgentTier.Foreign),
        Entry("busy-agent", AgentStatus.Busy, @"C:\Code\Personal\TheSupervisor\WebUI", AgentTier.Foreign),
        Entry("stranger", AgentStatus.Unenrolled, @"C:\Code\Other", AgentTier.Unenrolled),
    ];

    [Fact]
    public async Task ToolAndVerbReturnIdenticalResults()
    {
        // D14/L4's anti-drift pin. An Agent asking "what else is running" and a human asking the
        // same question must get the same answer — if they diverge, the two will disagree about the
        // fleet while both being confident, and there is no surface on which that disagreement shows.
        var agents = Fleet();

        var console = new TestConsole();
        console.Profile.Width = 200;
        await new ListCommand(console, new FakeFleetSource(agents))
            .ExecuteAsync(null!, new ListSettings { Json = true });

        var tool = await new FleetTool(new FakeFleetSource(agents)).ListAsync();

        Assert.Equal(console.Output.Trim(), tool.Trim());
    }

    [Fact]
    public async Task TheToolAppliesTheSameCwdFilter()
    {
        var source = new FakeFleetSource(Fleet());

        await new FleetTool(source).ListAsync(@"C:\Code\Personal");

        Assert.Equal(@"C:\Code\Personal", source.LastQuery?.WorkingDirectory);
    }

    [Fact]
    public async Task NoHubIsAnEmptyFleetNotAnError()
    {
        // A tool that throws puts an error into the calling Agent's context, where it reads as
        // something the Agent did wrong and invites a retry loop. An empty fleet is the truth.
        var json = await new FleetTool(FakeFleetSource.NoHub()).ListAsync();

        var view = System.Text.Json.JsonSerializer.Deserialize(json, FleetJsonContext.Default.FleetView);

        Assert.NotNull(view);
        Assert.Empty(view.Agents);
    }

    [Fact]
    public async Task TheToolReturnsParseableJsonNotProse()
    {
        // Whatever an MCP tool returns lands in an Agent's context as text. Returning a rendered
        // table would cost tokens and force the Agent to parse English to answer "is anything
        // blocked" — the one question this tool exists to answer.
        var json = await new FleetTool(new FakeFleetSource(Fleet())).ListAsync();

        var view = System.Text.Json.JsonSerializer.Deserialize(json, FleetJsonContext.Default.FleetView);

        Assert.Equal(
            ["waiting-agent", "busy-agent", "stranger"],
            view!.Agents.Select(a => a.DisplayName));
    }
}
