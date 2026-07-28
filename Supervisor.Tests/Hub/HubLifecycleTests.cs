using Supervisor.Core.Hub;

namespace Supervisor.Tests.Hub;

public sealed class HubLifecycleTests
{
    [Fact]
    public void StopsImmediatelyWhenNothingIsAttached()
    {
        var lifecycle = new HubLifecycle();

        Assert.Equal(HubStopOutcome.Stopped, lifecycle.RequestStop());
    }

    [Fact]
    public void StopRefusesWithAttachedClientsUnlessForced()
    {
        var lifecycle = new HubLifecycle();
        lifecycle.ClientAttached("agent-1");
        lifecycle.ClientAttached("agent-2");

        // Stopping the Hub tears down every attached client's state. Once terminals exist (WU-3)
        // that means killing live Agents mid-task, so the default must be refusal — the caller has
        // to say explicitly that they know what they are destroying.
        Assert.Equal(HubStopOutcome.RefusedClientsAttached, lifecycle.RequestStop());
        Assert.Equal(HubStopOutcome.Stopped, lifecycle.RequestStop(force: true));
    }

    [Fact]
    public void StopsOnceTheLastClientDetaches()
    {
        var lifecycle = new HubLifecycle();
        lifecycle.ClientAttached("agent-1");
        lifecycle.ClientDetached("agent-1");

        Assert.Equal(0, lifecycle.AttachedClients);
        Assert.Equal(HubStopOutcome.Stopped, lifecycle.RequestStop());
    }

    [Fact]
    public void AttachingTheSameClientTwiceCountsOnce()
    {
        // A client that reconnects re-announces itself. Counting the reconnect as a second client
        // would make the Hub permanently unstoppable without --force.
        var lifecycle = new HubLifecycle();
        lifecycle.ClientAttached("agent-1");
        lifecycle.ClientAttached("agent-1");

        Assert.Equal(1, lifecycle.AttachedClients);
    }
}
