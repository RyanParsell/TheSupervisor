# TheSupervisor — Product Requirements

**Status:** Living document. Updated by the post-impl skill as capability ships.
**Domain language:** [`CONTEXT.md`](../../CONTEXT.md) · **Decisions:** [`docs/adr/`](../adr/) · **Origin:** [`docs/plans/design.md`](../plans/design.md)

## Grounding Index

Route to the sections you need; do not read this document end to end.

| If the work touches… | Read |
|---|---|
| How Agents join the Roster | FR-1, FR-4, NFR-1, NFR-2, NFR-4 |
| What the Roster shows | FR-2, FR-3, NFR-8 |
| Steering, stopping, or launching Agents | FR-5, FR-7, FR-8, NFR-3 |
| The in-app terminal | FR-6, NFR-7 |
| Multiple machines | FR-9, NFR-3, NFR-4 |
| Continuity across restarts | FR-10, NFR-5 |
| Notifications and ambient presence | FR-11 |
| Install, upgrade, or repair | FR-12, NFR-1, NFR-4 |
| Anything at all | Problem Statement, Product Vision, Target Users |

---

## Problem Statement

A developer working with AI coding agents does not run one. They run several — across editor
windows, across repositories, and increasingly across machines. Each one is a separate context
window with its own state, its own working directory, and its own idea of what it is doing.

Nothing tells you about them collectively. Concretely, on a single machine right now it is normal to
have four sessions running where **one has been blocked on a question for twenty minutes** and
nothing surfaced that. The information exists — Claude Code tracks per-session status — but there is
no product over it: no aggregate view, no way to act on one from another, and nothing at all across
machines.

The cost is not theoretical. Unattended agents idle while waiting on input you didn't know they
needed; work is duplicated because two windows are on the same branch; and the developer becomes the
message bus between their own agents, hand-carrying context from one window to the next.

## Product Vision

**One place that knows about every agent you have running, and can act on any of them — reachable
from that place or from inside any agent.**

TheSupervisor is a CLI, a skill, and a local UI over a per-machine **Hub**. Hubs federate, so the
fleet spans machines while every Agent still only ever talks to loopback. The UI is two panes: a
**Roster** of every Agent and what it is working on, above a **terminal** that launches PowerShell
and Claude — and launching an Agent there is what makes it fully steerable.

Three commitments shape everything:

1. **Enrollment is involuntary.** Install once per machine; every session afterwards enrolls itself.
   A supervisor that depends on agents remembering to check in has exactly the wrong failure mode.
2. **Agents and humans have equal authority.** The CLI works identically from the UI, from a shell,
   and from inside any Agent — so the fleet can coordinate itself, not just be watched.
3. **It never gets in the way.** A broken TheSupervisor is invisible to a working session.

## Target Users

**One developer, on their own machines.** The trust model assumes every Agent in the Fleet belongs
to the same person. There is no multi-user story, no team fleet, and no shared Hub — those are not
deferred features, they are outside the boundary.

---

## Functional Requirements

### FR-1 — Involuntary enrollment
Installing TheSupervisor is a one-time, machine-level act. Every Claude Code session started
afterwards joins the Roster with no per-session action by the user or the model, via a user-scope
stdio MCP server plus lifecycle hooks. The skill is not the enrollment path. *(ADR-0002, D2–D4)*

### FR-2 — The Roster
The UI presents one Roster covering the whole Fleet — every Agent on this Machine and on every
paired Peer — showing Repository, name, Status, Activity Summary, tier, and Subagent count. It is a
flat list ordered by attention needed: waiting, then errored/stopped, then busy, then idle, then
unenrolled; recency breaks ties. *(D23, D24)*

### FR-3 — Activity Summary
Every Agent always has a one-line answer to "what is this working on." It is derived involuntarily —
the submitted prompt as baseline, kept current by the Hub tailing the Agent's transcript — carries an
age indicator, and may be replaced by a better line the Agent posts through the skill. A silent Agent
still shows something true. *(ADR-0005, D5, D18)*

### FR-4 — Unenrolled visibility
A session running on a Machine but not enrolled appears in the Roster as an **Unenrolled Agent** —
visible, honestly labelled, never controllable, and never silently omitted. Detected by diffing the
Roster against `claude agents --json`. *(D6)*

### FR-5 — Tiered control
An Agent launched from TheSupervisor's terminal is **Owned**: input can be injected, it can be
answered when blocked, and it can be stopped gracefully. An Agent that enrolled from elsewhere is
**Foreign**: fully observable, cooperatively controllable, and stoppable only by hard kill. The UI
never offers an action it cannot perform on that row. *(ADR-0003, D15, D25)*

### FR-6 — In-app terminal
The lower pane is a real interactive Windows pseudoterminal with **Start PowerShell** and **Start
Claude**, tabbed, surviving navigation and refresh. The launch Repository is chosen from those the
Roster has seen, most-recent first, with per-Repository launch options remembered. *(D12)*

### FR-7 — One control surface, three ways in
Every operation is available as a CLI verb, as an MCP tool, and from the UI — all delegating to one
shared control service so the surfaces cannot drift. The CLI behaves identically inside an Agent,
in any shell, and against a remote Machine. *(D14)*

### FR-8 — Equal authority with structural guards
Agents may issue the same Commands a human can, against any Agent on any paired Machine. Safety is
structural, not permission-based: a hop count, a per-originator rate limit, and originator
attribution on every Command. Agents may **not** choose the permission mode of an Agent they launch.
*(ADR-0004, D13)*

### FR-9 — Federation by explicit pairing
Machines federate only through a deliberate `peer pair` exchange recording endpoint, shared secret,
and certificate fingerprint on both sides. Federation carries Roster, Status, Activity Summary, and
Commands. **Live terminal output never crosses a Machine boundary.** *(ADR-0001, D8, D9, D22)*

### FR-10 — Workstreams
A Workstream — Repository plus current branch — gives effort continuity across Agent restarts.
A session that switches branch moves to the new branch's Workstream. Workstreams never occupy Roster
rows; they appear as context on a live Agent's row and as a detail view. *(ADR-0006, D26, D27)*

### FR-11 — Ambient attention
The Hub raises an OS notification when an Agent transitions into needing attention, so a blocked
Agent is noticed without watching the window. Fleet-level, so it covers remote Machines — which
per-session notifications never can. Clicking focuses the window with that row selected. *(D11)*

### FR-12 — Install, repair, and diagnose
`supervisor install` is idempotent: it backs up settings, merges only its own marked block, registers
the MCP server at user scope, and verifies. `supervisor uninstall` removes exactly what was added.
`supervisor doctor` reports live state — registered, hooks present, Hub reachable, enrollment
working — and repairs drift after a Claude Code upgrade. *(D20)*

---

## Non-Functional Requirements

### NFR-1 — Fail open, always
A failure anywhere in TheSupervisor must be invisible inside a working Claude session. The MCP shim
exits 0 silently on any failure; hooks always exit 0. Failures surface **only** in TheSupervisor's
own surfaces: unenrolled Roster rows and `supervisor doctor`. TheSupervisor must never be the reason
a developer cannot work. *(D19)*

### NFR-2 — Per-session startup budget
The `mcp` and `hook` verbs take an austere startup path — no update check, telemetry init, config
scan, or banner — held under roughly 200 ms by a CI-enforced budget test. Measured baseline: a
minimal .NET console starts in ~128 ms; a heavyweight CLI preamble costs ~3,500 ms, which multiplied
across every session and every prompt is a tax the developer feels all day. *(D10)*

### NFR-3 — Security
Loopback is not authorization. Capability tokens are scoped per client and verified per call; the
forbidden-vs-not-found distinction is preserved rather than conflated; secrets live only in
ACL-protected files and never appear in argv, URLs, logs, telemetry, or a terminal transcript;
constant-time comparison throughout. Peer links pin the certificate fingerprint exchanged at Pairing.
Federation can trigger code execution on the far Machine, so it is treated as such.

### NFR-4 — Contract versioning independent of build
The enrollment contract and the Peer protocol carry their own versions, kept additive-only, so a
routine tool upgrade does not de-enrol running Agents. Only a genuine protocol mismatch refuses, and
it refuses silently per NFR-1. The Hub is never auto-restarted — that would kill live terminals.
*(D28)*

### NFR-5 — Bounded storage
Runtime state is ephemeral across Hub restarts; only Pairings, learned launch targets, Workstream
indexes, and preferences persist. A Workstream stores a bounded session index with *pointers* to
transcripts, never copies. Nothing in TheSupervisor grows without limit. *(D17, D29)*

### NFR-6 — Testability by construction
The system is multi-process and multi-machine, so its real defects are integration defects. Seams
that let those be tested without a real Claude session, a real PTY, or a second machine — fake Agent,
fake Peer, fake PTY, injected clock, transcript fixtures — are architectural requirements, designed
before the code that needs them. Tests use fakes at the edges and real transports in the middle.
Real-Claude and two-machine runs are named developer smokes, never CI dependencies.

### NFR-7 — Platform
The terminal is Windows-only, following the ConPTY implementation being adapted. Other platforms
retain the rest of the product and render a defined terminal-unavailable state rather than failing.

### NFR-8 — Privacy
The Hub reads Agent transcripts to derive Activity Summaries. On-machine this is the developer's own
data. If the **Model Backend** is used to enrich summaries, conversation content leaves the machine
unless a local endpoint is configured — so the Model Backend is configurable, defaults to Anthropic,
supports a local alternative, and any off-machine use is an explicit, visible setting. *(ADR-0005)*

---

## Out of scope

- Linux/macOS pseudoterminals.
- Live terminal relay across Machines — deliberately excluded, not deferred (FR-9).
- Supervising other vendors' agents. The enrollment contract stays harness-agnostic so this can be
  added without redesign, but nothing is built for it.
- Persistent transcripts, replay, or search of terminal contents.
- A tray process, Windows service, or always-on supervisor of the Hub itself.
- Crash recovery of in-flight Commands or terminal state.
- Multi-user or team-wide fleets.
