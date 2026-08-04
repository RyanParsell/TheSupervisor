---
name: impl
description: Implement a TheSupervisor plan from docs/plans/ test-first against the plan's test plan, ending in a single local commit — `feat:` for a feature plan, `fix:` for a bug plan (a `YYYY-MM-DD-bug-<slug>.md` file) — with a green build + tests, no docs, no push. Refuses to implement on main unless the plan declares **Hotbug:** YES (pre-impl's --hotbug trunk-direct escape hatch). Refuses to implement a plan that has no test plan. Ends by logging any skill friction into the plan. Use when asked to implement, build, code up, or fix a planned CLI command, Hub capability, WebUI feature, bug, or other change in this repo. Afterward, run the post-impl skill (branch-aware) to add docs and push/open the PR.
user_invocable: true
version: 1.0.0
# ⚠️ IMPORTANT: When editing this file, increment the patch version above (e.g., 1.0.0 → 1.0.1).
# Derived from the WExpert skill trio (C:\Code\MS\CLI\skills), retargeted to TheSupervisor and
# hardened around test-first development.
---

## Implementation: $ARGUMENTS

This skill implements one or more plan files from `docs/plans/` into working, tested code and captures the result as a **single local commit** — `feat:` for a feature plan, `fix:` for a bug plan — then stops. It does not write docs, does not move the plan file, and does not push.

**Harness note.** The core workflow below is sequential and works in any agent harness. One optional acceleration (parallel git worktrees + sub-agents) is a Claude Code optimization, clearly marked as such — other harnesses simply implement the work units in sequence and get the same result. Skills are invoked differently per harness (Claude Code: `/name`; Codex: `$name` or via `/skills`; GitHub Copilot: activates by description); this doc refers to other skills by name (e.g. "the post-impl skill").

### Output Style

- Output brief phase indicators: "Phase 1: Discovery...", "Phase 2: Writing tests...", etc.
- Do NOT narrate each file read or edit
- Do NOT add filler text like "Let me now...", "Good, that worked..."

---

### Phase 0 — Resolve Plan Files

**Goal:** Determine which plan(s) to implement based on `$ARGUMENTS`.

**Plans directory:** `docs/plans/` (relative to project root)

> **`docs/plans/` is a BACKLOG, not a work queue.** It holds future/aspirational plans that nobody asked you to build, and occasionally a plan whose implementation already shipped but that post-impl never archived. **Never implement every plan in the folder.** Resolve to exactly the plan(s) the developer means — and when that is not unambiguous, **ask**.

**Resolution rules** (exclude `README.md` and `design.md` from all matching — `design.md` is the founding design record, not a plan, and is never implemented):

1. **Argument given** — find the matching plan:
   - Try exact match: `docs/plans/$ARGUMENTS`
   - Try with `.md` suffix: `docs/plans/$ARGUMENTS.md`
   - Try glob/substring match: `docs/plans/*$ARGUMENTS*.md`
   - If multiple plans match a substring, list them and ask the user to disambiguate
   - If no match, list available plans and ask the user which one to implement

2. **No argument → match the current branch.** The pre-impl skill stamps every plan with a `**Branch:**` header naming the branch it belongs to. Use it:
   - `git branch --show-current`, then find the plan whose `**Branch:**` equals it. **Exactly one match → that is the plan.** This is the deterministic path and the normal case.
   - **No `**Branch:**` header** (a plan written by hand): fall back to matching the branch's slug against the plan filename (`feature/foo-bar` / `bug/foo-bar` → `*foo-bar*.md`).
   - **Still ambiguous, or on `main`/trunk** (where no plan "owns" the branch): list the candidate plans with their `**Branch:**` and dates and **ask the developer which to implement**. Do not guess, and do not default to "all of them."
   - **More than one plan claims the current branch** → surface the conflict and ask; one branch carries one plan.
   - No plans at all → report "No plans found in `docs/plans/`" and stop.

3. **Multiple plans** are implemented in one run only when the developer **explicitly** names them.

**Before implementing, sanity-check the plan is not already done.** A plan whose story already appears in `docs/sdd/thesupervisor-stories.md`, or whose commits are already on the branch, was implemented and simply never archived — say so and stop rather than rebuilding it.

**Output:** A resolved list of 1+ plan file paths. Each plan is an independent work unit.

**Determine each plan's type** (it sets the commit type in Phase 6): a plan is a **bug** plan if it declares one (a `**Type:** Bug fix` line, written by pre-impl) or its filename carries the `-bug-` segment (`docs/plans/YYYY-MM-DD-bug-<slug>.md`). Everything else is a **feature** plan — **feature is the default**. Carry the type forward; do not re-litigate it here.

**REFUSE TO IMPLEMENT A PLAN WITH NO TEST PLAN.** pre-impl's Phase 7 is mandatory and produces a **Test plan** section naming, per work unit, the tests that will exist, their level, the seams and fakes they need, and the failing test that starts the work. If the resolved plan has no such section — or has one that says "add tests" without naming them — **STOP**:

> "This plan has no test plan, so I can't implement it test-first. Run the **pre-impl** skill on this branch to add one (its Phase 7), or tell me to author the test plan here first and you review it before I write any code."

Authoring it here on request is fine. Silently proceeding and retrofitting tests afterwards is not — that is the exact failure this repo's structure exists to prevent.

**REFUSE TO IMPLEMENT ON `main`.** Run `git branch --show-current`. If it is `main`/`master`, **STOP** — work belongs on a branch, and the pre-impl skill exists to create one:

> "This plan isn't a hotbug and I'm on `main`. Implementation belongs on a branch. Run the **pre-impl** skill to plan it onto `feature/<slug>` (or `bug/<slug>`), or — if the plan already exists — cut the branch it names in its `**Branch:**` header and re-run me there."

**The ONE exception is a hotbug.** If the plan declares `**Hotbug:** YES` (pre-impl writes this only when the developer passed `--hotbug`, alongside a `⚠️ HOTBUG — TRUNK-DIRECT` callout), then implement **on `main`** — that is the whole point of the flag. Say so out loud before you start:

> "HOTBUG: implementing on `main`. No branch, and post-impl will push straight to trunk with no PR."

Do **not** infer a hotbug from urgency, from the developer's tone, or from the plan being a bug fix. Only the literal `**Hotbug:** YES` declaration unlocks `main`. If someone asks you to "just do it on main" without that declaration, point them at pre-impl's `--hotbug`.

**Scope & branch behavior.** This skill implements onto **whatever branch is already checked out** — it never creates or switches branches, never assumes `main`, and (deliberately) **does not touch docs, plans, or `origin`**. In particular, for a **multi-phase plan** every phase — including phases run later in fresh context windows — is implemented and committed on the **same, already-checked-out `feature/` or `bug/` branch**; never cut a new `feature/<slug>-pN-…` branch per phase. (The only branches this skill ever makes are the throwaway worktree branches in the optional Phase 2 acceleration, which are deleted in Phase 5 and never pushed.) Its whole job is: implement → integrate → green build/test → make a **local `feat:`/`fix:` commit**. Then it stops.

- **Docs, the plan lifecycle (move-to-`artifacts` vs keep-in-`docs/plans/`), push, and PR are all the post-impl skill's job.** Run the post-impl skill *after* this one — it's **branch-aware**. The plan-lifecycle decision lives entirely in post-impl, so this skill stays out of it and there's exactly one place that decision is made.

---

### Phase 1 — Discovery (read-only)

**Goal:** Read every resolved plan, decompose into work units, and absorb existing patterns.

**1a. Read each plan file — including its Test plan.** For each plan, identify:
- The scope of work (new commands, Hub capabilities, UI features, modifications)
- **The named tests, their levels, and the seams/fakes they require** — this is your work list for Phase 2, not an appendix
- Dependencies between plans (shared files, ordering constraints)
- Whether any plans conflict (modify the same files in incompatible ways)

**1b. Decide the work-unit breakdown.** Treat each plan file as one work unit; within a plan, subdivide into sequential steps if it helps. Note two things for later:
- Shared files like `Program.cs` (CLI registration) are integrated in Phase 4, not touched piecemeal while implementing individual units.
- If plans have dependencies or conflicts, record the order they must be integrated in (Phase 3).
- **A seam the test plan needs but that doesn't exist yet is a work unit of its own, and it comes first.** Building `FakePeer` before the federation code that needs it is the correct order, not overhead.

**1c. Read reference patterns.** Read at least TWO test files and TWO implementation files from relevant domains to absorb conventions.

> **Non-code plans (docs, skills, SDD, CI, packaging config).** "Two test files and two implementation files" is meaningless for a Markdown or YAML plan — **read two comparable artifacts instead**: an existing `SKILL.md` when editing a skill, an existing story under the target `## Area:` when adding one, the config file's current shape/schema when changing it. The goal is unchanged — absorb the conventions of the thing you're about to edit before editing it.

**Read `CONTEXT.md` if you have not this session.** The domain terms are load-bearing and several have banned aliases. Code that calls a **Foreign Agent** a "remote agent" will read as being about a different system — and "remote" already means *on another Machine*, which is a genuinely different thing.

For CLI commands:
- `[Fact] public async Task` test methods, xUnit `Assert.*` only (no fluent assertions)
- `await cmd.ExecuteAsync(null!, settings, CancellationToken.None)` invocation
- Read `Supervisor.Tests/Fakes/` for the available fakes before writing a new one
- **Spectre branch-leaf `--json` gotcha:** Spectre.Console.Cli does NOT inherit `--json` onto branch-leaf commands — every leaf settings class must redeclare it (`public new bool Json`). The symptom is empty-JSON output from the command, not a parse error, so it survives until a test compares payloads.
- **Rich-output capture gotcha:** `CommandAppTester` does NOT capture output written through the static `AnsiConsole` — asserting on `result.Output` silently compares against an empty string. Commands that emit rich output inject `IAnsiConsole` (registered to `AnsiConsole.Console` in production) and tests use a per-test `TestConsole` seam. Never assign `AnsiConsole.Console` from a test.

For frontend (WebUI) work:
- Read existing components in `WebUI/src/components/` for patterns
- Read existing test files in `WebUI/src/__tests__/` for testing conventions
- React + TypeScript, Vite, Vitest + React Testing Library, custom CSS with design tokens, WebSocket API

---

### Phase 2 — Implement (test-first — red, green, refactor)

**Goal:** Implement each work unit end-to-end, driven by the plan's test plan, then build and verify green.

Implement the work units **sequentially** by default. This sequential path is the complete, first-class way to run the skill.

**The loop is not optional and not reorderable:**

1. **Red.** Write the test named in the plan. Run it. **Watch it fail, and check it fails for the right reason** — a test that errors on a missing type has not yet proven anything about behavior. A test that passes before the code exists is testing nothing; find out why before continuing.
2. **Green.** Write the least code that makes it pass.
3. **Refactor.** Clean up with the tests green. Use the **tdd** skill's guidance on deep modules and interface design here — this is where the design actually gets made.
4. Next test.

> **Never write implementation before its test.** If you catch yourself with working code and no failing test that preceded it, delete the code, write the test, watch it fail, then restore. That sounds wasteful and isn't: the test you write *after* the code is shaped by the code you already wrote, so it tests what you built rather than what was asked for.

> **### Claude Code acceleration (optional)**
> In Claude Code you can fan the work units out in parallel: launch one sub-agent per plan using the Agent tool with `isolation: "worktree"`, all in a **single message**. Each worktree branches off the currently checked-out branch, so agents inherit the committed plan and any prior commits on a feature branch. Each agent prompt must include: the **full plan text including its test plan**; the Phase 1c patterns/conventions; the red-green-refactor loop above; the build+test command for its domain; the explicit instruction **not to modify `Program.cs`** (integration happens in Phase 4); and the instruction to run an autonomous build-test-fix loop (max 10 cycles). Then wait for all agents (see 2d) and proceed to Phase 3 to merge the worktrees. **Other harnesses:** skip the worktrees — implement the same units in sequence in the primary tree; there is nothing to merge in Phase 3, and Phase 5 is a no-op.

> ### The non-code path (a plan with no code surface)
>
> Docs, skills, SDD, CI, and packaging-config plans are common in this repo, and the loop above does not apply to them: there is nothing to TDD and no `dotnet test` that exercises the change. **Do not silently skip verification** — that is precisely how a restructure ships with content dropped. Instead:
>
> 1. **Say there is nothing to TDD.** Don't manufacture a test to satisfy the phase.
> 2. **Name the verification BEFORE you edit.** Take it from the plan's **Test plan** section (pre-impl requires one for docs plans too, as runnable checks). If the plan is hand-written and lacks one, **author the checks yourself and state them up front** — deciding what "correct" means *after* you've edited is how you end up proving nothing.
> 3. **Run it after, and report the actual result** — not "verified," but the output: *"duplicate-ID grep returns empty; sorted-line diff vs `HEAD` shows additions only."*
> 4. **Still run the full build + test suite when the change touches build config** — `*.csproj` and CI yml are code by another name.
> 5. The verification and its result **must appear as a row in the Phase 7 table**, exactly where a code plan reports test counts. An absent row means the work was never proven.

**2a. Set up the work unit.** Implement directly in the checked-out tree (sequential path), or in the unit's worktree (Claude Code acceleration). Do **not** modify `Program.cs` while implementing a unit — CLI registration is integrated in Phase 4.

**2b. Write the tests the plan named**, at the levels it named. Categories to cover, adapted to domain:

For CLI commands:
1. **Happy path** — valid input, exit code 0, correct data written to the formatter
2. **Missing required arguments** — null/empty required fields, exit code 1 with the documented error code
3. **Not found** — entity absent from the fake, returns the documented not-found error
4. **Feature-specific edge cases** — filters, conditional dispatch, flags, empty results

For Hub / control-plane work:
1. **Tier gating** — an action offered on an Owned Agent is *refused* on a Foreign one, with the documented error
2. **Scope isolation** — one Agent (or one Peer, or one UI client) can never read or mutate another's state; assert the forbidden-vs-not-found distinction rather than conflating them
3. **Lifecycle** — enrollment, reconnect grace, expiry, teardown; assert that teardown revokes before it kills
4. **Clock-driven behavior** — Command TTL, idle timeout, summary staleness — through the injected clock, never by waiting

For frontend components:
1. **Rendering** — component mounts, shows expected initial state
2. **User interaction** — click, type, keyboard events produce correct behavior
3. **Edge cases** — empty data, loading states, error states
4. **Accessibility** — ARIA attributes, keyboard navigation

**2c. Run an autonomous build-test-fix loop** (max 10 cycles) per work unit:
- .NET: `dotnet build TheSupervisor.slnx --no-restore --verbosity quiet && dotnet test TheSupervisor.slnx --verbosity quiet`
- WebUI: `npm --prefix WebUI test` — and a filtered/single-file run uses the **same form**: `npm --prefix WebUI test -- <files>`. This runs the package's own `test` script with **cwd = the package dir**, so vitest picks up WebUI's config (`jsdom`, project root). Do **not** substitute `npm --prefix WebUI exec -- vitest run`: it looks equivalent but keeps cwd at the **repo root**, so vitest loads none of WebUI's config and runs in the `node` environment — every test then fails `ReferenceError: document is not defined`, *including ones that just passed*, which reads like your change broke them. **The tell:** vitest's `RUN v… <path>` banner must print the `WebUI` path; if it prints the repo root, the invocation is wrong, not the code.

Then: build first, fix compiler/type errors; test (filtered to the new test class/file), fix implementation (NOT tests); report final pass/fail count.

> **A green `--no-build` test run does NOT imply a green build.** `dotnet test --no-build` runs whatever binaries are already on disk, so it happily reports a full pass over a build that just failed. **The classic trigger here is a running Hub** — a detached `supervisor ui` host holds `Supervisor.Web.dll`, the build dies with `MSB3021`, and the next test run is green on stale bits. Check the build's own exit status / `0 Error(s)` line before trusting any test summary, and **`supervisor hub stop` before building**.

> **The startup budget is a test, and it is easy to break by accident.** Any change that adds a dependency reachable from the `mcp` or `hook` verbs can push the per-session fast path past its budget (D10). If that test goes red, the fix is to move the dependency off the fast path — not to raise the budget. Raising it is a plan-level decision, not an implementation one.

> **Fail-open means "it seems to work" is not evidence.** A broken shim exits 0 silently by design (D19), so a Claude session starting normally proves nothing about enrollment. Verify with `supervisor doctor` and by looking for the Agent in the Roster — never by the absence of an error.

**2d. Collect results.** For each work unit, record: files added/modified, test counts and pass/fail status, and which plan it implemented. (Claude Code acceleration: wait for all parallel agents to finish and collect their worktree paths too.)

**2e. Mandatory smoke-test stop (UI-affecting MSUs).** When the plan marks an MSU as UI-affecting, **STOP** once it is green and hand it to the developer to drive in the real UI. Do not start the next MSU until they confirm. This is the stop pre-impl's Phase 6 declares; honor it unconditionally.

The developer should never have to reconstruct a path, guess a port, or work out how to launch anything. **Hand them a ready-to-run block:**

1. **Absolute paths, always.** Every file the command references is given as a **full absolute path**, never relative to a directory they'd have to be standing in. Quote it, and give it in a form their shell accepts (forward slashes work in both PowerShell and Bash on Windows; a `C:\…` path with backslashes does not survive Bash).
2. **A clickable URL on its own line.** `http://localhost:<port>` — not "open the UI."
3. **Say which URL, and why the other one is wrong** when more than one is listening (the Vite dev server vs the raw backend).
4. **Serve the frontend from source, not the installed tool.** `supervisor ui` runs the `wwwroot` bundle packed at *install* time, so pointing the developer at the installed tool shows them **stale** frontend code and the smoke test silently passes on the old build. For any `WebUI/**` MSU, bring up the Vite dev server proxied to a `supervisor ui --foreground` backend — or reinstall the tool first. **Say which backend is live.**
5. **For a CLI MSU, say which build is live too.** Either redeploy so `supervisor` on PATH is current, or hand them the explicit `dotnet "<abs path>/Supervisor/bin/Debug/net10.0/supervisor.dll" <cmd>` form. A CLI smoke against a stale PATH binary is worse than no smoke, because it looks like a pass.
6. **Set up the fleet the smoke needs.** A Roster MSU is not smoke-tested against an empty list. Say how many real Agents in which states, and how to get there — e.g. *"start two `claude` sessions in different repos, leave one at a prompt so it goes `waiting`."*
7. **Prove the setup before handing it over.** Confirm the Hub is up (`supervisor hub status`), the route responds, and the data is actually present. A pane that renders "No agents" proves nothing.
8. **State what "pass" looks like and how it fails.** Name the specific observable (*"the blocked Agent sorts above both busy ones"*), and the failure mode you're hunting (*"if all four sort by recency instead, the attention comparator isn't wired"*). Without the failure mode they can't tell a pass from a thing that merely looks fine.

If starting servers, note that they stay running, and offer to stop them when the developer is done. **Stop the Hub BEFORE the next build** — see the Phase 2c `MSB3021` warning.

---

### Phase 3 — Merge Worktrees (Claude Code acceleration only)

**Skip this phase entirely on the sequential path** — the work already lives in the primary tree. This phase only applies when Phase 2 used parallel worktrees.

**Goal:** Copy all agent work into the main tree, keeping worktrees alive until the final commit succeeds.

**3a. For each worktree**, identify changed/new files:
```
cd <worktree_path>
git diff --name-only HEAD                 # modified tracked files
git ls-files --others --exclude-standard  # untracked new files
```

**3b. Merge order.** If plans have dependencies or conflicts (noted in Phase 1b), merge in dependency order. Otherwise merge in any order.

**3c. Copy files to the primary working tree.** ("Primary tree" = the checkout this skill runs in — the non-worktree clone — whichever branch is checked out there. It is NOT the `main` *branch*; nothing in this phase switches branches.) For each worktree:
- Copy NEW files into the primary tree
- Copy MODIFIED files (if not `Program.cs`) into the primary tree
- **Skip `Program.cs`** from all worktrees — it's integrated manually in Phase 4
- If two worktrees modified the same file, merge changes manually (read both versions, combine)

**3d. Do NOT clean up worktrees yet.** Keep them alive as a safety net until Phase 4's full build+test passes green (that green run is what proves the merge was complete). Cleanup happens in Phase 5.

---

### Phase 4 — Integrate in Program.cs (CLI only)

**Goal:** Manually integrate all command registrations, verb defaults, and using statements into `Program.cs`. **Skip this phase entirely if no CLI commands were added.**

**4a.** Review what each work unit needs registered (from its diff, or the plan).
**4b.** Apply all changes to the main tree's `Program.cs`:
- Add `using` statements for new namespaces
- Add entries to the `defaultVerbs` dictionary
- Register commands in the appropriate branch (or create new branches)
- Remove stubs that were replaced

**4c.** Build and run the full test suite:
```
dotnet build TheSupervisor.slnx --no-restore --verbosity quiet
dotnet test TheSupervisor.slnx --verbosity quiet
```

**4d.** If build or tests fail, fix issues. Common problems:
- Missing `using` statements
- Namespace mismatches
- Duplicate registrations
- Locked `.dll` files — a running Hub (`supervisor hub stop`, then retry)

---

### Phase 5 — Clean Up Worktrees (Claude Code acceleration only, build-gated)

**No-op on the sequential path** (no worktrees were created).

**Only run this once Phase 4's full build+test is green.** That green run is the safety gate: had the Phase 3 merge dropped a file, the build/test would have failed there — so a green Phase 4 proves the primary tree has everything, and the worktrees are safe to discard.

- **If the build/test is RED and can't be made green:** do NOT clean up. Leave the worktrees in place as a debugging safety net, report the failure, and stop.
- Otherwise remove each worktree and its throwaway branch:
  ```
  git worktree remove <path> --force
  git branch -D <branch>
  ```

Cleanup happens here — **before** the commit — so no worktree file locks interfere with the commit (a known Windows issue), and so the post-impl skill never has to know worktrees existed.

---

### Phase 6 — Local `feat:`/`fix:` commit (no push)

This skill captures the implementation as a **single local commit** and **stops**. It does **not** push, and it does **not** commit docs (those are the post-impl skill's `docs:` commit, layered on top later).

**6a. Stage the implementation.** Stage the new + modified code/test files this run produced. Exclude unrelated working-tree changes (check `git diff --stat`), and do **not** stage the plan file — it stays as-is for the post-impl skill to move or keep. (The one exception: Phase 8's friction log edits the plan; commit that with, or immediately after, this commit — never leave it uncommitted.)

> **Non-code plans: the deliverable docs ARE the implementation.** For a docs/skills/SDD/config plan, the plan's own deliverable files — edited `skills/*/SKILL.md`, restructured SDD docs, README conventions, CI/config — **are** the implementation and belong in this `feat:`/`fix:` commit. "Does not commit docs" means specifically the layer post-impl owns: the *new* story for this change, the plan move to `docs/artifacts/`, and the docs ledger. Do not defer the plan's deliverables to post-impl — that leaves this commit near-empty and buries the implementation under a later `docs:` subject.

**6b. Commit locally** with a Conventional-Commits subject whose type is the **plan type resolved in Phase 0** — `fix:` for a bug plan, `feat:` for a feature plan (the default):
```
<feat|fix>: <concise description of what was implemented>

<One-line summary of each major area>. <N> total tests, 0 failures.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
```
If multiple plans were implemented, summarize all of them. If they are of mixed type, use `feat:` and name the fix in the body.

**6c. Do NOT push.** The commit is local only — this preserves the review gate (trivially `git reset` / `git commit --amend`-able before anything reaches `origin`). Pushing, the `docs:` commit, the plan move, and the PR are all the post-impl skill's responsibility.

---

### Phase 7 — Actions taken (summary table)

Every skill in the trio ends with the **same table**, so three runs read alike. **One table — no separate metrics/status tables alongside.**

```markdown
## Actions taken

| Action | Target | Result |
|--------|--------|--------|
| Resolved plan | docs/plans/<file>.md | matched **Branch:** → <branch> (or: named by the developer) |
| Test plan | — | present, <n> tests named (or: ⚠ refused — no test plan) |
| Implemented on | <branch> | (or: ⚠ HOTBUG — on main, plan declares Hotbug: YES) |
| Work unit 1 | <scope> | ✅ green (or ❌ failed — <why>) |
| Work unit N | <scope> | … |
| Seams built | <fake> | FakePeer added (or: none needed) |
| Tests | Supervisor.Tests | 412 ×2 TFMs, 0 failures (N new) |
| Tests | WebUI | 96 passed (N new) |
| Startup budget | mcp/hook fast path | <n> ms, budget <n> ms |
| Verification (non-code) | <the named check> | <its actual result> |
| Smoke stop | MSU <n> | developer confirmed (or: awaiting confirmation) |
| Build/verify cycles | — | N |
| Worktrees | — | N cleaned (or: none created) |
| Committed | <feat\|fix>: | <hash> — local, NOT pushed |
```

**Rules that make it useful rather than decorative:**
- **Every Result is verifiable** — a hash, a count, a path, a branch. Never "done" or "✅" on its own.
- **A non-code plan MUST still show a Verification row** with the check and its outcome, exactly where a code plan shows test counts. An absent row means the work was never proven — that is a failure, not a formatting choice.
- **Report the non-actions too**: a refusal to run on `main`, a refusal for a missing test plan, a smoke-test stop, a deferred unit — each gets a row.
- **This is the only summary** — no second table, no prose recap of the same facts.

Then print the hand-off line:

> **Next:** run the **post-impl skill** (branch-aware — on a feature branch it keeps the plan in `docs/plans/` + opens/refreshes the stacked PR; on `main` it moves the plan to `artifacts/` + pushes trunk) to update docs, handle the plan file, add the `docs:` commit, and push / open the PR.

---

### Phase 8 — Friction log (self-improvement)

Look back over **this session** and ask whether anything about the **impl skill itself** should change. Append to the friction block at the tail of the plan file (pre-impl created it; if the plan predates that, create it):

```markdown
<!-- FRICTION:START -->
## Skill Friction Log

> Friction is **marked, never deleted**: every entry ends in a `**Status:**` line — `Open` (the default),
> `Resolved <date> — <what/where>`, or `Declined <date> — <reason>`. Open entries are the backlog for
> improving these skills. **Empty is a valid state — do not pad it.**

### F-2 — impl · Phase 2 (test-first loop)
**What happened:** <Enough narrative that a fresh context window — one that never saw this session —
can reason about a fix: what the skill told you to do, what you actually did, where it misled you or
wasted effort, and what that cost. Name the phase, the file, the command.>
**Recommendation:** <Only when there is a clear one. Omit the line entirely otherwise.>
**Status:** Open
<!-- FRICTION:END -->
```

**Continue** the `F-N` numbering from whatever pre-impl left — do not restart at F-1. Replace a `_No friction logged._` placeholder if you are the first to add an entry.

**Every entry MUST end with a `**Status:**` line** — `Open` when you log it. If you *resolve* a friction point during this same run, flip that line to `**Status:** Resolved <date> — <what/where>` (or `Declined <date> — <reason>`) — **not** an ad-hoc heading suffix or an `*(applied)*` note. Open work is found by grepping `Status: Open`.

**The bar.** Log friction that is **recurring or structural** — something an edit to the skill would actually prevent next time. Do **not** log: one-off environment hiccups (a flaky network call, a locked `.dll`, an expired token); trivia (a typo, a stale line number); anything you already fixed in-session; or feedback about the *codebase* rather than the *skill*. **Writing nothing is the expected outcome of a clean run** — an empty log is a signal, not a failure.

**Ask before you write.** Draft the candidate entries, show them to the developer, let them confirm/edit/add/drop, then write the confirmed set and commit it (with, or immediately after, the Phase 6 commit — never leave it uncommitted).
