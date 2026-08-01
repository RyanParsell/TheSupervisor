using Spectre.Console.Cli;
using Supervisor;

// ── Fast path ────────────────────────────────────────────────────────────────
// `mcp` and `hook` run on every Claude session and every lifecycle event, so their startup cost is
// paid all day (NFR-2). They are dispatched here, from raw argv, *before* the CommandApp exists:
// building Spectre's command tree costs ~160 ms on this machine, which alone exceeds the budget.
//
// This is the concrete meaning of D10's "austere path". Anything added above this line is paid by
// every session — keep it empty.
if (args.Length > 0 && FastPath.Handles(args[0]))
{
    return await FastPath.RunAsync(args).ConfigureAwait(false);
}

var app = new CommandApp(SupervisorCli.CreateRegistrar());
app.Configure(SupervisorCli.Configure);
return app.Run(args);
