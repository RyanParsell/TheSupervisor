namespace Supervisor.Tests.Fakes;

/// <summary>
/// Wall time under test control.
/// </summary>
/// <remarks>
/// The plan names this seam so that staleness, age, TTL, grace, and idle timeout are all provable
/// without waiting. <strong>No test may sleep</strong> — a suite that takes real seconds to assert a
/// ten-minute threshold is a suite that gets marked skipped within a month, and then the behavior it
/// guarded is unprotected without anyone deciding that.
/// </remarks>
public sealed class FakeClock : TimeProvider
{
    // A fixed, unremarkable instant. Deliberately not "now": a test whose expectations shift with the
    // calendar is a test that fails on a Tuesday for reasons nobody can reproduce.
    private static readonly DateTimeOffset Origin =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset? start = null) => _now = start ?? Origin;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves time forward. Negative spans are rejected — clocks do not run backwards.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "Time only moves forward.");
        }

        _now += by;
    }

    /// <summary>
    /// Sets the current instant outright, including to the past — the one way to stage the skew
    /// between two Machines' clocks that federation will have to survive.
    /// </summary>
    public void Set(DateTimeOffset now) => _now = now;
}
