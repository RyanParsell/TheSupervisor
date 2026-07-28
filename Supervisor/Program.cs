using Spectre.Console;
using Spectre.Console.Cli;
using Supervisor;

var registrar = new SimpleTypeRegistrar();
registrar.RegisterInstance(typeof(IAnsiConsole), AnsiConsole.Console);

var app = new CommandApp(registrar);
app.Configure(SupervisorCli.Configure);
return app.Run(args);
