---
name: pre-impl
description: Plan new TheSupervisor features and bug fixes before implementation. Syncs the repo, grounds in current specs, domain language, and decisions, grills requirements, writes a phased plan with a mandatory test plan in docs/plans, and creates or reuses the correct feature/<slug> or bug/<slug> branch. UI work is divided into smoke-testable units. Use when starting, scoping, or planning TheSupervisor work, or before the impl skill. Supports --main, --with-docs, and --hotbug; hotbug is the explicit urgent-fix path that remains on main with no PR. Ends with a reviewable plan and branch, not implementation.
user_invocable: true
version: 1.0.0
# ⚠️ IMPORTANT: When editing this file, increment the patch version above (e.g., 1.0.4 → 1.0.5).
# Derived from the WExpert skill trio (C:\Code\MS\CLI\skills), retargeted to TheSupervisor and
# hardened around test-first development.
---

## Pre-Implementation: $ARGUMENTS

The **front bookend** of the trio (**pre-impl → impl → post-impl**): get the repo current and the tool deployed, then interview the feature into a **phased plan** — with a **test plan naming the tests before the code exists** — on a correctly-named **feature branch**, ready to hand to the impl skill. It ends at *a plan on a branch*; it does **not** implement.

> **Harness-neutral.** Every phase below is written as concrete `git` / `pwsh` / `dotnet` commands any agent can run directly, plus references to sibling skills **by name**. Invocation differs by harness: Claude Code runs a skill as `/name`, Codex as `$name` or via `/skills`, and Copilot activates it by matching its description — so wherever this file says "run the X skill," use whatever your harness does to invoke a skill named X. If a referenced skill isn't installed, do its work inline. A **Claude Code accelerations** section at the end lists optional speedups (sub-agents, worktrees, background tasks); the numbered phases are the complete, first-class path on their own.

> **Effort is not measured in human-hours.** When shaping the plan, do **not** give implementation *time* any strong weight — the hours a human would spend building a feature generally don't apply to an agent. Decide the approach on merit: the most correct, complete, and maintainable result (right decomposition, proper tests, thorough coverage), not whatever looks fastest or smallest to hand-build. Never trim scope, skip tests, or pick a shallower design to "save time." If two approaches differ mainly in human effort, that difference is not a deciding factor.

> **Test-first is not negotiable here.** TheSupervisor is multi-process and multi-machine by construction — Agents, the Hub, Peers, pseudoterminals, a browser. Its real defects are integration defects, and the seams that make those testable (fake Agent, fake Peer, fake PTY, transcript fixtures) only exist if they are designed *before* the code. A plan without a test plan is not a plan; Phase 7 is a gate, not a formality.

### Arguments
- `--main` — switch to `main` and sync trunk before starting (default: sync the current branch — so a later phase of an in-flight feature continues **on that feature's existing branch**, not on a new one).
- `--with-docs` — understand via the **grill-with-docs** skill (challenge against the domain model + update `CONTEXT.md`/ADRs inline) instead of the default **grill-me** skill.
- **`--hotbug`** — the **trunk-direct escape hatch**: work on `main` with **no branch and no PR**. Match it liberally — `--hotbug`, `hotbug`, `hot bug`, `hot-bug`, `--hot-bug` in the invocation all mean the same thing. **This is the ONLY way any skill in the trio works on `main`.** See below.

### The `--hotbug` escape hatch

Normally pre-impl **always** puts work on a branch (`feature/<slug>` or `bug/<slug>`) and the impl skill **refuses to implement on `main`**. `--hotbug` is the single, deliberate exception: an urgent fix that goes straight to trunk without the branch/PR ceremony.

Because it bypasses the review gate, it carries obligations:

- **It is always a bug fix.** A hotbug is by definition restoring broken behavior *now*. If the change ships any new capability, it is not a hotbug — drop the flag and take a branch. **Say this to the developer** if what they describe sounds like a feature.
- **It stays on `main`** — no branch is created, at any point, by any skill in the trio.
- **It is declared in the plan** (`**Hotbug:** YES` — Phase 9), and that declaration is what unlocks the impl skill's `main` refusal. No declaration → impl stops.
- **It still gets a test plan.** Urgency is the reason to be *more* certain the fix is right, not less. A hotbug's test plan is usually one failing test that reproduces the defect — write it first, exactly as normal.
- **Confirm before proceeding.** State plainly: *"Hotbug: this goes straight to `main` with no branch and no PR — the review gate is skipped. Confirm?"* If the developer hesitates or the fix isn't genuinely urgent, use a normal `bug/<slug>` branch instead.

### Output Style
- Brief phase indicators ("Phase 1: Sync…", "Phase 3: Grounding (SDD + repro)…", "Phase 4: Grilling…").
- Do NOT narrate each file read/edit.
- End at a clear "plan written + branch created — review, then run the impl skill." (Under `--hotbug`: "plan written on `main` — HOTBUG, no branch.")

---

### Phase 1 — Sync to current

- If `--main` **or `--hotbug`**: `git switch main` first (a hotbug lands on trunk, so it must *start* from trunk — even if you were on a feature branch). Refuse (and stop) if switching would abandon real uncommitted work.
- **Dirty-tree rule (before pulling):** inspect `git status --porcelain`.
  - Only uncommitted changes are **`packages.lock.json`** files (incidental restore churn) → discard them: `git checkout -- **/packages.lock.json`, then pull.
  - **Real work the developer wants put on a branch** (typically: work started ad-hoc on `main` — common, because the impl skill *refuses* to run on `main`, so people get stopped mid-flight and need exactly this) → **RESCUE it, don't stop.** Cut the branch **now**: `git switch -c feature/<slug>` (or `bug/<slug>`) — uncommitted changes follow the switch, so nothing is lost and `main` is left clean. Then **record the branch name and reuse it in Phase 9 — do NOT create a second branch there.** If the grill hasn't named the work yet, use a provisional slug and rename before the plan commit (`git branch -m <new>`); a branch that has never been pushed renames for free.
  - **Any other** uncommitted change → **STOP and surface it.** Never stash/discard real work; let the developer decide. Stopping stays the default whenever the developer has *not* asked for the work to be branched.
- `git pull` (fast-forward the current branch, or `main` under `--main`). If the pull reports conflicts, stop and surface them.

---

### Phase 2 — Deploy the tool (when the work touches code)

The deploy is a pack + global-tool reinstall — **minutes of wall-clock**. It exists so that Phase 3b (reproduce/exercise the live system) and the impl skill run against current bits. That value is real for code work and **zero for a docs/config plan**, so decide before paying for it:

- **Work has a code surface** (or you don't yet know) → run it.
- **Docs / SDD / skills / config-only work** → **defer or skip it, and say so explicitly** ("Phase 2: deploy deferred — no code surface"). A silent skip is indistinguishable from a forgotten step; an explicit one is a decision.
- **Change type still unknown at this point** (the grill hasn't run) → **kick it off in the background** and reconcile the reported version before writing the plan. Where the harness supports background tasks, this is the default rather than an optimization.

```
pwsh ./install.ps1
```

This packs the tool, reinstalls it globally, verifies `supervisor --version`, and deploys the harness skills. Report the resulting version.

> **Until packaging exists**, there is no `install.ps1`. Say so explicitly and hand the developer the direct form instead — `dotnet "<abs path>/Supervisor/bin/Debug/net10.0/supervisor.dll" <cmd>` — and **state which build is live**. A CLI smoke run against a stale `supervisor` on PATH proves nothing.

**Never deploy while the Hub is running from a different build.** `supervisor hub status` first; a version-skewed Hub refuses enrollment by design (D28), and you will misread that refusal as a bug in whatever you just changed. Stop the Hub, deploy, restart it — and remember stopping the Hub kills every Owned Agent's terminal.

---

### Phase 3 — Ground in the current system

pre-impl usually runs in a **fresh context window**, so before interviewing the developer, ground yourself in what TheSupervisor already is — first from its specs and decisions (3a), then, for changes to existing behavior, from the running system (3b).

**3a. Read the SDD selectively.** Ground yourself in the `docs/sdd/` folder — the living specs that track what TheSupervisor *currently is*. Build one candidate tag set as you route the request, using the controlled `prd:` / `arch:` / `area:` vocabulary in `docs/artifacts/README.md`, then reuse it across every index:

1. `docs/sdd/thesupervisor-prd.md` — read its **Grounding Index**, then the universal product baseline (`Problem Statement`, `Product Vision`, `Target Users`) plus only the matching FR/NFR sections.
2. `docs/sdd/thesupervisor-architecture.md` — read its **Grounding Index**, then the architectural approach plus only the matching design/data-flow/convention sections.
3. `docs/sdd/thesupervisor-stories.md` — read its **`## Index`** (Area | Stories | Summary), then the **1–3 `## Area:` sections** the work touches, **in full**. Story IDs are self-minting UTC timestamps — there is no shared "next free ID" to read. If the Phase 4 grill later surfaces an area you did not load, read that area **then** — don't guess from the index summary.

A full read of any indexed SDD doc is rarely warranted; do it only if the work genuinely spans most of that document (a cross-cutting refactor, say). Reading a whole file "to be safe" wastes the context this phase exists to spend well.

**Also read the two documents the SDD does not replace:**

- **`CONTEXT.md`** — the ubiquitous language. Every term this plan uses must match it. **Agent**, **Owned/Foreign**, **Hub**, **Roster**, **Command**, **Workstream**, **Peer**, **Repository**, **Machine** all have precise, non-obvious meanings, and several have explicitly banned aliases. Using "session" where the domain says **Agent**, or "supervisor" where it says **Hub**, will make the plan read as if it were written about a different system. If the grill surfaces a term that isn't there or contradicts what is, that is a `--with-docs` moment.
- **`docs/adr/`** — the decision record. These are short; **read the titles of all of them and the body of any that touch your area.** They exist to stop you re-deciding something that was already decided against real alternatives. ADR-0003 in particular has been challenged once and upheld; do not reopen it casually.

`docs/plans/design.md` is the founding design session's record, carrying decision ids `D1`…`Dn` that plans cite. Consult it when you need to know *why* something is the way it is and the ADRs don't cover it. **It is not updated as the system evolves — when it disagrees with the SDD, the SDD wins.**

**Related-work scan (the archive knows how it came about).** The SDD says what the system *is*; the plan archive says *how it got that way* — decisions, rejected alternatives, the traps prior work hit. Every row in `docs/artifacts/README.md` carries **tags** (`prd:FR-N`, `arch:<slug>`, `area:<slug>` — vocabulary declared at the top of that README). After the SDD read identifies the FRs, components, and areas this work intersects:

1. **Translate them to candidate tags** and filter the index by those tags (a grep over the README rows works: `grep 'area:roster-ui' docs/artifacts/README.md`).
2. **Absorb every matching row's summary** — breadth is cheap; the summaries are dense.
3. **Fully read at most ~3 closest-ancestor artifact docs** (weigh recency + how directly they shaped the thing you're changing) — depth is expensive; the cap is the point. State which docs you read.

Carry what the ancestors teach — prior decisions, rejected approaches, named traps — into the grill and the plan's Context, exactly like the FRs and conventions from the SDD read.

**Already grounded?** This phase assumes a **fresh context window** — the usual case. When that assumption is false (a continuation session that already read, or *authored*, these documents), re-reading them buys nothing. You may **skip a grounding read you already hold from this session**, but you must **state which documents you are relying on and where that knowledge came from**, so the developer can catch you if you're wrong:

> "Grounding: skipped the re-read — this session authored the architecture *terminal* section and the stories index. Relevant area: In-App Terminal."

In a genuinely fresh window, or whenever you are unsure whether what you "know" came from the file or from inference, **read**. This is a continuity exception, not a license to skim.

This is **grounding, not editing** — make no changes here. The point is that the grill (Phase 4) and the resulting plan land *inside* the existing system: correct domain terminology, the right FR to extend (vs a redundant new one), the right component to touch, existing conventions honored, and no reinventing or contradicting what already ships.

**3b. Classify the change, then reproduce / exercise the current behavior.** Reading specs isn't the same as seeing the system run. Classify the requested change from `$ARGUMENTS` / the request as **new feature**, **update to an existing feature**, or **bug fix**.

> **This classification is binding — it names the plan, the branch, and the commits (Phase 9).** A change is a **bug fix** only if its entire deliverable is restoring intended behavior. If it ships anything with utility beyond closing the defect — a new flag, a new surface, a reusable capability — it is a **feature**. **When in doubt, feature.** State the classification to the developer and let them correct it.
>
> Under **`--hotbug`** the classification is **bug fix, by definition** — and if the work you just grilled looks like a feature, say so and push back on the flag rather than shipping a feature straight to trunk.

Under `--hotbug`, still **reproduce the bug** (the default below): a fix going to `main` unreviewed is the *last* place to guess at the defect.

Then **offer the developer a choice** of how to ground in the *live* system before planning — a **multi-select** where the harness supports one (in Claude Code, use `AskUserQuestion` with `multiSelect: true`; otherwise ask plainly). Pre-select the default by change type:

- **Bug fix (declared)** → **default: reproduce the bug.** Drive the failing flow yourself so the plan targets the *real* defect, not the reported symptom: for a **UI** bug, run the deployed `supervisor ui` and drive it with the **Playwright CLI** (not the Playwright MCP — no system Chrome here); for a **CLI/behavioral** bug, invoke `supervisor <command>`; for an **enrollment or Roster** bug, start a real `claude` session in a scratch directory and watch whether it appears — that is the one flow no fake can prove.
- **Update to an existing feature (declared)** → **default: exercise the current feature** the same way, so you understand the behavior you're about to change before changing it.
- **New feature, or change type unspecified** → **default: skip** (there is nothing to reproduce yet). Still present the option — the two choices are *"do an exploratory run of an adjacent/related capability"* vs *"skip and go to the grill"* — but leave **skip** selected.

Honor the developer's selection; if they decline, proceed. Whatever you observe (real behavior, repro steps, surprises, error codes) feeds the grill (Phase 4), the test plan (Phase 7), and the plan's Context — a bug plan should cite the reproduction; an update plan should reflect the current behavior you exercised.

---

### Phase 4 — Understand the feature (grill)

Run the grilling skill to converge on shared understanding (one question at a time, recommend answers, walk the decision tree):
- Default → the **grill-me** skill.
- `--with-docs` → the **grill-with-docs** skill (same, plus terminology vs `CONTEXT.md` + decisions written into `CONTEXT.md`/ADRs inline).

If neither skill is available in your harness, run the equivalent interview by hand: ask one question at a time, recommend an answer, and resolve each branch of the decision tree before moving on.

**Prefer `--with-docs` when the work introduces a noun.** TheSupervisor's domain language is unusually load-bearing — the difference between an **Agent** and a **Workstream**, or an **Owned** and a **Foreign** Agent, decides what the UI is even allowed to offer. If the grill starts inventing vocabulary, stop and switch.

**Detect UI.** During the grill, determine whether this is a **UI feature** — it touches `WebUI/` (new/changed React components, routes, panes). Confirm with the developer. This decides Phase 5's strategy and whether MSUs (Phase 6) are mandatory.

**Detect the blast radius.** Two further questions materially change the plan and are easy to miss:
- **Does it touch the per-session fast path** (the MCP shim or a hook)? That path has a CI-enforced startup budget (D10). Any dependency added there is a plan-level decision, not an implementation detail.
- **Does it change the enrollment contract?** If so, the protocol version moves (D28) and every running Agent de-enrols until restarted. That belongs in the plan, called out, with a migration note.

---

### Phase 5 — Pick the build strategy (it shapes the plan)

The decomposition **is** the plan, so choose now — you can't defer it to the impl skill:
- **Non-UI → fan-out into independent work units (default).** Decompose into *independent, parallelizable work units* with minimal cross-dependencies so implementation can proceed unit-by-unit (or concurrently where the harness supports it). No per-phase pause.
- **UI → sequential MSU mode (Phase 6).** UI-affecting units are built + smoke-tested one at a time; parallel fan-out does not apply.
- **Mixed features are both:** backend-only units fan out; UI-affecting units are sequential MSUs.

Record the chosen shape in the plan so the impl skill executes the right one.

---

### Phase 6 — MSU decomposition (UI features)

For any **UI-affecting** work, decompose each phase into **Minimum Smoke-testable Units (MSUs)** — the smallest units that each produce an *observable, smoke-testable behavior*. In the plan, every MSU documents:
- **Scope** — the smallest slice.
- **The smoke test that proves it** — a concrete "drive this route/interaction → see this," authored *with* the MSU, not after.
- **Delivery order** — MSUs are sequential.

**Mandatory smoke-test stop per UI-affecting MSU.** The impl skill **STOPS** after each so the developer drives the *real* UI and confirms before the next. This is non-negotiable for UI (and is exactly why UI work is sequential, not fanned out) — mark it in the plan so the impl skill honors it unconditionally. Backend-only MSUs in a mixed feature can still fan out / run without a stop.

**Record the UI-MSU rhythm in the plan:**
1. **TDD** — vitest test-first for React MSUs; use the **tdd** skill for any .NET/CLI MSU.
2. **Iterate on the fast dev loop** — HMR against the dev server, not the slow build+repack.
3. **Developer smoke-tests** the MSU in the real UI (the mandatory stop).
4. **Ship gate before commit** — `npm --prefix WebUI run build` (→ `Supervisor/wwwroot`) **+ commit the regenerated `wwwroot`**. The .NET build only *copies* `wwwroot`; the committed bundle is what ships — never let a `WebUI/` change land without rebuilding + committing the bundle (guards the stale-bundle trap).
5. **Commit the green MSU** (`MSU N: …`), then the next.

**A Roster or terminal MSU has a second smoke dimension: real Agents.** A pane that looks right with one fixture row can be wrong with four live sessions across two repos, one blocked on input. When the MSU touches the Roster, the smoke test says *how many real Agents in what states* — not just "open the page."

---

### Phase 7 — Write the test plan (MANDATORY — the TDD gate)

**No plan leaves this skill without a test plan.** This is the phase that makes `impl` able to work test-first; if it is vague here, impl will write code first and retrofit tests, which is the failure mode this whole structure exists to prevent.

For **every work unit and every MSU**, the plan names:

1. **The tests that will exist** — by level and by name-or-intent, not "add tests". "`RosterMergeTests.PeerRowsMarkedStaleOnPartition`" is a test plan; "unit test the merge" is not.
2. **The level each test sits at** — and the level is a decision, not an accident:

   | Level | Use for | Runs against |
   |---|---|---|
   | Unit | Roster merge, Command state machine, Workstream keying, tag/slug parsing | Pure objects, no I/O |
   | Component | Hub services, control service, session manager | Real objects + **fakes at the edges** |
   | Contract | The CLI surface and the MCP tool surface agree | Both surfaces over one shared service |
   | Hermetic e2e | Enrollment → Roster → Command → UI | **Fakes at the edges, real transports in the middle** |
   | Developer smoke | Real `claude`, real PTY, real second machine | Named, manual, **never a CI dependency** |

3. **Which seam each fake plugs into.** The seams are architectural and must exist before the code: a **fake Agent** that speaks the enrollment protocol without a real Claude, a **fake Peer** so federation is testable on one machine, a **fake PTY** process, and **recorded transcript fixtures** for Activity Summary derivation. If the work needs a seam that doesn't exist yet, **building that seam is a work unit in this plan** — not a footnote.
4. **What is deliberately NOT automated, and why.** Real Claude sessions need auth and burn tokens; a second machine isn't in CI. Those stay named developer smokes. Say so explicitly, with the exact steps a human runs — an unnamed manual test is an untested path.
5. **The failing test that starts the work.** For a bug fix this is the reproduction from Phase 3b, expressed as a test. For a feature it is the first behavioral assertion of the first work unit. Impl starts by making it fail for the right reason.

**Docs / config plans get a test plan too** — as **concrete, runnable checks**, named here so impl doesn't have to invent them: **invariant greps** ("no duplicate story ID: `grep '^### ' … | uniq -d` returns empty"), a **content-preservation diff against `HEAD`** (a restructure must not silently drop content), **link/anchor checks**, schema validation — plus the **full build + test suite whenever the change touches build config** (`*.csproj`, CI yml are code by another name).

**Time-dependent behavior is a design constraint, not a test problem.** Command TTL, the reconnect grace, host idle timeout, and summary staleness are all clock-driven. If the work touches any of them, the plan states how the clock is injected — a test that waits fifteen minutes will simply never be run.

> **The bar:** a competent engineer reading only the test plan should be able to write the tests without reading the rest of the plan. If they'd have to guess, it isn't done.

---

### Phase 8 — Demos (offer for any showable feature)

Ask the developer: **also produce demo(s)?** Pitch it honestly — since MSU work already yields smoke-testable capabilities *with* their proofs, composing several MSUs into a demo is a small step that yields a **peer-showable artifact AND a durable regression fixture**.

If yes, the plan lists **demo artifacts as deliverables**:
- **Composed from the MSUs**, showcasing the feature end-to-end. Choose the format to fit the feature — a seeded fleet scenario (N fake Agents in known states) for a Roster feature; a scripted terminal flow for a terminal feature; a demo script for a CLI capability.
- **Always doubling as e2e fixtures** — wire each demo into the hermetic harness so "the demo runs correctly" *is* the regression assertion. **If no such harness exists yet, standing it up (or extending it) is itself a plan deliverable** — "always double" is a real commitment, not best-effort.
- Each demo carries its own "renders/runs correctly" smoke test — effectively a capstone MSU.

---

### Phase 9 — Name, branch, and write the plan

Only now that the work is understood (so names reflect what it really is):

1. **Slug** — a short kebab-case topic slug.
2. **Apply the bug/feature naming split**, driven by the Phase 3b classification (bug fix → **bug**; new feature or update to an existing feature → **feature**; when in doubt, **feature**):

   | | Bug fix | Feature (default) |
   |---|---|---|
   | Branch | `bug/<slug>` | `feature/<slug>` |
   | Plan | `docs/plans/YYYY-MM-DD-bug-<slug>.md` | `docs/plans/YYYY-MM-DD-<slug>.md` |
   | Impl commits (impl/post-impl) | `fix(<scope>): …` | `feat(<scope>): …` |

   Record the classification **explicitly in the plan** (a `**Type:** Bug fix` / `**Type:** Feature` line) so the impl and post-impl skills pick the right commit type without re-deriving it.

3. **Create the branch — at most ONE branch per feature/bug, never one per phase/plan-run.** Decide by where you are:
   - **`--hotbug`** → **create NOTHING. Stay on `main`.** This is the only case in the entire trio where work lives on trunk. Skip the rest of this step.
   - **Already on this work's branch** (the current branch is the `feature/<slug>` or `bug/<slug>` for this same work, or the plan you are extending/continuing already lives on the current branch) → **stay on it; create nothing.** Later phases of a multi-phase plan, re-runs, and fresh-context continuations all commit to the *same* branch. Do NOT derive a new per-phase slug (`feature/<slug>-p2-…`) — that fragments one feature across stacked branches.
   - **On `main`/trunk (new work)** → `git switch -c feature/<slug>` (or `git switch -c bug/<slug>`). **Always** — being on `main` is never a reason to *stay* on `main`.
   - **On a different, unrelated `feature/` or `bug/` branch** → STOP and ask the developer whether to stack deliberately or restart from `main` (`--main`).
   (pre-impl **owns** branch creation; it happens here, deliberately, not earlier — and at most once per feature/bug.)
4. **Write the plan** — at the path from the table above. Per `docs/plans/README.md` the date prefix is when the plan is *documented* (today); the post-impl skill re-dates it to the implementation date when it moves to `docs/artifacts/` (**preserving the `bug` segment**). Use the **same slug** as the branch.

**The plan MUST open with this header** — `docs/plans/` is a *backlog*, not a work queue, and the impl skill resolves which plan to build by matching **`**Branch:**`** against the checked-out branch. Without it that match is guesswork:

```markdown
# <Title>

**Branch:** feature/<slug>          <!-- or bug/<slug> — the branch created in step 3; how impl finds this plan -->
**Type:** Feature                   <!-- or: Bug fix — drives feat:/fix: commits in impl + post-impl -->
**Tags:** `area:<slug>` `arch:<slug>` <!-- SDD-anchored tags from the Phase 3a grounding; vocabulary in docs/artifacts/README.md; ≥1 area:, ≤6 total -->
**Date documented:** YYYY-MM-DD
```

The `**Tags:**` line comes straight out of the Phase 3a grounding (the FRs/components/areas the work intersects, as tags). post-impl **reconciles** it against what actually shipped and copies it into the artifacts index row — so stamp what you know now; drift is expected and handled.

Write `**Branch:**` with the branch's **exact** name, and never let it drift: if a later run moves the work to a different branch, update this line. One plan names one branch; one branch carries one plan.

**Under `--hotbug` the header is different, and the callout is not optional** — the impl skill **refuses to implement any plan while on `main`** unless it finds this declaration, so it is both a warning to humans and the literal unlock:

```markdown
# <Title>

> ## ⚠️ HOTBUG — TRUNK-DIRECT
> **This plan lands on `main` with NO branch and NO PR.** The review gate is deliberately skipped
> because the fix is urgent. Everything here goes straight to trunk: read it as if it were already
> in production, because it is about to be.

**Hotbug:** YES                     <!-- the flag impl checks before it will run on main -->
**Branch:** main                    <!-- deliberate: trunk-direct, no branch is ever created -->
**Type:** Bug fix                   <!-- a hotbug is always a bug fix -> fix: commits -->
**Tags:** `area:<slug>` `arch:<slug>` <!-- same tag rules as a normal plan -->
**Date documented:** YYYY-MM-DD
```

The `**Hotbug:** YES` line and the `⚠️ HOTBUG — TRUNK-DIRECT` callout are a matched pair — write **both**. Never write `**Hotbug:** YES` into a plan the developer did not explicitly flag as a hotbug.

The plan then contains:
- **Context** + the **locked decisions** from the grill, citing the `D<n>` ids from `design.md` and the ADRs it rests on.
- **Phases** — always. UI phases carry the **MSU breakdown** (scope + smoke test + order) and the mandatory-smoke-test-stop marking; non-UI phases carry the parallel work-unit decomposition.
- **Build strategy** (fan-out work units vs sequential MSUs) recorded so the impl skill runs the right shape.
- **Test plan — MANDATORY (Phase 7).** Per work unit and per MSU: the tests, their level, the seams and fakes they need, what stays a manual smoke and why, and the failing test that starts the work. A plan with no test plan is **incomplete** and impl should refuse it.
- **Verification** — the suites that must be green to call it done, plus any invariant checks.
- **Demos + e2e** deliverables, if chosen.

Then run **Phases 10 and 11** (below) before committing, so the summary and the friction log land in the same commit.

Make a **local** `docs(plan): <slug>` commit of the plan on the branch (reviewable + resettable — no push; push is the gate). **Under `--hotbug` this commit lands on `main` and is still local — pre-impl never pushes, hotbug or not.**

**Then STOP.** pre-impl ends at a plan on a named branch, ready for review. It does **not** implement — hand off:

> **Next:** review the plan in `docs/plans/`, then run the **impl** skill to build it (which ends in local commit(s); UI plans stop for a smoke test per MSU). Finish with the **post-impl** skill (branch-aware) for docs + push/PR.

Under `--hotbug`, say so explicitly instead:

> **Next (HOTBUG):** review the plan — it is on `main`, and impl will build it **on `main`** because the plan declares `**Hotbug:** YES`. post-impl will then push **straight to trunk, no PR**. Nothing else in the trio will ever put work on `main`.

---

### Phase 10 — Actions taken (summary table)

Every skill in the trio ends with the **same table**, so three runs read alike:

```markdown
## Actions taken

| Action | Target | Result |
|--------|--------|--------|
| Synced | main | fast-forward → da76b13 |
| Deployed tool | global | 0.3.1-enrollment.4 (or: deferred — no code surface) |
| Classified | — | Feature (or: Bug fix / ⚠ HOTBUG — trunk-direct) |
| Grounded | SDD + CONTEXT + ADR | PRD + architecture read; stories areas: Agent Enrollment & Lifecycle; ADR-0002, ADR-0005 |
| Related work | docs/artifacts/README.md | 4 rows matched `area:roster-ui`; read 2 ancestor docs (or: no tag matches) |
| Reproduced | supervisor list | <observed failure> (or: skipped — new feature) |
| Test plan | <n> units | <n> tests named; seams needed: fake Peer (new work unit) |
| Created branch | feature/<slug> | new (or: stayed on <branch> / none — HOTBUG, on main) |
| Wrote plan | docs/plans/<file>.md | N phases, test plan + verification specified |
| Committed | docs(plan) | <hash> — local, NOT pushed |
```

**Rules that make it useful rather than decorative:**
- **Every Result is verifiable** — a hash, a version, a path, a count, a branch name. Never "done" or "✅".
- **Report the non-actions too.** A deferred deploy, a skipped repro, a grounding read you already had in context — each gets a row saying so. A step that silently vanishes is indistinguishable from a step that was forgotten.
- **Anything skipped, deferred, or refused appears**, with the reason in the Result cell.
- **This is the only summary** — do not also emit a second table or a prose recap of the same facts.

---

### Phase 11 — Friction log (self-improvement)

The trio has no other feedback loop: friction you hit *running these skills* evaporates when the context window closes unless it is written down. So before committing the plan, look back over **this session** and ask whether anything about **pre-impl itself** should change.

**Write the block at the tail of the plan** — always, even when there is nothing to report (impl and post-impl append to it later):

```markdown
<!-- FRICTION:START -->
## Skill Friction Log

> Friction is **marked, never deleted**: every entry ends in a `**Status:**` line — `Open` (the default),
> `Resolved <date> — <what/where>`, or `Declined <date> — <reason>`. Open entries are the backlog for
> improving these skills. **Empty is a valid state — do not pad it.**

_No friction logged._
<!-- FRICTION:END -->
```

Replace `_No friction logged._` with one entry per real friction point:

```markdown
### F-1 — pre-impl · Phase 3a (grounding)
**What happened:** <Enough narrative that a fresh context window — one that never saw this session —
can reason about a fix: what the skill told you to do, what you actually did, where it misled you or
wasted effort, and what that cost. Name the phase, the file, the command.>
**Recommendation:** <Only when there is a clear one. Omit the line entirely otherwise.>
**Status:** Open
```

Number entries `F-1`, `F-2`, … — impl and post-impl **continue** the sequence rather than restarting it.

**Every entry MUST end with a `**Status:**` line** — `Open` when you log it. If you *resolve* a friction point during this same run (you fixed the skill as you went), flip that line to `**Status:** Resolved <date> — <what/where>` (or `**Status:** Declined <date> — <reason>`) — **not** an ad-hoc heading suffix or an `*(applied)*` note. Open work is found by grepping `Status: Open`.

**The bar.** Log friction that is **recurring or structural** — something an edit to the skill would actually prevent next time. Do **not** log: one-off environment hiccups (a flaky network call, a locked `.exe`, an expired token); trivia (a typo, a stale line number); anything you already fixed in-session; or feedback about the *codebase* rather than the *skill*. **Writing nothing is the expected outcome of a clean run** — an empty log is a signal, not a failure.

**Ask before you write.** Draft the candidate entries, show them to the developer, and let them confirm, edit, add, or drop. Then write the confirmed set.

### Claude Code accelerations (optional)

These speed the phases up in Claude Code and are **not required** — the numbered phases above stand on their own in any harness.

- **Phase 2 (build) as a background task.** Kick off the deploy in the background and continue into the Phase 3 SDD read + Phase 4 grill while it runs; reconcile the reported version before writing the plan.
- **Phase 5 fan-out via sub-agents / worktrees.** For non-UI work units, the plan can note that the impl skill may build independent units concurrently in parallel **git worktrees** (one sub-agent per unit). This is purely an execution accelerant for the impl stage — the *decomposition* into independent units is the harness-neutral requirement; whether they run concurrently is up to the executing harness.
- **Grill / TDD as invoked skills.** Where available, invoking the sibling skills (grill-me, grill-with-docs, tdd) is faster than reproducing their loops by hand.
- **A real fleet for Phase 3b.** Claude Code can start background sessions, which is the cheapest way to get several live Agents in known states to reproduce a Roster behavior against.
