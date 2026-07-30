# TheSupervisor

A CLI, skill, and local UI for supervising the AI coding agents you have running concurrently —
across editor windows, repositories, and machines — from any one of them.

> **Status: early.** Agents enrol themselves with a Hub, but nothing displays them yet. See
> [Where this is](#where-this-is).

## Why

You don't run one Claude session. You run several — different repos, different windows, sometimes
different machines. Nothing tells you about them collectively, so a session that has been blocked
on a question for twenty minutes looks exactly like one that is working hard.

TheSupervisor gives that fleet one place to be seen and steered, and makes joining it **involuntary**:
install once per machine, and every session afterwards enrols itself with no action from you or the
model.

## Documentation

| | |
|---|---|
| [`docs/plans/design.md`](docs/plans/design.md) | The founding design session — decisions `D1`…`D29` and the alternatives rejected |
| [`CONTEXT.md`](CONTEXT.md) | Domain language. Terms here are load-bearing and have banned aliases |
| [`docs/adr/`](docs/adr/) | Decisions that were hard to reverse and chosen against real alternatives |
| [`docs/sdd/`](docs/sdd/) | Living specification — PRD, architecture, stories |

## Building

Requires **.NET 10**.

```bash
dotnet build TheSupervisor.slnx
dotnet test  TheSupervisor.slnx
```

There is no installer yet. Run the built binary directly:

```
Supervisor/bin/Debug/net10.0/supervisor.exe
```

## Commands

```
supervisor --version

supervisor hub status [--json]   # is a Hub running, and what is attached to it
supervisor hub stop [--force]    # refuses while clients are attached, unless forced
supervisor hub serve             # run a Hub in this process (normally spawned for you)

supervisor mcp                   # the per-session MCP shim (spawned by Claude Code, not by you)
```

### Trying enrollment

Point a Claude session at the shim without touching your global settings:

```jsonc
// mcp.json
{ "mcpServers": { "thesupervisor": {
    "command": "<abs path>/supervisor.exe", "args": ["mcp"] } } }
```

```bash
claude --mcp-config mcp.json --strict-mcp-config -p "reply: ok"
supervisor hub status          # a Hub the session started, if none was running
```

If enrollment fails it does so **silently and successfully** — that is the design (NFR-1). The
evidence lands in `%LOCALAPPDATA%\TheSupervisor\shim.log`.

## Design notes worth knowing

**Fail open, always.** A broken TheSupervisor must be invisible inside a working session. The shim
exits 0 on every failure path and writes the reason down instead, because a silent failure that
leaves no trace is indistinguishable from success.

**The per-session fast path is a hard constraint.** `mcp` and `hook` run on every session start, so
they bypass the command tree entirely and are guarded by a CI test. If that guard fails, the fix is
to move the dependency off the fast path — raising the budget is a plan-level decision.

**Loopback is not authorization.** The Hub binds `127.0.0.1` and still requires a bearer secret,
compared in constant time, on every admin call.

## Where this is

| Work unit | |
|---|---|
| Scaffold + CI | ✅ |
| Hub host, rendezvous, `hub` verbs | ✅ |
| MCP shim, fast path, startup budget | ✅ |
| Hooks, `install` / `uninstall` / `doctor` | — |
| Roster, transcript tailing, unenrolled backstop | — |
| `supervisor list` and the Fleet MCP tool | — |
| Seeded-fleet demo + hermetic e2e | — |

Tracked in [`docs/plans/2026-07-27-enrollment-and-roster.md`](docs/plans/2026-07-27-enrollment-and-roster.md).

## Development

Work moves through three skills in [`skills/`](skills/): **pre-impl** (plan + test plan) →
**impl** (test-first) → **post-impl** (reconcile, document, publish). TDD is a gate, not a
preference: impl refuses a plan without a test plan, and post-impl reports any test the plan
promised and the work didn't deliver.
