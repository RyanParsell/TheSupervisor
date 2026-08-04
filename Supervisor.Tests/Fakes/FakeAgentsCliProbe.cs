using Supervisor.Core.Roster;

namespace Supervisor.Tests.Fakes;

/// <summary>
/// Stands in for <c>claude agents --json</c> so the unenrolled backstop is provable offline.
/// </summary>
/// <remarks>
/// The real probe spawns another product's CLI. Depending on that in a unit test would make the
/// suite need Claude installed, authenticated, and with sessions running — which is to say it would
/// make the backstop untested in CI, exactly where the double-listing bug would hide.
/// </remarks>
public sealed class FakeAgentsCliProbe : IAgentsCliProbe
{
    private readonly Exception? _failure;

    public FakeAgentsCliProbe(params ClaudeAgentSighting[] sightings) => Sightings = sightings;

    private FakeAgentsCliProbe(Exception failure)
    {
        Sightings = [];
        _failure = failure;
    }

    /// <summary>
    /// What the next call reports. Settable so a test can move a session between states across
    /// refreshes of the <em>same</em> assembler — which is the only way to assert a transition,
    /// since a fresh assembler stamps everything as newly changed regardless.
    /// </summary>
    public IReadOnlyList<ClaudeAgentSighting> Sightings { get; set; }

    /// <summary>A probe that cannot run — Claude absent, not on PATH, or a contract change.</summary>
    public static FakeAgentsCliProbe Failing(string message = "claude: command not found") =>
        new(new InvalidOperationException(message));

    public int Calls { get; private set; }

    public Task<IReadOnlyList<ClaudeAgentSighting>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return _failure is not null
            ? Task.FromException<IReadOnlyList<ClaudeAgentSighting>>(_failure)
            : Task.FromResult(Sightings);
    }
}
