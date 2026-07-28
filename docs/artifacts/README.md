Markdown files in this folder are plans that have been completed. The preceding date in each filename is the date of implementation.

Bug fixes carry a `bug` segment — `<date>-bug-<slug>.md`; features are `<date>-<slug>.md` (the default; see `docs/plans/README.md` for the rule). Completed plans may end in a `<!-- FRICTION:START -->` block — friction the pre-impl/impl/post-impl skills hit while running.

**Friction is marked, never deleted.** Every entry (`### F-N — <skill> · <phase>`, then `**What happened:**` / `**Recommendation:**`) ends with exactly one `**Status:**` line:

- `**Status:** Open` — unresolved (the default when logged).
- `**Status:** Resolved <YYYY-MM-DD> — <what changed & where>`
- `**Status:** Declined <YYYY-MM-DD> — <why not>`

Resolving a point flips **only** that line — entries are a permanent, auditable record. The open-work list is one grep: `grep -rn 'Status:\*\* Open' docs/artifacts`. `F-N` numbering restarts per document, so a point's address is `<file> + F-N`.

## Tag vocabulary

Every index entry (and every plan in `docs/plans/`, via its `**Tags:**` header) carries **tags**
that point back at the living SDD documents — so an agent grounding itself in a fresh context
window can filter this index by the part of the system it is about to change and read *how that
part came about*. Three namespaces:

- `prd:FR-N` / `prd:NFR-N` — a requirement in `docs/sdd/thesupervisor-prd.md`.
- `arch:<slug>` — an architecture component from the **curated list below**. Never invent a slug
  inline; add it to the table first (a deliberate one-line edit).
- `area:<slug>` — a stories `## Area:` section in `docs/sdd/thesupervisor-stories.md`, via the mapping below.

**Rules**: every entry has **≥1 `area:` tag**; `prd:`/`arch:` tags only where they genuinely apply —
never padded; at most **6** tags per entry. Workflow: **pre-impl** stamps `**Tags:**` into the plan
from its grounding, **post-impl** reconciles them against what actually shipped and copies them into
the index row. A test enforces this once the test project exists.

<!-- TAG-VOCAB:START -->
**`arch:` slugs** (curated):

| Slug | Component |
|------|-----------|
| `cli-core` | Command tree, Spectre.Console plumbing, execution modes, output formatters |
| `enrollment` | Stdio MCP server, lifecycle hooks, install/uninstall/doctor, protocol versioning |
| `federation` | Pairing, the Peer link, roster merge, cross-machine command forwarding |
| `hub` | Hub host process — Kestrel, rendezvous, start-or-attach, detached lifecycle |
| `model-backend` | Configurable inference provider (Anthropic default, local endpoint alternative) |
| `notifications` | OS toasts and attention signalling |
| `observability` | Telemetry context, spans, diagnostic tracing |
| `packaging` | Global-tool nupkg, install scripts, TFM/RID matrix, self-update |
| `roster` | Roster model, Status, Activity Summary, transcript tailing, unenrolled backstop |
| `security` | Capability tokens, secrets at rest, ACLs, origin/host guards, certificate pinning |
| `skills` | Agent skills — tracked `skills/` source, cross-harness deploy, workflow-skill trio |
| `supervision` | Shared control service — Commands, lifecycle, Owned/Foreign gating, launch |
| `terminal` | ConPTY interop, Job Object, terminal session manager, terminal transport |
| `webui` | Browser UI — React shell, Roster pane, terminal pane, WebSocket host |
| `workstream` | Workstream identity, branch migration, bounded session index |

**`area:` slugs** (1:1 with the stories doc's `## Area:` sections):

| Slug | Stories area heading |
|------|----------------------|
| `agent-control` | Agent Control & Commands |
| `agent-enrollment` | Agent Enrollment & Lifecycle |
| `agent-skills` | Agent Integration & Skills |
| `cli-ux` | CLI UX & Output |
| `distribution-packaging` | Distribution & Packaging |
| `federation` | Multi-Machine Federation |
| `infra-testing` | Infrastructure & Testing |
| `roster-ui` | Roster & Fleet View |
| `terminal` | In-App Terminal |
| `workstreams` | Workstreams & Continuity |
<!-- TAG-VOCAB:END -->

## Index

Sorted newest first.

| Date | Plan | Tags | Summary |
|------|------|------|---------|
| | _No plans archived yet._ | | |
