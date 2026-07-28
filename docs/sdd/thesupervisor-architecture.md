# TheSupervisor — Architecture

**Status:** Living document. Updated by the post-impl skill as the system changes.
**Domain language:** [`CONTEXT.md`](../../CONTEXT.md) · **Requirements:** [`thesupervisor-prd.md`](thesupervisor-prd.md) · **Decisions:** [`docs/adr/`](../adr/)

## Grounding Index

Route to the sections you need; do not read this document end to end.

| If the work touches… | Read | `arch:` slug |
|---|---|---|
| The command tree, output modes, formatters | Project Structure, Conventions | `cli-core` |
| How a session enrolls; hooks; the fast path | Enrollment, The Fast Path | `enrollment` |
| Hub lifecycle, rendezvous, start-or-attach | The Hub | `hub` |
| Roster contents, transcript tailing | The Roster, Data Flow | `roster` |
| Commands, tiers, launch | The Control Service | `supervision` |
| ConPTY, Job Objects, terminal transport | The Terminal | `terminal` |
| Pairing, Peer link, roster merge | Federation | `federation` |
| Workstream identity and storage | Workstreams | `workstream` |
| React shell, panes, WebSocket | The Browser UI | `webui` |
| Tokens, secrets, origin guards, pinning | Security Model | `security` |
| Toasts and attention | Ambient Attention | `notifications` |
| Inference provider | Model Backend | `model-backend` |
| Packaging, install, self-update | Packaging | `packaging` |
| Skills source and deploy | Skills | `skills` |
| Telemetry, spans, tracing | Observability | `observability` |
| Anything at all | Architectural Approach, Testing Strategy | — |

---

## Architectural Approach: "Adapted Core"

TheSupervisor is built by **copy-adapting a proven stack** rather than inventing one. WExpert
(`C:\Code\MS\CLI`) already ships, in production, the hard parts: a detached per-user host with an
ACL-protected rendezvous, a hand-vendored ConPTY interop layer with a kill-on-close Job Object, a
bounded-backpressure terminal transport over WebSocket, a co-hosted Streamable-HTTP MCP endpoint with
per-client capability tokens, and a React/Vite browser shell with an xterm.js terminal drawer.

Three consequences follow, and they shape everything below:

1. **Lift the proven, invert the mismatched.** Most of WExpert's terminal and host stack transfers
   near-verbatim. Three decisions do **not** and must be deliberately reversed — see *Where the
   adaptation breaks*.
2. **Federation is the genuinely new part.** Everything else has prior art; Hub-to-Hub is net-new and
   is therefore where the design is most likely to be wrong.
3. **The per-session fast path is a first-class constraint.** WExpert has one host process;
   TheSupervisor spawns an MCP child *per Agent session* and runs hooks *per prompt*. Startup cost
   moves onto the critical path in a way it never was for WExpert.

### Where the adaptation breaks

| WExpert decision | Why it cannot transfer |
|---|---|
| **D18** — every terminal inherits the invocation directory; no folder picker | TheSupervisor's premise is *many* Repositories. Inverted by D12: launch targets are learned from the Roster. |
| Terminal transport is loopback-only by construction | Federation must therefore **not** relay PTY frames (D8). The constraint is preserved by narrowing the feature, not by weakening the transport. |
| A ~3,500 ms CLI startup preamble | Measured; acceptable for a tool invoked by hand, unacceptable per-session. Hence the austere fast path and its budget test (D10, NFR-2). |

---

## Project Structure

```
TheSupervisor.sln
├─ Supervisor/                  # CLI + global tool entry point (binary: supervisor)
│  ├─ Program.cs                #   Spectre.Console.Cli command tree
│  ├─ Commands/                 #   verb implementations, incl. AgentGuideCommand.cs
│  └─ wwwroot/                  #   committed WebUI bundle (copied by build, NOT generated)
├─ Supervisor.Core/             # domain model — Agent, Roster, Command, Workstream
├─ Supervisor.Web/              # Hub host — Kestrel, WebSocket, MCP endpoint, terminal
│  └─ Terminal/ConPty/          #   hand-vendored kernel32 interop + Job Object
├─ Supervisor.Tests/            # xUnit; Fakes/ holds the seams (see Testing Strategy)
├─ WebUI/                       # React + TypeScript + Vite source (built into wwwroot)
├─ e2e/                         # Playwright hermetic scenarios
├─ skills/                      # tracked skill source (deployed, never read in place)
└─ docs/{sdd,plans,artifacts,research,adr}/
```

**`Supervisor/wwwroot` is build output that is committed.** The .NET build only *copies* it, so a
`WebUI/` change that is not rebuilt and committed ships a stale bundle silently. Every MSU touching
`WebUI/` rebuilds and commits it — this is a known trap inherited from WExpert, where it shipped.

---

## Components

### The Hub

One Hub per user per Machine, owning Kestrel and all runtime state. `supervisor ui` is a short-lived
**start-or-attach launcher**: it probes the rendezvous, attaches if a Hub is alive, otherwise spawns
a detached one, opens a browser window, and exits. Closing the launching terminal does not kill it.

- **Rendezvous:** `%LOCALAPPDATA%\TheSupervisor\hub\host.json`, user-ACL-protected, holding a
  non-secret host id, a separate 256-bit host secret, pid, loopback endpoint, version, and start
  time. Advisory and always probe-verified; stale files are removed. Concurrent starts serialize.
- **Spawn form:** `UseShellExecute=true` + `WindowStyle=Hidden` — **not** `CreateNoWindow=true`.
  A console-less process cannot bind a pseudoconsole child, so the terminal comes up dead. This is a
  bug WExpert shipped and fixed; we inherit the fix. *(D16)*
- **Lifetimes are distinct:** Hub outlives everything; an Agent lives for its session; a UI client
  owns terminals and expires after a reconnect grace; a terminal is one ConPTY in a Job Object.

### Enrollment

The CLI is registered once as a **user-scope stdio MCP server**, so every Claude session spawns and
connects it at startup — that connection *is* enrollment, and the same pipe is the inbound Command
channel. `SessionStart`/`SessionEnd` hooks pin lifecycle; `UserPromptSubmit` provides the baseline
Activity Summary. The skill is not the enrollment path. *(ADR-0002)*

**Backstop:** each Hub polls `claude agents --json` — a supported, TTY-free, scriptable contract
covering interactive *and* background sessions — and diffs it against the Roster. Unmatched local
sessions become **Unenrolled Agents**: visible, labelled, uncontrollable. This turns a silent blind
spot into a diagnosable state. *(D6)*

### The Fast Path

The `mcp` and `hook` verbs run on a deliberately austere startup path: no update check, no telemetry
init, no config scan, no banner. A CI test asserts the budget (~200 ms). **If that test goes red, the
fix is to move the dependency off the fast path — not to raise the budget.** *(D10)*

### The Roster

The Hub's live set of enrolled Agents. Each row carries Repository, name, Status
(`busy`/`idle`/`waiting`), Activity Summary with age, tier, Machine, and a Subagent count.

**Activity Summary** is layered: the submitted prompt as an involuntary baseline, kept current by the
Hub **tailing the Agent's transcript** (`~/.claude/projects/<slug>/<sessionId>.jsonl`, which carries
role, assistant text, and `tool_use` entries). Chosen over `PreToolUse` hooks because tailing costs
the Agent nothing while a hook costs ~130–200 ms *per tool call*. Parsing failure degrades to the
prompt baseline; no Agent disappears. *(ADR-0005)*

**Ordering** is flat and attention-first: waiting → errored/stopped → busy → idle → unenrolled,
recency breaking ties. Repository, Machine, and tier are columns, not structure. *(D23)*

### The Control Service

One shared service behind the CLI verbs, the MCP tools, and the UI, so the three surfaces cannot
drift — the pattern WExpert proved with `UiControlService`. *(D14)*

**Tiers.** An **Owned Agent** was launched into a Hub-owned ConPTY and accepts injected input. A
**Foreign Agent** enrolled elsewhere: fully observable, cooperatively controllable (it collects its
Pending Command on its next tool call), and stoppable only by hard kill. Ownership is fixed at launch
and never transfers. *(ADR-0003)*

**Commands** carry originator, hop count, and TTL. States: queued → delivered → acknowledged, or
expired/refused, always reported to the originator. **Queue depth per Agent is 1** — a new Command
replaces the pending one, so a woken Agent acts on latest intent rather than a stale backlog. *(D21)*

**Stop** is tiered: Owned gets a graceful ladder (Ctrl+C → exit instruction → Job Object kill on
timeout); Foreign gets a confirmed hard kill by pid. Acceptable because the transcript persists and
the conversation stays resumable. *(D25)*

**Launch** targets are learned from the Roster — every Repository an Agent has enrolled from,
most-recent first — with options remembered per Repository. **Agents may not set permission mode**;
only human-initiated launches may. Otherwise a constrained Agent could mint an unconstrained one.

### The Terminal

Copy-adapted near-verbatim from `WExpert.Web/Terminal/`: hand-vendored `kernel32` ConPTY interop
(`CreatePseudoConsole`, `STARTUPINFOEX` pseudoconsole attribute, `ResizePseudoConsole`), a
kill-on-close **Job Object**, a per-client session manager with a cap, and a dedicated binary
WebSocket carrying raw PTY bytes with **bounded backpressure that never drops a byte** — dropping
part of a VT sequence corrupts terminal state.

Browser side: `@xterm/xterm` + fit addon, self-hosted (no CDN), waiting for a stable fitted size
before starting ConPTY so the first paint is at the right width.

The terminal is **load-bearing architecture, not a convenience**: launching an Agent here is the only
way to make it Owned, and therefore steerable.

### Federation

Hubs pair explicitly. `supervisor peer pair <host>` records the Peer's endpoint, a shared 256-bit
secret, and a **certificate fingerprint** on both sides. No broadcast discovery, no implicit trust —
mDNS is unreliable on corporate networks and VPNs, and pairing works identically over LAN, VPN, and
overlay networks.

Transport is a **persistent WebSocket over TLS** with the fingerprint pinned at Pairing (no PKI, no
CA, no expiry cliff) and a bearer capability derived from the shared secret. Persistent because the
Roster is live: polling would lag the fleet view and delay Commands.

Carried: Roster, Status, Activity Summary, Commands (one hop). **Not carried: PTY frames.** To watch
a remote Agent's screen, open the UI against that Machine's Hub. A partitioned Peer's rows are marked
stale, never dropped, and local supervision is unaffected. *(ADR-0001, D8, D22)*

Agent identity is **Machine-qualified**, because derived session names (`wexpert-2-a1`) collide
across boxes.

### Workstreams

A durable thread keyed by **Repository + current branch**. A session that switches branch *moves* to
the new branch's Workstream — deliberate, because the `pre-impl` flow cuts a feature branch
mid-session and the effort genuinely moves with it.

**Repository means the normalized git remote URL** — identity, not location — so the same repo on two
Machines is one Workstream and a branch's history is continuous across the fleet. Falls back to
Machine + absolute path when there is no remote; those Workstreams simply never span Machines.

Storage is a **bounded session index**: session id, Machine, time range, final Activity Summary, and
a *pointer* to each transcript — never a copy. Older entries prune. A Workstream whose branch and
Repository are both gone is offered for cleanup. *(ADR-0006, D29)*

### The Browser UI

Kestrel serves a React + TypeScript + Vite SPA; a JSON WebSocket carries live Roster updates and a
separate binary WebSocket carries terminal bytes. Launched as a **chromeless app window** by default
(`--browser` opts into a full browser) — inherited from WExpert's existing default.

Two panes: **Roster** above, **terminal** below (dockable bottom/right, persisted).

### Security Model

- **Loopback is not authorization.** Capability tokens are per-client, verified per call, and
  compared in constant time. Exact `Origin`/`Host` guards on every WebSocket; no CORS; DNS-rebinding
  hosts rejected even from a loopback peer.
- **Forbidden vs not-found is never conflated** — leaking existence is a real bug.
- **Secrets** live only in ACL-protected files, never in argv, URLs, logs, telemetry, or a terminal
  transcript.
- **Teardown revokes before it kills**, so no surviving descendant holds a live token.
- **Federation can trigger code execution on the far Machine** (an Agent runs what it is told). It is
  designed as such: explicit pairing, pinned certificate, derived bearer capability.

### Ambient Attention

The Hub raises an OS notification on transition into attention-needed, click-to-focus with the row
selected. Fleet-level, so it covers remote Machines — which per-session Claude Code notifications
cannot. No tray process: the detached Hub runs with a hidden console and no message pump. *(D11)*

### Model Backend

A configurable inference provider used wherever a Hub capability needs a model — first use is
optional Activity Summary enrichment. **Anthropic/Claude by default; a local endpoint (e.g. LM Studio)
is a first-class alternative.** Sending transcript-derived content off-machine is an explicit,
visible setting (NFR-8).

### Packaging

A single .NET global tool. One binary, two startup personalities: the austere fast path for
`mcp`/`hook`, the full path for everything else.

### Skills

`skills/` is the tracked source of truth, deployed to harnesses by `supervisor skills install` — never
read in place. `SKILL.md` is a cross-harness standard, so deployment is a copy, not a conversion. The
`supervisor` skill is a content-free bootstrap stub routing to `supervisor agent-guide --json`, so
instruction content versions with the binary and a stale deployed skill can never contradict the tool.

### Observability

Telemetry context and spans over Hub operations. Deliberately modest — this is a single-developer
tool, and the Roster itself is the primary diagnostic surface.

---

## Data Flow

### Enrollment → Roster

```
claude session starts
  └─ harness spawns  supervisor mcp  (user-scope stdio MCP server)   ← austere path, ~130ms
       └─ start-or-attach Hub via host.json  (serialized; fail open on any error)
            └─ register Agent: sessionId, pid, cwd → Repository, Machine
  └─ SessionStart hook          → lifecycle pinned
  └─ UserPromptSubmit hook      → baseline Activity Summary
Hub tails ~/.claude/projects/<slug>/<sessionId>.jsonl → live Activity Summary + Subagent count
Hub polls  claude agents --json → diff → Unenrolled Agent rows
Hub pushes Roster deltas over the JSON WebSocket → UI top pane
```

### Command → Agent

```
originator (human in UI | CLI in any shell | Agent via MCP tool)
  └─ Control Service: authorize, attribute, hop-count, TTL, replace Pending Command
       ├─ Owned   → write into the Hub-owned ConPTY            → delivered immediately
       ├─ Foreign → held as Pending Command                    → collected on next tool call
       └─ remote  → forwarded over the Peer link (one hop)     → repeats on that Hub
  └─ state reported back: queued | delivered | acknowledged | expired | refused
```

### Launch → Owned Agent

```
UI: Start Claude → pick Repository (learned from Roster) + options (remembered per Repository)
  └─ TerminalSessionManager: mint terminalId + capability, create Job Object
       └─ ConPTY child: claude, cwd = chosen Repository
            └─ that session enrolls normally → Roster row, tier = Owned
```

---

## Testing Strategy

The system is multi-process and multi-machine, so its real defects are integration defects — and the
things that make it hard to test (a real Claude session needs auth and burns tokens; a PTY is
platform-bound; federation needs two boxes) are exactly the things that must not be in the test loop.

**Fakes at the edges, real transports in the middle.**

| Seam | Replaces | So that |
|---|---|---|
| `FakeAgent` | An enrolled Claude session | Enrollment and Commands are testable without auth or tokens |
| `FakePeer` | Another Machine's Hub | Federation is testable on one box |
| `FakeTerminalProcess` | A ConPTY child | Terminal logic is testable without a real PTY |
| `FakeClock` | Wall time | TTL, grace, idle, and staleness tests never sleep |
| Transcript fixtures | A live session's JSONL | Activity Summary derivation has stable input |

Hermetic end-to-end scenarios drive the **real** WebSocket and MCP transports against those fakes, so
the wire protocol is genuinely exercised. Real-Claude runs, real-PTY runs, and two-machine runs are
**named developer smokes** — written down, performed deliberately, and never CI dependencies.
post-impl reconciles the plan's test plan against what was actually tested, and reports anything
promised-but-not-delivered rather than letting it evaporate.

**Fail-open makes silence ambiguous.** A session starting normally proves nothing about enrollment,
because a broken shim exits 0 by design. Tests assert both the exit code *and* that the failure was
recorded somewhere observable.

---

## Conventions

- **Domain terms come from `CONTEXT.md`,** which lists banned aliases. `session` for **Agent**,
  `supervisor` for **Hub**, and `remote agent` for **Foreign Agent** are all wrong — the last
  especially, since *remote* already means *on another Machine*.
- **Spectre branch-leaf `--json`** is not inherited; every leaf settings class redeclares it. The
  symptom is empty JSON output, not a parse error, so it survives until a test compares payloads.
- **Rich output injects `IAnsiConsole`**; `CommandAppTester` does not capture the static `AnsiConsole`.
- **`dotnet test --no-build` can report green over a failed build.** The usual cause here is a running
  Hub holding `Supervisor.Web.dll` (`MSB3021`). `supervisor hub stop` before building.
- **Never auto-restart the Hub** to resolve version skew — it would kill live terminals. Refuse,
  diagnose, and let the developer choose the moment. *(D28)*
