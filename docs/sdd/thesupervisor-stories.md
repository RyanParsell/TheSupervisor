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
| [Agent Enrollment & Lifecycle](#area-agent-enrollment--lifecycle) | `agent-enrollment` | _none yet_ | How an Agent joins the Roster and leaves it: the MCP shim, hooks, protocol versioning, install/uninstall/doctor, fail-open behavior. |
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

_No stories yet. First expected from WU-1 (see `docs/plans/design.md` § 7)._

Grounding: FR-1, FR-4, FR-12, NFR-1, NFR-2, NFR-4 · ADR-0002 · `arch:enrollment`

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
