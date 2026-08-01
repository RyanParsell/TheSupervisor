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
    private readonly IReadOnlyList<ClaudeAgentSighting> _sightings;
    private readonly Exception? _failure;

    public FakeAgentsCliProbe(params ClaudeAgentSighting[] sightings) => _sightings = sightings;

    private FakeAgentsCliProbe(Exception failure)
    {
        _sightings = [];
        _failure = failure;
    }

    /// <summary>A probe that cannot run — Claude absent, not on PATH, or a contract change.</summary>
    public static FakeAgentsCliProbe Failing(string message = "claude: command not found") =>
        new(new InvalidOperationException(message));

    public int Calls { get; private set; }

    public Task<IReadOnlyList<ClaudeAgentSighting>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return _failure is not null
            ? Task.FromException<IReadOnlyList<ClaudeAgentSighting>>(_failure)
            : Task.FromResult(_sightings);
    }
}
