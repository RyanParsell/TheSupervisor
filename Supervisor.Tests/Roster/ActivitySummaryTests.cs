using Supervisor.Core.Roster;
using Supervisor.Tests.Fakes;

namespace Supervisor.Tests.Roster;

public sealed class ActivitySummaryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (ActivitySummary Summary, FakeClock Clock) Build(TimeSpan? staleAfter = null)
    {
        var clock = new FakeClock(Start);
        return (new ActivitySummary(clock, staleAfter), clock);
    }

    [Fact]
    public void MarksStaleAfterThreshold()
    {
        // ADR-0005: an Agent whose activity has not advanced must read as visibly stuck. Without a
        // threshold, a frozen Agent and a working one are indistinguishable in the pane — which is
        // the exact failure the age indicator exists to prevent.
        var (summary, clock) = Build(TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(summary.Present("Running Bash", Start).IsStale);
    }

    [Fact]
    public void IsFreshRightUpToTheThreshold()
    {
        // The boundary is inclusive on the stale side, so "stale after 10 minutes" means what it
        // says. Asserting one tick either side pins it against a silent off-by-one.
        var (summary, clock) = Build(TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));

        Assert.False(summary.Present("Running Bash", Start).IsStale);
    }

    [Fact]
    public void AStaleSummaryIsStillShown()
    {
        // Staleness is presentation, not truth. Blanking a stale line would discard the only
        // information there is about that Agent and leave a row that says nothing at all.
        var (summary, clock) = Build(TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromHours(3));
        var presented = summary.Present("Refactoring the enrollment path", Start);

        Assert.True(presented.IsStale);
        Assert.Equal("Refactoring the enrollment path", presented.Text);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1m")]
    [InlineData(45 * 60, "45m")]
    [InlineData(3600, "1h")]
    [InlineData(4320, "1h 12m")]
    [InlineData(26 * 3600, "1d")]
    [InlineData(50 * 3600, "2d")]
    public void LabelsAgeAtHumanScale(int elapsedSeconds, string expected)
    {
        // The label is read at a glance beside a one-line summary, so it is coarse on purpose:
        // seconds of precision on a forty-minute-old row is noise, not information.
        var (summary, clock) = Build();

        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal(expected, summary.Present("doing something", Start).AgeLabel);
    }

    [Fact]
    public void ClampsAFutureTimestampRatherThanReportingNegativeAge()
    {
        // Two Machines' clocks do not agree. A Peer whose clock runs ahead would otherwise produce a
        // negative age — rendering as "-3m" and sorting wrongly — for an Agent that is perfectly fine.
        var (summary, clock) = Build();

        var presented = summary.Present("Running Read", clock.GetUtcNow().AddMinutes(3));

        Assert.Equal(TimeSpan.Zero, presented.Age);
        Assert.Equal("just now", presented.AgeLabel);
        Assert.False(presented.IsStale);
    }

    [Fact]
    public void NeverPresentsAnEmptySummary()
    {
        // CONTEXT.md: an Agent has exactly one Activity Summary, and it is never empty. The tail
        // guarantees a baseline, but a row that arrives before any transcript exists must still say
        // something true rather than render as a blank cell.
        var (summary, _) = Build();

        Assert.Equal("No activity reported", summary.Present("   ", Start).Text);
        Assert.Equal("No activity reported", summary.Present(null, Start).Text);
    }

    [Fact]
    public void ThresholdIsConfigurableAndDefaultsToTenMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ActivitySummary.DefaultStaleAfter);

        var clock = new FakeClock(Start);
        var impatient = new ActivitySummary(clock, TimeSpan.FromMinutes(1));
        var patient = new ActivitySummary(clock, TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(impatient.Present("Running Task", Start).IsStale);
        Assert.False(patient.Present("Running Task", Start).IsStale);
    }
}
