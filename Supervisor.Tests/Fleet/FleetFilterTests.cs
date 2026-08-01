using Supervisor.Core.Fleet;

namespace Supervisor.Tests.Fleet;

public sealed class FleetFilterTests
{
    // `claude agents --cwd <path>` is documented as "Show only background sessions started under
    // <path>". Under, not equal to — matching its semantics is what stops `supervisor list --cwd`
    // and `claude agents --cwd` disagreeing about the same fleet on the same machine.

    [Theory]
    [InlineData(@"C:\Code\Personal\TheSupervisor", @"C:\Code\Personal\TheSupervisor", true)]
    [InlineData(@"C:\Code\Personal", @"C:\Code\Personal\TheSupervisor", true)]
    [InlineData(@"C:\Code", @"C:\Code\Personal\TheSupervisor", true)]
    [InlineData(@"C:\Code\Personal\TheSupervisor", @"C:\Code\Personal", false)]
    [InlineData(@"C:\Code\Other", @"C:\Code\Personal\TheSupervisor", false)]
    public void MatchesADirectoryAndEverythingUnderIt(string filter, string candidate, bool expected)
    {
        Assert.Equal(expected, FleetFilter.IsUnder(candidate, filter));
    }

    [Fact]
    public void DoesNotMatchASiblingThatMerelySharesAPrefix()
    {
        // The trap in every naive prefix check: "C:\Code\Foo" starts with "C:\Code\Fo", and
        // "C:\Code\FooBar" starts with "C:\Code\Foo". A filter that leaks siblings would show the
        // developer another repository's Agents and look, at a glance, entirely plausible.
        Assert.False(FleetFilter.IsUnder(@"C:\Code\FooBar", @"C:\Code\Foo"));
        Assert.True(FleetFilter.IsUnder(@"C:\Code\Foo\Nested", @"C:\Code\Foo"));
    }

    [Fact]
    public void IgnoresTrailingSeparatorsOnEitherSide()
    {
        Assert.True(FleetFilter.IsUnder(@"C:\Code\Foo\", @"C:\Code\Foo"));
        Assert.True(FleetFilter.IsUnder(@"C:\Code\Foo", @"C:\Code\Foo\"));
    }

    [Fact]
    public void IsCaseInsensitiveOnWindows()
    {
        // Windows paths are case-insensitive, and the same repository is routinely typed three ways.
        // A case-sensitive filter would silently return nothing and read as an empty fleet.
        Assert.Equal(
            OperatingSystem.IsWindows(),
            FleetFilter.IsUnder(@"C:\code\personal\thesupervisor", @"C:\Code\Personal\TheSupervisor"));
    }

    [Fact]
    public void AnAbsentFilterMatchesEverything()
    {
        Assert.True(FleetFilter.IsUnder(@"C:\anywhere", null));
        Assert.True(FleetFilter.IsUnder(@"C:\anywhere", "   "));
    }

    [Fact]
    public void AnUnknownWorkingDirectoryIsExcludedByAFilterButKeptWithoutOne()
    {
        // A row with no working directory cannot be proven to be under the path, so a filter must
        // drop it — but an unfiltered listing must still show it, because it is still an Agent.
        Assert.False(FleetFilter.IsUnder("", @"C:\Code"));
        Assert.True(FleetFilter.IsUnder("", null));
    }
}
