using Spectre.Console.Cli;
using Supervisor;

var app = new CommandApp();
app.Configure(SupervisorCli.Configure);
return app.Run(args);
