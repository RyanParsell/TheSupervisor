# TheSupervisor — Stories

**Status:** Living document. The post-impl skill appends a story per shipped change.
**Domain language:** [`CONTEXT.md`](../../CONTEXT.md) · **Requirements:** [`thesupervisor-prd.md`](thesupervisor-prd.md)

## How to use this document

Story IDs are **self-minting UTC timestamps**: `S-YYMMDD.HHMMSSx` — e.g. `S-260727.143012a`. Run
`date -u +'S-%y%m%d.%H%M%S'` once per post-impl run and append a per-story letter starting at `a`.
There is no shared counter to bump. Acceptance criteria derive as `AC-<id>.<n>`. IDs are permanent —
never renumber; supersede and link forward.

Stories are appended **under the `## Area:` section that owns them, in chronological order**, with
their ACs all `[x]` and a final AC carrying the full-suite test count. The `## Index` below is the
routing table — read it, pick the 1–3 areas your work touches, and read only those in full.

The area slugs here are 1:1 with the `area:` tag vocabulary in
[`docs/artifacts/README.md`](../artifacts/README.md). Adding an area means adding it in both places.

## Index

| Area | Slug | Stories | Summary |
|------|------|---------|---------|
| [Agent Enrollment & Lifecycle](#area-agent-enrollment--lifecycle) | `agent-enrollment` | S-260730.201447a | How an Agent joins the Roster and leaves it: the MCP shim, hooks, protocol versioning, install/uninstall/doctor, fail-open behavior. |
| [Roster & Fleet View](#area-roster--fleet-view) | `roster-ui` | _none yet_ | What the Roster shows and how it is ordered: Status, Activity Summary, transcript tailing, unenrolled rows, attention ordering, Subagent badges. |
| [Agent Control & Commands](#area-agent-control--commands) | `agent-control` | _none yet_ | Steering, stopping, and launching Agents: the shared control service, Owned/Foreign gating, Command lifecycle, hop and rate guards. |
| [In-App Terminal](#area-in-app-terminal) | `terminal` | _none yet_ | The ConPTY terminal pane: session management, Job Objects, transport, tabs, Start PowerShell / Start Claude, launch-target picking. |
| [Multi-Machine Federation](#area-multi-machine-federation) | `federation` | _none yet_ | Pairing, the Peer link, Roster merge across Machines, Command forwarding, partition behavior. |
| [Workstreams & Continuity](#area-workstreams--continuity) | `workstreams` | _none yet_ | Effort continuity across Agent restarts: Workstream identity, branch migration, the bounded session index. |
| [CLI UX & Output](#area-cli-ux--output) | `cli-ux` | _none yet_ | The command tree, output modes, error codes, and the human-facing ergonomics of the CLI. |
| [Agent Integration & Skills](#area-agent-integration--skills) | `agent-skills` | _none yet_ | The tracked `skills/` source, cross-harness deploy, the workflow trio, and the agent guide that supervised Agents read. |
| [Distribution & Packaging](#area-distribution--packaging) | `distribution-packaging` | _none yet_ | Global-tool packaging, install scripts, target-framework matrix, self-update. |
| [Infrastructure & Testing](#area-infrastructure--testing) | `infra-testing` | _none yet_ | Test seams and fakes, hermetic harnesses, CI, the startup-budget guard, meta-tests over the docs. |

---

## Area: Agent Enrollment & Lifecycle

Grounding: FR-1, FR-4, FR-12, NFR-1, NFR-2, NFR-4 · ADR-0002 · `arch:enrollment`

### S-260730.201447a — A Claude session enrols itself with no action from me

**As** a developer running several Claude sessions,
**I want** each one to join the fleet the moment it starts,
**so that** supervision does not depend on my remembering to do anything.

Covers WU-A, WU-B and WU-C of `docs/plans/2026-07-27-enrollment-and-roster.md`. That plan is a
**mid-plan checkpoint** — WU-D through WU-G are outstanding, so nothing yet *displays* the Roster.

- [x] **AC-260730.201447a.1** — The solution builds and the CLI reports a version, with CI running
      build, full suite, and the startup-budget guard on every push.
- [x] **AC-260730.201447a.2** — A Hub publishes an advisory `host.json` whose DACL grants only the
      current user and SYSTEM, with inheritance severed; the file carries a 256-bit bearer secret.
- [x] **AC-260730.201447a.3** — A rendezvous that fails its probe is deleted, not ignored; the probe
      confirms host identity and protocol version, not merely reachability.
- [x] **AC-260730.201447a.4** — Concurrent start-or-attach converges on one Hub.
- [x] **AC-260730.201447a.5** — `supervisor hub status|stop|serve` work, with stop refusing while
      clients are attached unless forced.
- [x] **AC-260730.201447a.6** — The `mcp` and `hook` verbs bypass the command tree, measured at
      129 ms against 260 ms for the full path in Release; a mutation test confirms the guard fires.
- [x] **AC-260730.201447a.7** — A real `claude` session spawns the shim, which starts a Hub that did
      not exist, enrols, serves MCP over stdio, and deregisters on exit — 5 of 5 consecutive sessions.
- [x] **AC-260730.201447a.8** — Every enrollment failure path exits 0 in silence and is recorded, so
      `doctor` can distinguish "silent" from "broken".
- [x] **AC-260730.201447a.9** — Repository identity is the normalized git remote, resolving SSH host
      aliases via `~/.ssh/config`, so one repository is one identity across Machines.
- [x] **AC-260730.201447a.10** — 66 tests, 0 failures, green in Debug and Release.

**Delivered differently from the plan:** `HubVersionTests.ProtocolVersionIsIndependentOfAssemblyVersion`
does not exist — "these two values are independent" has no meaningful runtime assertion. It ships as
`RendezvousTests.CarriesProtocolVersionSeparatelyFromBuildVersion`, asserting both fields appear
distinctly in the serialized rendezvous, which is the property a reader actually depends on. Two
other tests moved class as the design settled (`StaleFileRemovedWhenProbeFails` →
`HubLocatorTests`, `ConcurrentStartsConvergeOnOneHub` → `HubStarterTests`).

**Added beyond the plan:** `ResolveAsync` and its background-enrollment path. The plan did not
anticipate that Claude Code spawns MCP servers ~3.5 s before writing the session state file, which
made enrollment succeed 1 time in 5 — intermittently and silently.

---

## Area: Roster & Fleet View

_No stories yet. First expected from WU-1/WU-2._

Grounding: FR-2, FR-3, NFR-8 · ADR-0005 · `arch:roster`, `arch:webui`

---

## Area: Agent Control & Commands

_No stories yet. First expected from WU-4._

Grounding: FR-5, FR-7, FR-8 · ADR-0003, ADR-0004 · `arch:supervision`

---

## Area: In-App Terminal

_No stories yet. First expected from WU-3._

Grounding: FR-6, NFR-7 · ADR-0003 · `arch:terminal`

---

## Area: Multi-Machine Federation

_No stories yet. First expected from WU-5._

Grounding: FR-9, NFR-3, NFR-4 · ADR-0001 · `arch:federation`, `arch:security`

---

## Area: Workstreams & Continuity

_No stories yet. First expected from WU-4b._

Grounding: FR-10, NFR-5 · ADR-0006 · `arch:workstream`

---

## Area: CLI UX & Output

_No stories yet._

Grounding: FR-7 · `arch:cli-core`

---

## Area: Agent Integration & Skills

_No stories yet._

Grounding: FR-1, FR-7 · `arch:skills`

---

## Area: Distribution & Packaging

_No stories yet._

Grounding: FR-12 · `arch:packaging`

---

## Area: Infrastructure & Testing

_No stories yet._

Grounding: NFR-2, NFR-6 · `arch:observability`
