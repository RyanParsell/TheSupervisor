using Supervisor.Core.Roster;

namespace Supervisor.Tests.Roster;

public sealed class RosterOrderingTests
{
    private static RosterEntry Entry(
        string name, AgentStatus status, int minutesAgo, AgentTier tier = AgentTier.Foreign) => new()
    {
        AgentId = $"machine-a/{name}",
        DisplayName = name,
        RepositoryId = "github.com/ryanparsell/thesupervisor",
        WorkingDirectory = @"C:\Code\Personal\TheSupervisor",
        MachineId = "machine-a",
        MachineName = "DESKTOP-RYAN",
        Status = status,
        Tier = tier,
        ActivitySummary = "doing something",
        ActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void AttentionOrderingAcrossAllStates()
    {
        // The pane's only job is "where am I needed next". A blocked Agent must outrank a busy one
        // even if the busy one changed a second ago — recency is a tie-breaker, never the sort.
        var ordered = RosterOrdering.Order(
        [
            Entry("idle-recent", AgentStatus.Idle, minutesAgo: 0),
            Entry("busy-recent", AgentStatus.Busy, minutesAgo: 0),
            Entry("unenrolled", AgentStatus.Unenrolled, minutesAgo: 0, AgentTier.Unenrolled),
            Entry("waiting-old", AgentStatus.Waiting, minutesAgo: 90),
            Entry("errored-old", AgentStatus.Errored, minutesAgo: 120),
        ]);

        Assert.Equal(
            ["waiting-old", "errored-old", "busy-recent", "idle-recent", "unenrolled"],
            ordered.Select(e => e.DisplayName));
    }

    [Fact]
    public void RecencyBreaksTiesWithinBand()
    {
        var ordered = RosterOrdering.Order(
        [
            Entry("busy-stale", AgentStatus.Busy, minutesAgo: 40),
            Entry("busy-fresh", AgentStatus.Busy, minutesAgo: 1),
            Entry("busy-middle", AgentStatus.Busy, minutesAgo: 10),
        ]);

        Assert.Equal(
            ["busy-fresh", "busy-middle", "busy-stale"],
            ordered.Select(e => e.DisplayName));
    }

    [Fact]
    public void StoppedRanksWithErroredNotWithIdle()
    {
        // A finished Agent is worth a glance — it may have finished badly, or be waiting to be
        // replaced. Filing it with idle would bury it.
        var ordered = RosterOrdering.Order(
        [
            Entry("idle", AgentStatus.Idle, minutesAgo: 0),
            Entry("stopped", AgentStatus.Stopped, minutesAgo: 60),
            Entry("busy", AgentStatus.Busy, minutesAgo: 0),
        ]);

        Assert.Equal(["stopped", "busy", "idle"], ordered.Select(e => e.DisplayName));
    }

    [Fact]
    public void UnenrolledAlwaysSortsLast()
    {
        // Visible, but never above something actionable: you cannot do anything to an Agent that
        // never enrolled, so it must not outrank one you can help.
        var ordered = RosterOrdering.Order(
        [
            Entry("unenrolled", AgentStatus.Unenrolled, minutesAgo: 0, AgentTier.Unenrolled),
            Entry("idle-ancient", AgentStatus.Idle, minutesAgo: 600),
        ]);

        Assert.Equal(["idle-ancient", "unenrolled"], ordered.Select(e => e.DisplayName));
    }

    [Fact]
    public void OrderingIsStableForIdenticalEntries()
    {
        // Two Agents in the same state with the same timestamp must not swap places between refreshes
        // — a list that reshuffles under the cursor is unusable.
        var a = Entry("alpha", AgentStatus.Busy, minutesAgo: 5);
        var b = Entry("beta", AgentStatus.Busy, minutesAgo: 5);

        var first = RosterOrdering.Order([a, b]).Select(e => e.DisplayName).ToList();
        var second = RosterOrdering.Order([a, b]).Select(e => e.DisplayName).ToList();

        Assert.Equal(first, second);
    }
}
