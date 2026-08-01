using Supervisor.Core.Roster;

namespace Supervisor.Tests.Roster;

public sealed class AgentsCliProbeTests
{
    /// <summary>
    /// Captured from a real <c>claude agents --json</c> run on a developer Machine, with paths,
    /// names, and session ids replaced. The shape is verbatim — that is the whole point of checking
    /// it in. `claude agents --json` is a supported contract, but supported is not frozen: when it
    /// changes, this test is what tells us, instead of a column that quietly goes blank.
    /// </summary>
    private const string CapturedContract = """
        [
          {
            "pid": 28868,
            "cwd": "C:\\Code\\Personal\\TheSupervisor",
            "kind": "interactive",
            "startedAt": 1785164721957,
            "sessionId": "11111111-1111-4111-8111-111111111111",
            "name": "TheSupervisor",
            "status": "busy"
          },
          {
            "pid": 35632,
            "cwd": "C:\\Code\\Personal\\Scratch",
            "kind": "interactive",
            "startedAt": 1785190044004,
            "sessionId": "22222222-2222-4222-8222-222222222222",
            "name": "scratch-pad",
            "status": "idle"
          }
        ]
        """;

    [Fact]
    public void ParsesTheContractShapeCapturedFromARealMachine()
    {
        var sightings = AgentsCliContract.Parse(CapturedContract);

        Assert.Equal(2, sightings.Count);

        var first = sightings[0];
        Assert.Equal("11111111-1111-4111-8111-111111111111", first.SessionId);
        Assert.Equal(28868, first.ProcessId);
        Assert.Equal(@"C:\Code\Personal\TheSupervisor", first.WorkingDirectory);
        Assert.Equal("TheSupervisor", first.DisplayName);
        Assert.Equal("interactive", first.Kind);
        Assert.Equal("busy", first.ReportedStatus);

        // Milliseconds, not seconds. Reading it as seconds puts every Agent in 2026 BC-adjacent
        // territory and silently breaks every age indicator on the pane.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1785164721957), first.StartedAt);
    }

    [Fact]
    public void IgnoresFieldsItDoesNotKnowAbout()
    {
        // Another product owns this contract and will add to it. A new field must be uninteresting,
        // not fatal.
        var sightings = AgentsCliContract.Parse(
            """
            [{"pid":7,"cwd":"C:\\tmp","kind":"interactive","startedAt":0,
              "sessionId":"s-1","name":"n","status":"idle","somethingNew":{"nested":true}}]
            """);

        Assert.Equal("s-1", Assert.Single(sightings).SessionId);
    }

    [Fact]
    public void SkipsEntriesWithNoSessionIdRatherThanFailingTheWholeProbe()
    {
        // An entry we cannot identify cannot be matched against the registry, so listing it risks
        // duplicating an Agent that is already enrolled. Dropping one entry beats dropping all of them.
        var sightings = AgentsCliContract.Parse(
            """
            [{"pid":7,"cwd":"C:\\tmp","kind":"interactive","startedAt":0,"name":"n","status":"idle"},
             {"pid":8,"cwd":"C:\\tmp","kind":"interactive","startedAt":0,"sessionId":"s-2","name":"n","status":"idle"}]
            """);

        Assert.Equal("s-2", Assert.Single(sightings).SessionId);
    }

    [Fact]
    public void EmptyFleetIsNotAnError()
    {
        Assert.Empty(AgentsCliContract.Parse("[]"));
    }
}
