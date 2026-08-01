using System.Globalization;

namespace Supervisor.Core.Roster;

/// <summary>
/// An Activity Summary as it appears on a Roster row: the line itself, how old it is, and whether
/// that age has crossed into stale.
/// </summary>
/// <param name="Text">What the Agent is working on. Never empty.</param>
/// <param name="Age">How long since the summary last changed. Never negative.</param>
/// <param name="IsStale">Whether the summary has stopped advancing for long enough to be worth noticing.</param>
/// <param name="AgeLabel">The age at a glance — "just now", "45m", "1h 12m", "2d".</param>
public readonly record struct PresentedActivity(string Text, TimeSpan Age, bool IsStale, string AgeLabel);

/// <summary>
/// Presents an Agent's Activity Summary with its age.
/// </summary>
/// <remarks>
/// <para>
/// FR-3 and ADR-0005. The summary is derived involuntarily by tailing the transcript, which means a
/// wedged Agent keeps reporting the last true thing it did — forever, and confidently. The age
/// indicator is what separates "working on this" from "stopped working on this ten minutes ago",
/// and it is the difference between a pane you can trust and one you learn to ignore.
/// </para>
/// <para>
/// <strong>Staleness is presentation, not truth.</strong> A stale summary is still shown, just
/// marked. Blanking it would discard the only information there is about that Agent, leaving a row
/// that says nothing — strictly worse than one that says something old.
/// </para>
/// <para>
/// This lives in Core rather than in a renderer so the CLI and the browser cannot disagree about
/// when a row is stale, for the same reason <see cref="RosterOrdering"/> does.
/// </para>
/// </remarks>
public sealed class ActivitySummary
{
    /// <summary>
    /// How long a summary may sit unchanged before it is called stale.
    /// </summary>
    /// <remarks>
    /// Ten minutes, because the transcript advances on every tool call and the longest routine gap
    /// is a slow build or test run. Tighter than that and a healthy Agent running the suite flags as
    /// stuck, which trains the developer to ignore the marker — the one outcome worse than not
    /// having it.
    /// </remarks>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Shown when there is genuinely nothing yet — an Agent that enrolled before writing a transcript.
    /// </summary>
    /// <remarks>
    /// Public because the Roster assembler needs the same words: a row's summary is never empty, and
    /// two components disagreeing about how to say "nothing yet" would show up as two different
    /// placeholder strings in one pane.
    /// </remarks>
    public const string NothingYet = "No activity reported";

    private readonly TimeProvider _time;

    public ActivitySummary(TimeProvider? time = null, TimeSpan? staleAfter = null)
    {
        _time = time ?? TimeProvider.System;
        StaleAfter = staleAfter ?? DefaultStaleAfter;
    }

    /// <summary>The threshold this presenter applies.</summary>
    public TimeSpan StaleAfter { get; }

    /// <summary>
    /// Presents <paramref name="text"/> as of <paramref name="activityAt"/>, against the current clock.
    /// </summary>
    public PresentedActivity Present(string? text, DateTimeOffset activityAt) =>
        PresentAsOf(text, activityAt, _time.GetUtcNow());

    /// <summary>
    /// Presents against an explicit reference instant rather than the clock.
    /// </summary>
    /// <remarks>
    /// A Fleet view is a snapshot taken at a known moment, and ages belong to *that* moment. Using
    /// the renderer's own clock instead would make the same view age as it is passed around — and,
    /// once a Peer's rows are in it, would silently mix two Machines' clocks into one column.
    /// </remarks>
    public PresentedActivity PresentAsOf(string? text, DateTimeOffset activityAt, DateTimeOffset asOf)
    {
        var line = string.IsNullOrWhiteSpace(text) ? NothingYet : text.Trim();

        // Clamped, because two Machines' clocks do not agree. A Peer running a few minutes ahead
        // would otherwise hand us a negative age — rendering as "-3m" and sorting above everything
        // — for an Agent that is behaving perfectly.
        var elapsed = asOf - activityAt;
        var age = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

        return new PresentedActivity(line, age, age >= StaleAfter, Label(age));
    }

    /// <summary>
    /// Renders an age for a glance, not for a stopwatch.
    /// </summary>
    /// <remarks>
    /// Coarse on purpose: seconds of precision beside a forty-minute-old summary is noise. The
    /// question the column answers is "should I look at this row", and that never turns on a second.
    /// </remarks>
    private static string Label(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return Format(age.Minutes, "m");
        }

        if (age.TotalDays < 1)
        {
            var hours = Format(age.Hours, "h");
            return age.Minutes == 0 ? hours : hours + " " + Format(age.Minutes, "m");
        }

        return Format((int)age.TotalDays, "d");
    }

    private static string Format(int value, string unit) =>
        value.ToString(CultureInfo.InvariantCulture) + unit;
}
