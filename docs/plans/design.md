# TheSupervisor — design

**Date:** 2026-07-27
**Status:** Design agreed (grill-with-docs session). Not yet planned into implementable phases.
**Domain language:** [`CONTEXT.md`](../../CONTEXT.md)
**Decisions:** [`docs/adr/`](../adr/)

---

## 1. What this is

A CLI, a skill, and a local UI for supervising the AI coding agents one developer has running
concurrently — across editor windows, repositories, and machines — from any one of them.

Two surfaces, mirroring WExpert:

- **A CLI** with stdio and MCP capabilities, usable from inside any agent or any shell.
- **A UI** with two panes: a **Roster** of every running agent and what it's working on, above a
  **terminal** that launches PowerShell and Claude.

The endgame is a fleet spanning multiple VS Code windows, multiple windows against one repository,
multiple repositories on a machine, and multiple machines on a network — manageable from the UI or
from the CLI inside any agent.

---

## 2. What we found before designing

Four investigations changed the design materially. Recording them because each one would otherwise
be rediscovered expensively.

### 2.1 Claude Code already publishes a roster

`~/.claude/sessions/<pid>.json` is written and updated live per interactive session:

```json
{"pid":28868,"sessionId":"cd835d81-…","cwd":"C:\\Code\\Personal\\TheSupervisor",
 "name":"thesupervisor-3b","status":"busy","peerProtocol":1,"kind":"interactive",
 "waitingFor":"input needed","statusUpdatedAt":1785175542600}
```

More importantly, **`claude agents --json` is a supported, scriptable surface** over the same data —
documented as "does not require a TTY (for scripting)", with `--all` and `--cwd` filters, covering
interactive *and* background sessions. This is a stable contract, not internal-file scraping.

What it does **not** provide, and why Enrollment still exists:

- it is local-machine only;
- `status` is a coarse `busy`/`idle`/`waiting` enum, never an Activity Summary;
- it is strictly read-only — there is no control channel.

**Consequence:** it becomes the Roster's completeness backstop (§5, D6), not its source.

### 2.2 You cannot inject a turn into a session you didn't launch

| Channel | Reaches | Limit |
|---|---|---|
| Write to a PTY we own | Agents we launched | Full fidelity — but only those |
| MCP tool result | Any enrolled Agent | Agent must *call* a tool; cannot wake an idle one |
| `UserPromptSubmit` hook context | Any enrolled Agent | Only fires when the human types |

This is the origin of the **Owned vs Foreign** split (ADR-0003) and it promotes the terminal pane
from a convenience to load-bearing architecture: launching an Agent there is the *only* way to make
it fully controllable.

### 2.3 .NET startup is fine; WExpert's startup is not

Measured on this machine:

| Binary | Median startup |
|---|---|
| `wexpert.exe --version` | **~3,500 ms** warm (15,000 ms cold) |
| Minimal .NET 10 console, published | **128 ms** |

The 3.5s is WExpert's own preamble — update check, telemetry init, config scan, banner — not the
runtime. Since TheSupervisor spawns an MCP child **per Claude session** and runs hooks on **every
prompt**, that preamble is exactly what must not be copied. Hence D10: an austere fast path with a
CI-enforced startup budget.

### 2.4 The detached host needs a real console

From WExpert's `UiHostLauncher.cs` and its `2026-07-26-bug-detached-terminal-openconsole` fix: a
windowless (`CreateNoWindow=true`) process **cannot bind a pseudoconsole child**, so the in-app
terminal comes up dead. The working form is `UseShellExecute=true` + `WindowStyle=Hidden`, which
gives the host its own real, hidden console. We inherit the fix rather than rediscovering it.

### 2.5 `peerProtocol: 1` is a read contract, not a message channel

Session files advertise `peerProtocol: 1`, which suggested a native cross-session mechanism worth
building on. Investigation found **no IPC endpoint for interactive sessions** — enumerating named
pipes while four sessions were live turned up nothing. Only the background-agent daemon creates
pipes, and only for its own workers. The field versions the *session-file read contract* that other
processes may parse. **MCP tool-call pickup therefore remains the only route to a Foreign Agent**,
and WU-4 is unaffected.

### 2.6 Background agents publish attachable PTYs — and we decline them

When the daemon runs, `~/.claude/daemon/roster.json` publishes per worker a `rendezvousSock` and
`ptySock` named pipe plus `rvAuth`/`ptyAuth` tokens, all **user-readable**. A process running as the
user could in principle attach to a background Agent it did not launch. We deliberately do not
(ADR-0003, "Challenged and upheld"): it would make control depend on an undocumented pipe protocol
and produce UI actions that silently break on upgrade.

Incidentally, `daemon.log` prefixes every line `[supervisor]` — further justification for **Hub**.

### 2.7 Transcripts carry everything a live summary needs

`~/.claude/projects/<slug>/<sessionId>.jsonl` is appended at turn boundaries and contains role,
assistant text, and `tool_use` entries with tool names. That is sufficient to derive a current
activity line at **zero cost to the Agent**, versus roughly 130–200 ms per tool call for a
`PreToolUse` hook. See ADR-0005.

---

## 3. Prior art — what we lift from WExpert

Source: `C:\Code\MS\CLI`. Strategy agreed: **copy-adapt aggressively**.

| Layer | Files | Fit |
|---|---|---|
| ConPTY | `WExpert.Web/Terminal/ConPty/{ConPtyNativeMethods,ConPtyTerminalProcess,ConPtyTerminalProcessFactory,WindowsJobObject}.cs` | Near-verbatim |
| Terminal session | `WExpert.Web/Terminal/{TerminalSessionManager,TerminalSession,TerminalWebSocketHandler,TerminalTransport,WebSocketTerminalTransport,TerminalContracts}.cs` | Near-verbatim |
| Host + rendezvous | `WExpert.Web/{WebHost,UiHostRendezvous}.cs`, `WExpert/Commands/Ui/{UiHostLauncher,HttpUiHostClient,IUiHostClient,UiHostVersion}.cs` | Adapt — Hub replaces UI host |
| Browser terminal | `WebUI/src/terminal/{terminalSocket,terminalPrefs}.ts`, `components/{TerminalPanel,XTerminal,TerminalSettings}.tsx`, `hooks/{useTerminalPanel,useTerminalSize}.ts` | Near-verbatim |
| Control plane shape | Co-hosted Streamable HTTP MCP, per-client capability tokens, `UiControlService` shared by CLI + MCP | Adapt — pattern, not code |
| Test scaffolding | `WExpert.Tests/Fakes/{FakeTerminalProcess,FakeTerminalTransport}.cs`, `Web/Terminal/*Tests.cs` | Near-verbatim |

**Where copy-adapt breaks — and must be inverted:**

- **WExpert D18** locks every terminal to the invocation directory and explicitly rules out a folder
  picker. TheSupervisor's premise is many repos, so it needs a launch-target picker (D12).
- **WExpert's terminal transport is loopback-only by construction.** Federation must not relay PTY
  frames across machines (D8).
- **WExpert's startup path** is 3.5s. The per-session path must not inherit it (D10).

---

## 4. Product contract

1. Installing TheSupervisor is a **one-time, machine-level act**. Every Claude session started
   afterwards enrolls itself with no per-session action by the user or the model.
2. The UI presents one **Roster** covering the whole **Fleet** — every Agent on this machine and on
   every paired **Peer** — with repository, Status, and Activity Summary per row.
3. Every Agent always has an Activity Summary. A silent Agent falls back to what it was last asked
   to do; a cooperative Agent may replace that with something better via the skill.
4. A session running on a machine but not enrolled appears as an **Unenrolled Agent** — visible and
   honestly labelled, never silently omitted.
5. An Agent launched from TheSupervisor's terminal is **Owned**: input can be injected, it can be
   answered when blocked, and it can be stopped. An Agent that enrolled from elsewhere is
   **Foreign**: fully observable, cooperatively controllable only. The UI never offers an action it
   cannot perform on that row.
6. The bottom pane is a real interactive Windows pseudoterminal with **Start PowerShell** and
   **Start Claude**, tabbed, with the launch repository chosen from recently-seen repositories.
7. The same operations are available as CLI verbs and as MCP tools, from inside any Agent, from any
   shell, and from the UI — all delegating to one shared service.
8. Agents and humans have **equal authority** over the Fleet. Agent-originated Commands are rate
   limited, hop-counted, and attributed to their originator.
9. Machines are federated only by **explicit Pairing**. Nothing is trusted implicitly.
10. Live terminal output never crosses a machine boundary. To watch a remote Agent's screen, open
    the UI against that machine's Hub.
11. The Hub raises an OS notification when an Agent needs attention, so a blocked Agent is noticed
    without watching the window.
12. Where the Hub needs inference, the **Model Backend** is configurable — Anthropic/Claude by
    default, a local endpoint such as LM Studio as a supported alternative.
13. Windows-only for the terminal, following WExpert. Other platforms get a defined
    terminal-unavailable state.

---

## 5. Locked decisions

| # | Decision | Source |
|---|---|---|
| D1 | An **Agent** is one enrolled Claude Code session, interactive or background. **Subagents** are never rows; they roll up onto their parent. | Q3 |
| D2 | **Enrollment is involuntary**, performed by the harness: the CLI is installed once as a user-scope stdio MCP server, so every session auto-connects at start. | ADR-0002 |
| D3 | `SessionStart`/`SessionEnd` hooks pin lifecycle deterministically; `UserPromptSubmit` captures the baseline Activity Summary. | Q2, Q4 |
| D4 | The skill is **not** the enrollment path. It exists only for rich, model-authored verbs on top. | Q2 |
| D5 | Activity Summary is layered: submitted prompt → active task/tool → optional model override. Always present, degrades gracefully. | Q4 |
| D6 | Each Hub polls `claude agents --json` on its own machine and diffs it against the Roster; unmatched local sessions show as **Unenrolled Agents**. | Q6 |
| D7 | **One Hub per machine**, loopback-only for Agents; Hubs federate to present one Fleet. | ADR-0001 |
| D8 | Federation carries Roster, Status, Activity Summary, and Commands. It does **not** carry PTY frames. | Q8 |
| D9 | Peers are established by **explicit Pairing** with a pre-shared 256-bit secret. No broadcast discovery, no implicit trust. | Q9 |
| D10 | One .NET tool. The `mcp` and `hook` verbs take an austere startup path — no update check, telemetry, config scan, or banner — with a CI-enforced budget (~200 ms). | Q10 |
| D11 | The UI is a chromeless app window by default (WExpert's existing default), with `--browser` to opt into a full browser. | Q11 |
| D12 | Launch targets are **learned from the Roster** — every cwd an Agent has enrolled from, most-recent first, plus browse. Launch options are remembered per repository. Inverts WExpert D18. | Q12 |
| D13 | Agents and humans have equal authority; loop safety is structural (hop count + rate limit + originator attribution). | ADR-0004 |
| D14 | The CLI surface and the MCP tool surface delegate to **one shared control service** so they cannot drift. | Q13 / WExpert precedent |
| D15 | Ownership is fixed at launch. A Foreign Agent can never be promoted to Owned. | ADR-0003 |
| D16 | The detached Hub is spawned `UseShellExecute=true` + `WindowStyle=Hidden` so it owns a real hidden console and can bind pseudoconsole children. | §2.4 |
| D17 | Runtime state is ephemeral across Hub restarts. Only Pairings, learned launch targets, and preferences persist. | WExpert D32 precedent |
| D18 | The Activity Summary is kept current by the **Hub tailing each Agent's transcript**, never by per-tool-call hooks. Rows carry an age indicator so a stuck Agent reads as stuck. | ADR-0005 |
| D19 | **Fail open, always.** The MCP shim performs start-or-attach (serialized, per WExpert's rendezvous) and exits 0 silently on any failure; hooks always exit 0. A broken TheSupervisor is never felt inside a working session. Failures surface only in the Roster's unenrolled rows and `supervisor doctor`. | Q17 |
| D20 | Install is a single idempotent `supervisor install`: timestamped settings backup, a **marked block** for our hook entries, user-scope MCP registration, verification. `supervisor uninstall` removes exactly what it added; `supervisor doctor` reports live state and repairs drift after a Claude Code upgrade. | Q18 |
| D21 | A **Command** carries a TTL (default ~15 min, overridable) and an explicit state — queued, delivered, acknowledged, expired, refused — reported back to its originator. Queue depth per Agent is **1**: a new Command replaces the pending one, so a woken Agent acts on latest intent, not a stale backlog. | Q19 |
| D22 | The Peer link is a **persistent WebSocket over TLS**, with a self-signed certificate per Hub whose fingerprint is pinned at Pairing, and a bearer capability derived from the pre-shared secret. No PKI, no CA, no expiry cliff. | Q20 |
| D23 | The Roster is a **flat list ordered by attention needed** — waiting, then errored/stopped, then busy, then idle, then unenrolled; recency breaks ties. Repository, machine, and tier are columns, not structure. | Q21 |
| D24 | **Subagents get a count badge only.** No separate rollup model — the transcript-derived Activity Summary already reflects fan-out. | Q22 |
| D25 | **Stop is tiered.** Owned Agents get a graceful ladder (Ctrl+C → exit instruction → Job Object kill on timeout). Foreign Agents get a hard kill by pid, presented as visibly distinct and confirmed. ADR-0003 governs *steering*, not lifecycle. | Q23 |
| D26 | A **Workstream** — repository + current branch — gives effort continuity across Agent restarts. A session that switches branch moves to the new branch's Workstream. | ADR-0006 |
| D27 | The Roster shows **live Agents only**. A Workstream is context on the row ("feature/x · 4th session") and a detail view, never a row of its own. | Q26 |
| D28 | The **enrollment contract is versioned independently of the build** and kept additive-only, so routine tool upgrades don't de-enrol running Agents. Only a genuine protocol mismatch refuses, silently per D19, diagnosed by `supervisor doctor`. The Hub is never auto-restarted (WExpert D27 precedent — it would kill live terminals). | Q27 |
| D29 | A Workstream persists a **bounded session index** — session id, machine, time range, final Activity Summary, and a *pointer* to each transcript, never a copy. Older entries prune; a Workstream whose branch and repository are both gone is offered for cleanup. | Q28 |

---

## 6. Architecture

```text
Machine A                                          Machine B
─────────────────────────────────────────          ──────────────────────────
  claude session ──stdio MCP──┐                      claude session ──┐
  claude session ──stdio MCP──┤                      claude session ──┤
  hooks (SessionStart/End,    │                      hooks ───────────┤
        UserPromptSubmit) ────┤                                       │
                              ▼                                       ▼
                     ┌──────────────────┐   paired,          ┌──────────────────┐
                     │      HUB A       │◄──shared secret───►│      HUB B       │
                     │                  │   Roster+Commands  │                  │
                     │  Roster          │   (never PTY)      │  Roster          │
                     │  ControlService  │                    │  ControlService  │
                     │  TerminalMgr     │                    │  TerminalMgr     │
                     │  Kestrel + WS    │                    └──────────────────┘
                     │  MCP (HTTP)      │
                     │  host.json       │
                     └────────┬─────────┘
                              │ loopback
              ┌───────────────┼────────────────┐
              ▼               ▼                ▼
        chromeless UI    supervisor CLI    ConPTY terminals
        ┌───────────┐    (from any agent)   ├─ PowerShell
        │  ROSTER   │                       └─ claude  ──► Owned Agent
        ├───────────┤
        │ TERMINAL  │
        └───────────┘
```

**Identity and lifetime** (four distinct things, following WExpert D23):

- **Hub** — one per user per machine; outlives every Agent and UI client.
- **Agent** — one Claude session; enrolled at start, gone at exit.
- **UI client** — one browser tab; owns terminals, expires after a reconnect grace.
- **Terminal** — one ConPTY in a kill-on-close Job Object; owned by a UI client.

Agent identity is **machine-qualified** from the first commit (ADR-0001) — two machines will host
Agents with colliding derived names like `wexpert-2-a1`.

---

## 7. Work units

Ordered so the **unproven** mechanism is attacked first. ConPTY is proven-by-copy; enrollment is not.

### WU-0 — Hub host and rendezvous
Copy-adapt `WebHost`, `UiHostRendezvous`, `UiHostLauncher`, `HttpUiHostClient`. Start-or-attach
detached Hub, ACL-protected `host.json` (host id + 256-bit secret + pid/endpoint/version), liveness
probe, stale cleanup, `hub status` / `hub stop`, version-skew refusal. Spawn per D16.

### WU-1 — Enrollment and the Roster *(first shippable milestone)*
The user-scope stdio MCP server and its austere startup path (D10) with a CI startup-budget test.
`SessionStart`/`SessionEnd` hooks; `UserPromptSubmit` for the baseline Activity Summary. Start-or-
attach with concurrent-start serialization, and the fail-open posture throughout (D19). Transcript
tailing for summary currency, with age indicators and soft degradation to the prompt baseline (D18).
The Roster with Status, attention ordering (D23), and the Subagent count badge (D24). The
`claude agents --json` backstop and the Unenrolled Agent state (D6). `supervisor list`.
`supervisor install` / `uninstall` / `doctor` (D20).

**Ships as:** the UI's top pane, live over WebSocket, showing every Agent on this machine.

### WU-2 — UI shell and Roster pane
Copy-adapt the Kestrel + React/Vite + WebSocket shell and the chromeless launch (D11). Roster pane:
repository, name, Status, Activity Summary with age, tier badge, Subagent count. Attention ordering
(D23). Row selection and detail.

### WU-3 — Terminal pane
Copy-adapt the whole ConPTY stack and the browser terminal components. Tabbed panel, dock
bottom/right, stable-size first fit, bounded backpressure, Job Object teardown, `beforeunload` while
live. **Start PowerShell** and **Start Claude** with the learned launch-target picker (D12).
Agents launched here are Owned.

### WU-4 — Control
The shared control service (D14) behind both CLI verbs and MCP tools: `list`, `show`, `send`,
`launch`, `stop`, `status`. Owned/Foreign gating so the UI never offers an impossible action.
The Command lifecycle — TTL, five states, depth-1 replacing queue, originator reporting (D21).
Tiered stop: graceful ladder for Owned, confirmed hard kill for Foreign (D25).
Queued-command pickup for Foreign Agents. Hop counting, rate limiting, attribution (D13).

### WU-4b — Workstreams
Repository + branch identification with branch-switch migration (D26). The bounded session index
with transcript pointers and pruning (D29). Row context and the Workstream detail view (D27).
Cleanup for Workstreams whose branch and repository are both gone.
Sequenced after WU-4 because it depends on stable Agent identity and the transcript tail, and
because the first milestone must not wait on durable storage.

### WU-5 — Federation
`supervisor peer pair|list|remove` with the pre-shared-secret exchange and certificate-fingerprint
pinning (D9, D22). Persistent WSS Peer link with reconnect across sleep/wake. Roster merge across
Peers with machine-qualified identity. Command forwarding with one-hop limit. Peer version-skew
refusal, mirroring WExpert's `UI_HOST_VERSION_MISMATCH`. Partition behaviour: own Agents plus a
clearly-stale Peer view, never silent row loss.

### WU-6 — Ambient and enrichment
OS toast on attention-needed transitions, click-to-focus (D11). Configurable Model Backend and the
optional model-authored summary enrichment (D12 of CONTEXT / Q4 override path).

### The skill
Thin by design (D4). Teaches an Agent the verbs it gains from enrollment: post a better Activity
Summary, inspect the Fleet, dispatch to another Agent, and — critically — that a Foreign Agent
cannot be woken, so a command to one is queued rather than immediate.

---

## 8. Verification

1. Four concurrently running Claude sessions all appear in the Roster with correct repository,
   Status, and a non-empty Activity Summary, without any of them having run the skill.
2. Killing a session removes its row within the reconnect grace; a crashed session does not leak.
3. A session started with the MCP registration removed appears as an **Unenrolled Agent**, not as a
   missing row.
4. The `mcp` and `hook` fast paths stay under the CI startup budget; the test fails if a dependency
   drags the preamble back in.
5. An Owned Agent blocked on input can be answered from the UI and resumes.
6. A Foreign Agent shows no injection action; a command to it is queued and picked up on its next
   tool call.
7. Two paired machines present one merged Roster; unpairing removes the Peer's rows immediately.
8. A partitioned Peer's rows are marked stale rather than dropped, and local supervision is
   unaffected.
9. An Agent-originated command reaching its hop limit is refused and attributed.
10. Closing the launching terminal does not stop the Hub; closing a UI tab kills only its terminals;
    `hub stop` tears everything down.
11. No secret appears in argv, URL query, logs, telemetry, or a terminal transcript.
12. Process inspection after terminal close shows no surviving shell or Claude descendant.
13. **Fail-open proof:** with the Hub binary deleted and the Hub unreachable, a Claude session starts
    normally, shows no error, and loses no functionality — the only visible effect is that the
    Roster reports it unenrolled.
14. `supervisor uninstall` restores `settings.json` to a state diffing clean against the pre-install
    backup, leaving no orphaned hook entries pointing at a removed binary.
15. A Command to an Agent that never wakes expires at its TTL, is never delivered, and is reported
    expired to its originator.
16. Sending a second Command to an Agent with one pending replaces it; the woken Agent executes only
    the later one.
17. A Peer presenting a certificate whose fingerprint was not pinned at Pairing is refused.
18. The Roster's first row is always the Agent most needing attention, across machines.
19. Upgrading the tool while Agents are running does **not** de-enrol them, provided the enrollment
    protocol version is unchanged.
20. An Agent that cuts a feature branch mid-session moves to that branch's Workstream, and its
    previous Workstream retains the sessions that preceded the switch.
21. A Workstream's session index stays bounded under sustained use and never stores transcript
    contents — only pointers.
22. Hard-killing a Foreign Agent leaves its transcript intact and resumable via `claude --resume`.

---

## 9. Out of scope

- Linux/macOS pseudoterminals.
- Live terminal relay across machines (D8) — deliberately excluded.
- Supervising GitHub Copilot or other vendors' agents. The enrollment contract stays
  harness-agnostic so this can be added without redesign, but nothing is built for it.
- Persistent transcripts, replay, or search of terminal contents.
- A tray process, Windows service, or always-on supervisor of the Hub itself.
- Crash recovery of in-flight Commands or terminal state.
- Multi-user or team-wide fleets. The trust model assumes every Agent belongs to one person.

---

## 10. Open questions

Not blocking the first milestone. Six of the original open items were resolved by the 2026-07-27
grill (§2.5–2.7, D18–D24); these remain:

- **Plugin packaging.** Whether a Claude Code plugin can carry hooks and an MCP server registration
  is unverified — the local marketplace cache holds only metadata. If it can, some of D20's
  settings-merging could be delegated to the harness. The .NET binary needs `dotnet tool install`
  either way, so this is a refinement, not a redesign.
- **Terminal tab cap.** WExpert caps at six per UI client. Whether a supervisor needs more is
  untested and cheap to change.
- **Peer link liveness.** The persistent WSS reconnect policy across laptop sleep, VPN flap, and
  IP change is unspecified. Related: whether a Peer that has been unreachable for a long period is
  auto-dropped from the merged Roster or held indefinitely as stale.
- **Model Backend scope.** Agreed as configurable and Claude-by-default, but which capabilities
  actually call it — summary enrichment only, or future ones — is unscoped. Note the privacy
  consequence recorded in ADR-0005: enrichment sends conversation content off-machine unless a
  local backend is configured.
- **Skill contents.** The skill is deliberately thin (D4), but the exact verbs it teaches — and how
  it explains that a Foreign Agent's Command is queued rather than immediate — are unwritten.
