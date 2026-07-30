# Enrollment and the Roster

**Branch:** feature/enrollment-and-roster
**Type:** Feature
**Tags:** `area:agent-enrollment` `area:roster-ui` `area:infra-testing` `arch:enrollment` `arch:hub` `arch:roster`
**Date documented:** 2026-07-27

The first implementable plan. Delivers WU-0 (scaffolding + Hub host) and WU-1 (enrollment + Roster)
from `docs/plans/design.md` § 7, shipping `supervisor list` as the visible surface. No browser UI.

---

## Context

TheSupervisor exists because a developer runs several agents at once and nothing tells them about
those agents collectively — right now, on this machine, four sessions are live and one has been
blocked on input for twenty minutes with nothing surfacing it.

Everything in the product rests on one mechanism: **involuntary enrollment** (ADR-0002). If a
user-scope stdio MCP server plus lifecycle hooks does not reliably produce a complete, live Roster,
nothing built above it matters. That mechanism is the only genuinely unproven part of the design —
the terminal, the host, and the rendezvous are all being copy-adapted from working WExpert code
(`C:\Code\MS\CLI`), and the browser UI is downstream of a Roster that doesn't exist yet.

So this plan attacks enrollment first and proves it with a CLI verb rather than a pane.

**Grounding.** SDD (`thesupervisor-{prd,architecture,stories}.md`), `CONTEXT.md`, and ADR-0001…0006
were authored in the founding session (commit `1c29466`) and are relied on directly rather than
re-read. Live grounding from that same session: `claude agents --json` against four running sessions,
`~/.claude/sessions/<pid>.json`, transcript JSONL shape, named-pipe enumeration, and startup timings.

**Related work:** none — `docs/artifacts/` is empty. This is the first plan.

### Requirements delivered

FR-1 (involuntary enrollment), FR-2 (the Roster, CLI surface only), FR-3 (Activity Summary), FR-4
(unenrolled visibility), FR-12 (install/repair/diagnose), and partially FR-7 (one control surface —
the shared service plus its first CLI verb and first MCP tool).
NFR-1 (fail open), NFR-2 (startup budget), NFR-4 (contract versioning), NFR-6 (testability seams).

### Explicitly NOT delivered

Browser UI (WU-2), terminal (WU-3), any mutating Command (WU-4), Workstreams (WU-4b), federation
(WU-5), notifications and Model Backend (WU-6). Every Agent in this plan is **Foreign** — nothing
launches Agents yet, so nothing is Owned.

---

## Locked decisions

Decisions carried from `design.md` are cited by id. Two are **revised** here; both are called out.

| # | Decision | Source |
|---|---|---|
| L1 | **Single-target `net10.0`.** No multi-targeting — one test run, not two, permanently. Any Machine paired later needs .NET 10. | Q2 |
| L2 | **GitHub Actions CI from the first commit**: build, full suite, startup-budget guard. The budget test is a guard, and a guard that only runs when remembered is a suggestion. | Q2, NFR-2 |
| L3 | **Hooks are `SessionStart` + `SessionEnd` only.** ⚠ **Revises D3**, which specified `UserPromptSubmit`. The transcript tail already sees prompts, so the third hook is redundant and would cost ~130 ms before every prompt, forever. FR-3 is unchanged — the baseline is still the submitted prompt, sourced from the tail. Trade-off: a brand-new session shows a thinner summary until its first turn is tailed. | Q3 |
| L4 | **The shim exposes exactly one read-only MCP tool** — list the Fleet. ⚠ **Narrows D14's scope for v1**: the shared control service exists and both surfaces delegate to it, but it has one read method and no mutating method. Proves the tool path before WU-4 widens it. | Q4 |
| L5 | **Shim↔Hub transport is loopback HTTP**, the same Kestrel the browser will need — not a second named-pipe transport. Connection cost is paid once per session, not per call. Copy-adapted from WExpert's `host.json` + `HttpUiHostClient`. | — |
| L6 | **Agent display names are Claude Code's derived names** (`thesupervisor-3b`), read from the session, never minted by us. A Roster that disagrees with `claude agents` about what a session is called is worse than no name. | — |
| L7 | **Transcript tailing is `FileSystemWatcher` as a nudge plus a tracked-offset poll**, never FSW alone — FSW coalesces and drops events under load, and a missed append silently freezes an Activity Summary. The poll is the correctness mechanism; the watcher only reduces latency. | ADR-0005 |
| L8 | **`supervisor list` auto-starts the Hub** (start-or-attach), consistent with D19. A deliberate user invocation should just work. | D19 |
| L9 | Every Agent in this plan is **Foreign**. The tier field exists and is populated, but nothing can produce an Owned Agent until WU-3. | ADR-0003 |
| L10 | Runtime state is **in-memory in the Hub only**. Nothing persists in this plan except settings written by `install`. | D17 |

Carried unchanged and load-bearing here: D2/D4 (MCP transport is the enrollment path, skill is not),
D6 (`claude agents --json` backstop), D10 (austere fast path + budget), D16 (`UseShellExecute=true` +
`WindowStyle=Hidden` — a console-less Hub cannot bind a pseudoconsole child later), D19 (fail open),
D23 (attention ordering), D28 (protocol versioned separately from build).

---

## Build strategy

**Fan-out into independent work units.** No `WebUI/` surface, so no MSUs and no smoke-test stops.
Dependency order:

```
WU-A (scaffold + CI)
   └─> WU-B (Hub host + rendezvous)
          ├─> WU-C (MCP shim + fast path)   ─┐
          └─> WU-E (Roster + tail + backstop)├─> WU-F (list + tool)  ─> WU-G (demo + e2e)
                                             │
                 WU-D (hooks + install/doctor)┘   (needs WU-C's verb surface)
```

WU-C and WU-E are independent of each other and may be built concurrently (in Claude Code, one
sub-agent per unit in parallel worktrees). WU-D depends on WU-C. WU-F depends on both. WU-G is last.

---

## Work units

### WU-A — Repository scaffold and CI

`TheSupervisor.slnx` targeting `net10.0`, with `Supervisor` (CLI, `PackAsTool`, binary `supervisor`),
`Supervisor.Core` (domain — Agent, Roster, Status, ActivitySummary), `Supervisor.Web` (Hub host), and
`Supervisor.Tests` (xUnit). `Directory.Build.props` and `Directory.Packages.props` for central package
management. Spectre.Console.Cli command tree with `--json` on every leaf. GitHub Actions workflow:
restore, build, test, startup-budget guard.

**Deliberately deferred:** `install.ps1`, packaging/release, self-update. The tool is run via
`dotnet run` / a local `dotnet tool install` from a locally packed nupkg until WU-A of a later plan.

### WU-B — Hub host, rendezvous, start-or-attach

Copy-adapt `WExpert.Web/WebHost.cs`, `UiHostRendezvous.cs`, `WExpert/Commands/Ui/UiHostLauncher.cs`,
`HttpUiHostClient.cs`, `IUiHostClient.cs`, `UiHostVersion.cs`.

- `%LOCALAPPDATA%\TheSupervisor\hub\host.json` — user-ACL-protected; non-secret host id, separate
  256-bit host secret, pid, loopback endpoint, protocol version, start time. Advisory, always
  probe-verified, stale files removed, concurrent starts serialized.
- Detached spawn with `UseShellExecute=true` + `WindowStyle=Hidden` (D16) — **not** `CreateNoWindow`.
  Nothing in this plan binds a pseudoconsole, but getting this wrong now means WU-3 fails mysteriously
  later, and the fix is one `ProcessStartInfo` property.
- `--foreground` for debug and e2e (bypasses detachment and the rendezvous, so tests stay isolated).
- `supervisor hub status` / `supervisor hub stop`, with confirmation when clients are attached.
- Protocol version carried in `host.json`, **separate from the assembly version** (D28).

### WU-C — MCP shim and the austere fast path

`supervisor mcp` — a stdio MCP server that Claude Code spawns per session, which is simultaneously a
loopback client of the Hub. Enrollment is its connection.

- **Austere startup path** (D10): no update check, no telemetry init, no config scan, no banner. A
  separate composition root from the interactive verbs, so the two cannot share a dependency by
  accident.
- Start-or-attach the Hub on connect (L8, D19). **Fail open on everything** — spawn failure, connect
  failure, timeout, version mismatch — exit 0, silently, always.
- Register the Agent: sessionId, pid, cwd → Repository, Machine, derived name (L6), kind.
- Protocol-version handshake; a genuine mismatch refuses **silently** and is visible only via
  `doctor` and the unenrolled row.

### WU-D — Hooks, install, uninstall, doctor

- `supervisor hook session-start` / `session-end` — same austere path as WU-C, always exit 0.
- `supervisor install` — timestamped backup of `settings.json`, a **marked block** for our hook
  entries, user-scope MCP registration, verification. Idempotent; re-running repairs drift.
- `supervisor uninstall` — removes exactly what was added, verified by diff against the backup.
- `supervisor doctor` — reports registered / hooks present / Hub reachable / protocol match /
  enrollment working, with the specific remedy for each failure.

> The user's `~/.claude/settings.json` is hand-maintained and currently has **no hooks**. Getting
> uninstall wrong leaves orphaned hooks firing forever against a deleted binary. This is the unit
> where a bug is most expensive to the developer personally.

### WU-E — Roster, transcript tail, and the unenrolled backstop

- `Roster` in `Supervisor.Core`: enrolled Agents keyed by Machine-qualified identity, with Status,
  Activity Summary + age, tier (always Foreign, L9), Subagent count.
- **Transcript tail** (ADR-0005, L7): watch `~/.claude/projects/<slug>/<sessionId>.jsonl`, derive the
  live line from the latest `tool_use` name and assistant text, count outstanding `Task` calls for the
  Subagent badge. Parse failure degrades to the prompt-derived baseline; **no Agent ever disappears
  because parsing broke.**
- **Backstop** (D6): poll `claude agents --json`, diff against the Roster, emit **Unenrolled Agent**
  rows for unmatched local sessions.
- **Attention ordering** (D23): waiting → errored/stopped → busy → idle → unenrolled; recency breaks
  ties. Ordering lives in `Supervisor.Core` and is unit-tested there, so the UI later inherits it
  rather than reimplementing it.

### WU-F — The control service, `supervisor list`, and the MCP tool

- `IFleetQueryService` in `Supervisor.Core` with one read method, and **both** the CLI verb and the
  MCP tool delegating to it (D14, narrowed by L4). The anti-drift shape is established now, while it
  is one method wide.
- `supervisor list` — Spectre table by default, `--json` for scripting, `--cwd` filter mirroring
  `claude agents`.
- One read-only MCP tool exposing the same result, so any Agent can see the Fleet from its own context.
- A parity test pinning the tool and the verb to the same service method.

### WU-G — Seeded-fleet demo and hermetic e2e

`docs/promotional/demos/01-seeded-fleet/` — a README plus a seed script standing up fake Agents in
known states (busy, idle, **blocked on input**, unenrolled, and one on a fake Peer to prove the
Machine-qualified identity holds before federation exists). Wired into the hermetic harness so
**"the demo runs correctly" is the regression assertion**. Stands up the e2e harness WU-2 will need.

---

## Test plan

Test-first throughout: each unit starts from a named failing test. Levels per
`thesupervisor-architecture.md` § Testing Strategy.

### Seams built in this plan

| Seam | Unit | Replaces |
|---|---|---|
| `FakeAgent` | WU-C | An enrolled Claude session — no auth, no tokens |
| `FakeClock` | WU-E | Wall time — staleness and age never sleep |
| Transcript fixtures | WU-E | Live JSONL — captured from real sessions, checked in |
| `FakeAgentsCliProbe` | WU-E | `claude agents --json` — so the backstop is testable offline |
| `FakeHubClient` | WU-C | The Hub, for shim-side fail-open tests |

`FakePeer` and `FakeTerminalProcess` are **not** built here — nothing federates or spawns a PTY yet.

### The failing test that starts the work

`ScaffoldTests.SolutionBuildsAndCliReportsVersion` — WU-A's tracer bullet. Everything else follows it.

### Per unit

**WU-A**
- `ScaffoldTests.SolutionBuildsAndCliReportsVersion` — unit
- `CommandTreeTests.EveryLeafRedeclaresJson` — unit; guards the Spectre branch-leaf `--json` trap,
  whose symptom is empty JSON rather than an error
- CI green on push (build + test + budget)

**WU-B**
- `RendezvousTests.WritesHostJsonWithUserOnlyAcl` — component
- `RendezvousTests.StaleFileRemovedWhenProbeFails` — component
- `RendezvousTests.ConcurrentStartsConvergeOnOneHub` — component; two racing launchers, one Hub
- `HubLifecycleTests.StopRefusesWithAttachedClientsUnlessForced` — component
- `HubVersionTests.ProtocolVersionIsIndependentOfAssemblyVersion` — unit (D28)
- `HubSpawnTests.UsesShellExecuteWithHiddenWindow` — unit; asserts the `ProcessStartInfo` shape, so
  the D16 pseudoconsole trap cannot regress silently before WU-3 needs it

**WU-C**
- `ShimEnrollmentTests.ConnectRegistersAgentWithRepositoryAndMachine` — component, `FakeHubClient`
- `ShimFailOpenTests.ExitsZeroWhenHubUnreachable` — component
- `ShimFailOpenTests.ExitsZeroOnProtocolMismatch` — component
- `ShimFailOpenTests.FailureIsRecordedEvenThoughExitIsZero` — component. **The important one**:
  fail-open makes silence ambiguous, so assert the exit code *and* that the failure is observable.
- `StartupBudgetTests.McpVerbStaysUnderBudget` — unit, **CI-enforced**. Measures the austere path.
  If this goes red the fix is to move the dependency off the fast path, never to raise the number.
- `StartupBudgetTests.HookVerbStaysUnderBudget` — unit

**WU-D**
- `InstallTests.WritesOnlyOurMarkedBlock` — component, over a fixture `settings.json` carrying
  pre-existing `statusLine`, `permissions`, and `enabledPlugins` (mirroring the real one)
- `InstallTests.IsIdempotentAcrossRepeatedRuns` — component
- `UninstallTests.RestoresFileByteIdenticalToPreInstallBackup` — component. Directly guards the
  orphaned-hook failure mode.
- `DoctorTests.ReportsEachFailureModeWithItsRemedy` — component; table-driven over the failure matrix
- `HookTests.AlwaysExitsZero` — component, including when the Hub is unreachable

**WU-E**
- `TranscriptTailTests.DerivesActivityFromLatestToolUse` — component, fixture-driven
- `TranscriptTailTests.CountsOutstandingSubagentTasks` — component
- `TranscriptTailTests.DegradesToPromptBaselineOnParseFailure` — component, malformed fixture.
  **Asserts the Agent is still present**, not merely that no exception escaped.
- `TranscriptTailTests.PollCatchesAppendWhenWatcherEventIsDropped` — component; the L7 correctness
  claim, with the watcher suppressed
- `ActivitySummaryTests.MarksStaleAfterThreshold` — unit, `FakeClock`
- `RosterOrderingTests.AttentionOrderingAcrossAllStates` — unit; a blocked Agent sorts above a busy
  one and above every idle one
- `RosterOrderingTests.RecencyBreaksTiesWithinBand` — unit
- `BackstopTests.LocalSessionNotEnrolledAppearsAsUnenrolled` — component, `FakeAgentsCliProbe`
- `BackstopTests.EnrolledAgentIsNotDuplicatedByTheProbe` — component; identity match across the two
  sources, which is where a double-listing bug would live

**WU-F**
- `ListCommandTests.TableAndJsonCarryTheSameRows` — component
- `ListCommandTests.CwdFilterMatchesClaudeAgentsSemantics` — component
- `FleetToolTests.ToolAndVerbReturnIdenticalResults` — contract. The anti-drift pin (D14/L4).
- `ListCommandTests.EmptyFleetIsNotAnError` — component

**WU-G**
- `e2e/seeded-fleet.spec.ts` — the demo driven hermetically: seed the fleet, run `list`, assert
  ordering, the unenrolled row, and the Machine-qualified identity of the remote-Machine Agent.

### Not automated — named developer smokes

These require a real Claude session and are deliberately excluded from CI (auth + model capacity).
Each is a step to perform, not an assertion to inspect:

1. **Real enrollment.** Run `supervisor install`, start a new `claude` session in a scratch directory,
   run `supervisor list` — the session appears with the right Repository and a truthful Activity
   Summary. *This is the one thing no fake can prove.*
2. **The four-session fleet.** With several real sessions running and one deliberately left blocked
   at a prompt, `supervisor list` puts the blocked one first.
3. **Unenrolled.** Start a session with `--strict-mcp-config` so the shim never loads; it appears as
   an Unenrolled Agent rather than vanishing.
4. **Fail-open under a deleted binary.** Delete the Hub binary, start a session — it starts normally,
   shows no error, loses no functionality, and `doctor` explains exactly what is wrong.
5. **Uninstall.** `supervisor uninstall`, then diff `~/.claude/settings.json` against the pre-install
   backup — clean, with no orphaned hooks.

### Time-dependent behavior

Activity Summary staleness and the Hub's idle timeout are the only clock-driven behavior in this
plan. Both go through `FakeClock`. **No test sleeps** — a test that waits is a test that gets skipped.

---

## Verification

1. `dotnet build TheSupervisor.slnx` green; `dotnet test TheSupervisor.slnx` green, 0 failures.
2. CI green on push — build, full suite, and the startup-budget guard.
3. The budget guard passes with headroom, and its measured value is reported in the summary.
4. `e2e/seeded-fleet.spec.ts` green from a clean checkout.
5. All five named developer smokes performed, with results stated. Any not performed is reported as
   **unverified** — not "verified by inspection."
6. `supervisor uninstall` leaves `settings.json` diffing clean against the pre-install backup.
7. No secret appears in argv, logs, or any command output (`host.json`'s secret especially).
8. Domain terms match `CONTEXT.md` — no `session` for **Agent**, no `supervisor` for **Hub**.

---

## Open risks

- **`~/.claude` internals are undocumented.** Session-file shape and transcript JSONL shape can change
  in any Claude Code release. Mitigated by degrading to the prompt baseline rather than failing, and
  by the `claude agents --json` backstop (a supported contract) being the presence check. Fixtures are
  checked in, so a format change shows up as a failing test rather than a silent blank column.
- **The startup budget may be tight** once the MCP SDK is on the fast path. Measured baseline is
  ~128 ms for a minimal console; the SDK's cost is unknown until WU-C. If it doesn't fit, that is a
  plan-level decision (a separate AOT shim, D10's rejected option B), not an implementation shortcut.
- **`supervisor install` mutates a hand-maintained file.** Backup before write, marked block, and an
  uninstall test asserting byte-identical restore.

<!-- FRICTION:START -->
## Skill Friction Log

> Friction is **marked, never deleted**: every entry ends in a `**Status:**` line — `Open` (the default),
> `Resolved <date> — <what/where>`, or `Declined <date> — <reason>`. Open entries are the backlog for
> improving these skills. **Empty is a valid state — do not pad it.**

### F-1 — pre-impl · invocation (wrong skill loaded)

**What happened:** `/pre-impl` loaded the **global WExpert** skill from `~/.claude/skills/pre-impl`,
not this repo's `skills/pre-impl/SKILL.md`. They differ materially: the global one has no test-plan
phase (the entire point of this repo's adaptation), grounds against `docs/sdd/wexpert-*.md`, and runs
`bash setup.sh`. Nothing in the loaded skill said "check whether this repo has its own copy," so the
mismatch was only caught by recognizing the text. Following it would have produced a plan with no test
plan, which `impl` would then have refused.

**Recommendation:** Two changes. (1) Have `supervisor install` deploy the project-level
`.claude/skills/` copies, so a repo with its own trio always shadows the global one — done manually
this session, and it worked. (2) Add a line to each of the trio: "If `skills/<name>/SKILL.md` exists
in the current repo and differs from the copy you are executing, the tracked one wins — say so and
follow it." post-impl already has this as its Phase 0 self-editing check; pre-impl and impl do not.

**Status:** Open

### F-2 — pre-impl · Phase 2 (deploy) on a repo with no code

**What happened:** Phase 2 assumes a deployable tool exists. On the very first plan there is no
solution, no project, and no `install.ps1`, so the phase has nothing to do. The skill's guidance
covers "docs-only work → defer and say so," but not "the code surface does not exist yet," which is a
different reason for the same outcome and will recur on any greenfield repo's first plan.

**Recommendation:** Add a bootstrap case to Phase 2: when no solution/tool exists yet, state
"deploy skipped — no tool exists; this plan creates it" and continue. Cheap, and it stops the phase
reading as forgotten.

**Status:** Open

### F-3 — pre-impl · Phase 3a (grounding a doc set you just wrote)

**What happened:** The continuity exception ("skip a grounding read you already hold, but state what
you are relying on") worked exactly as intended and was the right call — the SDD, ADRs, and
`CONTEXT.md` were authored minutes earlier in this same session. Logged not as a problem but because
the *first* plan in any repo will always hit this path, and it is worth knowing the exception is
load-bearing rather than an edge case.

**Recommendation:** None — the skill handled it correctly.

**Status:** Declined 2026-07-27 — working as designed; recorded for context only.

### F-4 — post-impl · Phase 3 (plan handling)

**What happened:** Phase 3 said "always move" the plan to `docs/artifacts/`. This run was a mid-plan
checkpoint — WU-A/B/C shipped, WU-D through WU-G outstanding on the same branch — and archiving
would have been actively harmful: the impl skill resolves work by matching `**Branch:**` against the
checked-out branch and only looks in `docs/plans/`, so the next `/impl` on this branch would have
reported "no plans found" on a branch halfway through one. The "always" was inherited from WExpert,
where each *phase* is its own branch and the archived plan is referenced by later branches; a plan
whose remaining units live on the same branch is a different case the wording did not admit.

**Recommendation:** Applied — Phase 3 is now "move only when the plan is finished", with an explicit
mid-plan checkpoint path that runs every other phase and leaves the plan in place, plus a Phase 10
row naming the outstanding units.

**Status:** Resolved 2026-07-30 — `skills/post-impl/SKILL.md` Phase 3 rewritten (v1.1.0), summary
line updated, redeployed to `.claude/skills/`.

### F-5 — impl · Phase 2c (`--no-build` masking a failed build)

**What happened:** `dotnet test --no-build` reported `Passed! 51` and `Passed! 33` over builds that
had **failed**, twice in one session — once on a missing project reference, once on a missing using.
The skill warns about this, and the warning was not enough, because the failure mode is that the
green line is the last thing printed and reads as success. Both times the real error was several
lines above, already scrolled past.

**Recommendation:** Have Phase 2c prescribe the *shape* of the command, not just the hazard: run
build and test as separate steps and gate the second on the first (`dotnet build … && dotnet test
--no-build …`), so a failed build cannot be followed by a green test summary at all.

**Status:** Open

### F-6 — impl · Phase 6 (commit not gated on green)

**What happened:** Commit `443fe1c` landed while `HookVerbStaysUnderBudget` was failing. The Phase 6
instructions describe staging and committing but never say *gate the commit on a green suite*, and
chaining build/test/commit into one shell invocation makes ignoring the result the default. It was
noticed only when reading back the output afterwards. The failure turned out to be a flake, which is
luck rather than process — nothing about the workflow would have caught a real one.

**Recommendation:** Make Phase 6 state the precondition explicitly ("do not commit unless the suite
just passed in this run") and show the gated form, the same way Phase 2c should.

**Status:** Open

### F-7 — pre-impl · Phase 7 (test plan named no test that runs the binary)

**What happened:** Two bugs made **every invocation of the CLI fail** while all 31 unit tests stayed
green: the type registrar instantiated eagerly (Spectre's own `ExplainCommand` has no parameterless
constructor, so configuration threw before any verb ran), and the resolver returned null for
`IEnumerable<T>` (Spectre asks for `IEnumerable<IHelpProvider>` mid-dispatch and treats null as
fatal). Neither is reachable through `CommandAppTester`. They were found by running the binary by
hand, not by anything the test plan named. The plan's test-level table has Unit / Component /
Contract / Hermetic e2e / Developer smoke, and *nothing* between the last two that simply executes
the shipped artifact once.

**Recommendation:** Add a standing rule to Phase 7: any plan that produces or changes an executable
names at least one test that **spawns the built binary and asserts on its exit code and output**.
Cheap, and it is the only level that catches composition-root failures.

**Status:** Open
<!-- FRICTION:END -->
