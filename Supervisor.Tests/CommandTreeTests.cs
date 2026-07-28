using System.Reflection;
using Spectre.Console.Cli;
using Supervisor.Commands;

namespace Supervisor.Tests;

public sealed class CommandTreeTests
{
    private static IEnumerable<Type> SettingsTypes() =>
        typeof(SupervisorCli).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true }
                        && typeof(CommandSettings).IsAssignableFrom(t));

    [Fact]
    public void EveryLeafRedeclaresJson()
    {
        // Spectre.Console.Cli does NOT push a branch's settings onto its leaves, so a leaf that
        // does not inherit GlobalSettings silently loses --json. The symptom is empty output from
        // the flag rather than a parse error, so it survives until something compares payloads.
        var settings = SettingsTypes().ToList();

        Assert.NotEmpty(settings);
        Assert.All(settings, t => Assert.True(
            typeof(GlobalSettings).IsAssignableFrom(t),
            $"{t.Name} must inherit GlobalSettings so it carries --json."));
    }

    [Fact]
    public void RegistrarDefersConstructionUntilResolve()
    {
        // Spectre registers its own internal commands (ExplainCommand among them), and they do not
        // have parameterless constructors. A registrar that instantiates at Register() time throws
        // during app configuration — before any verb runs — so *every* invocation of the binary
        // fails identically while unit tests, which never configure the real tree, stay green.
        var registrar = new SimpleTypeRegistrar();

        var exception = Record.Exception(() =>
            registrar.Register(typeof(NeedsAnArgument), typeof(NeedsAnArgument)));

        Assert.Null(exception);
    }

    private sealed class NeedsAnArgument
    {
        public NeedsAnArgument(string required) => Required = required;
        public string Required { get; }
    }

    [Fact]
    public void ResolvesAnEmptySequenceForUnregisteredEnumerables()
    {
        // Spectre asks the resolver for IEnumerable<IHelpProvider> (and similar) while dispatching a
        // command, and treats null as a hard failure rather than "none registered". Returning an
        // empty sequence is the contract. Missing this breaks every verb *except* --version, which
        // short-circuits before dispatch — so it looks like a command bug rather than a DI bug.
        var resolver = new SimpleTypeRegistrar().Build();

        var resolved = resolver.Resolve(typeof(IEnumerable<IFormattable>));

        Assert.NotNull(resolved);
        Assert.Empty((IEnumerable<IFormattable>)resolved);
    }

    [Fact]
    public void EveryCommandIsConstructibleByTheRegistrar()
    {
        // The registrar satisfies a command's widest constructor from what is registered. A command
        // taking a dependency nobody registered constructs with a null and fails at runtime, on the
        // user's machine, rather than here.
        var registrar = new SimpleTypeRegistrar();
        registrar.RegisterInstance(typeof(Spectre.Console.IAnsiConsole), Spectre.Console.AnsiConsole.Console);
        var resolver = registrar.Build();

        var commands = typeof(SupervisorCli).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ICommand).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(commands);

        foreach (var command in commands)
        {
            var instance = resolver.Resolve(command);
            Assert.NotNull(instance);

            foreach (var parameter in command.GetConstructors().Single().GetParameters())
            {
                Assert.True(
                    resolver.Resolve(parameter.ParameterType) is not null,
                    $"{command.Name} needs {parameter.ParameterType.Name}, which nothing registers.");
            }
        }
    }
}
