---
name: post-impl
description: Publish completed TheSupervisor work after implementation. Reconciles the plan's test plan against what was actually tested, updates the required SDD docs, help, and agent guide, archives the plan under docs/artifacts, creates separate implementation and documentation commits, then pushes or opens the correct PR for the current branch. Feature and bug branches use stacked PR behavior, while a declared hotbug may publish directly from main. Use after the impl skill or when asked to finish, document, push, release, or open a PR for completed TheSupervisor work.
user_invocable: true
version: 1.0.0
# ⚠️ IMPORTANT: When editing this file, increment the patch version above (e.g., 1.0.0 → 1.0.1).
# Derived from the WExpert skill trio (C:\Code\MS\CLI\skills), retargeted to TheSupervisor and
# hardened around test-first development.
---

## Post-Implementation Processing

> **Invocation across harnesses.** This skill activates by name/description in any harness — in Claude Code as a `/`-command, in Codex via `$`-prefix or the `/skills` menu, and in GitHub Copilot by matching the `description` above. Any arguments described below are read from the invocation (e.g. release type, `--pr`); if your harness passes them, use them, otherwise infer intent from the request and the current branch. References to other skills below use their **names** (the impl skill, the pre-impl skill) — invoke them with your harness's own mechanism.

**One branch-aware skill for both trunk and stacked-feature-branch flows.** Behavior keys off the current branch:

- **On `main`/`master` (trunk):** push to `main` (or open a PR with `--pr`), release-tag eligible.
- **On any feature branch (stacked):** push the current branch, open/refresh the **stacked** PR (base = the prior phase's branch). Never touches `main`; never release-tags.

**Universal in both modes:** **reconcile the test plan**; **move the plan from `docs/plans/` to `docs/artifacts/`**; separate `feat:`/`fix:` (code) and `docs:` (docs) commits — never bury implementation under a `docs:` subject; rebuild committed web assets (`wwwroot`) before staging; sync skill mirrors; and "verify, don't manufacture" doc edits.

### Output Style
- Brief phase indicators ("Phase 1: SDD docs…"). Do NOT narrate each file edit. End with a summary table.

---

### Argument grammar

```
post-impl [<release-type> [<version>]] [--pr | --push-main] [--branch=<name>] [--base=<branch>]
```

- `<release-type>` — `noteworthy` | `silent` (**main only**). Omitted = no tag.
- `<version>` — semver following `<release-type>`; omitted = auto-derive (Phase 8).
- `--pr` — force PR mode (even from `main`).
- `--push-main` — force direct push to `main` (escape hatch; valid **only** when on `main`).
- `--branch=<name>` — override the auto-derived head branch name (main→PR migration only).
- `--base=<branch>` — override the stacked-PR base (feature-branch PR).

**Branch-aware default** (no `--pr`/`--push-main`): on `main` → push directly to `main`; on any other branch → PR mode targeting the current branch.

**Refusal rules** (validate immediately, STOP if violated):
- `--pr` + `noteworthy` → REFUSE: "Stable releases must be tagged on main. Merge the PR first, then re-run this skill with `noteworthy` from main."
- `--pr` + `--push-main` → REFUSE (mutually exclusive).
- `--push-main` while **not** on `main`/`master` → REFUSE: "`--push-main` requires being on main."
- `<release-type>` while on a **feature branch** → REFUSE: "Release tags are main-only — merge the stack to main first, then re-run this skill with `<release-type>` from main."
- `noteworthy <version>` containing `-` → REFUSE; `silent <version>` **not** containing `-` → REFUSE (the release workflow reads hyphenated tags as prereleases).

---

### Phase 0 — Branch + commit-state check (sets the mode)

```
git branch --show-current      # commits/pushes target THIS
git status --short ; git diff --stat
```

- **Mode** = **trunk** if on `main`/`master`, else **feature**. (This drives plan handling, push target, PR base, and release eligibility.)
- **Trunk mode + a `**Hotbug:** YES` plan** → this is the sanctioned trunk-direct path: push straight to `main`, **no PR**, `fix:` commits. Announce it (`⚠ HOTBUG — pushing direct to trunk, no PR`) so nobody mistakes it for the normal flow.
- **Trunk mode + a plan that is NOT a hotbug** → the work skipped the branch discipline (the impl skill refuses to implement on `main` for exactly this reason). **Warn the developer and offer `--pr`** — which migrates the commits to a branch and opens a PR (Phase 6a) — before pushing to trunk. Proceed with the direct push only if they confirm.
- Note whether the **implementation is already committed** (the impl skill makes a local `feat:` commit; UI plans commit per-MSU) or is still uncommitted in the working tree — decides the Phase 6 commit shape.
- Identify **unrelated** modified/untracked files (other WIP, scratch) to exclude from staging.
- **Self-editing check:** if this plan edited `skills/{pre-impl,impl,post-impl}`, the tracked `skills/…` source is **authoritative for this run** — the deployed copy you are executing from may predate the change. Follow the tracked version wherever the two disagree.

---

### Phase 1 — Reconcile the test plan (do this FIRST)

The plan carries a **Test plan** (pre-impl Phase 7) naming, per work unit, the tests that would exist and at what level. **Before documenting anything, check reality against it** — this is the one moment where a quietly-skipped test is still cheap to catch.

For each named test: does it exist, and does it run? Then classify the delta honestly:

- **Delivered as planned** — nothing to say beyond the count.
- **Delivered differently** — the test exists under another name or at another level because the design moved. Fine, and worth a sentence in the story: the *plan* was wrong, not the work.
- **Not delivered** — say so plainly, in the summary table, the story, and the PR body. Either it was genuinely unnecessary (say why) or it is **outstanding work**, in which case it belongs in the plan's friction log or a follow-up plan. Never let it evaporate silently.
- **Manual smoke that never ran** — the plan named developer smokes deliberately excluded from CI (real Claude, real PTY, a second machine). If one was named and not performed, it is **unverified**, not "verified by inspection." Report it as such.

> **A reviewer must never be able to read "tests green" and reasonably infer that everything the plan promised was tested.** That inference is the failure mode this phase exists to prevent.

---

### Phase 2 — Update SDD Documents

Read each file first to find the insertion point.

- **2a. Architecture** (`docs/sdd/thesupervisor-architecture.md`) — update components, design decisions, data flow, and conventions when behavior changed; add new files/projects to the structure listing; keep the Grounding Index routing accurate. If the change added an `arch:` slug's worth of new component, the vocabulary table in `docs/artifacts/README.md` needs the slug too (Phase 3).
- **2b. Stories** (`docs/sdd/thesupervisor-stories.md`) — the doc opens with an **`## Index`** (Area | Stories | Summary) over thematic **`## Area:`** sections. Adding a story is three edits:
  1. **Mint the ID** — story IDs are **self-minting UTC timestamps**, `S-YYMMDD.HHMMSSx`: run `date -u +'S-%y%m%d.%H%M%S'` **once** for this run, then append a per-story letter (`a`, `b`, `c`, …). Always start at `a`, even for a single story; the letter enumerates the stories added in *this* run. Acceptance criteria derive as `AC-<id>.<n>`. There is **no shared counter** — nothing to bump, nothing to cross-check.
  2. **File it** — append the story under the **`## Area:` section that owns it**, in **chronological / authoring order**, with ACs all `[x]`; the final AC carries the **full-suite** test count. Do not append to the end of the file. If the work genuinely fits no existing area, add a new area (section + index row) rather than forcing it — and add the matching `area:` slug to the vocabulary table.
  3. **Update the index** — extend that area's `Stories` cell with the new ID, and refresh the area's `Summary` if its scope actually widened.

  **The ACs must reflect Phase 1.** If a planned test wasn't delivered, the story does not get an AC claiming it was.
- **2c. PRD** (`docs/sdd/thesupervisor-prd.md`) — update the relevant FR/NFR and keep its Grounding Index mapping accurate. A capability *extension* of an existing FR (another flag, output mode, surface) updates that bullet rather than inventing a new FR.
- **2d. CONTEXT.md** — if the work introduced, renamed, or sharpened a **domain term**, update the glossary, the relationships, and (if a real ambiguity was resolved) the flagged-ambiguities list. Terms are load-bearing here; a new noun that never reaches `CONTEXT.md` will be called three different things within a month.
- **2e. `docs/adr/`** — if the work made a decision that is hard to reverse, surprising without context, and chosen against real alternatives, add an ADR. If it **contradicted** an existing ADR, do not silently diverge: amend that ADR with a dated note (as ADR-0003 carries its "challenged and upheld" and scope-clarification notes) or supersede it.

---

### Phase 3 — Plan handling (always move)

**MOVE** `docs/plans/YYYY-MM-DD-<topic>.md` → `docs/artifacts/<impl-date>-<topic>.md` (re-date the prefix to the implementation date; add one if absent), then **insert a top row** in `docs/artifacts/README.md` (table is newest-first): `| <impl-date> | [<slug>](<file>) | <tags> | 2–3 sentence outcome summary. |`. Use filesystem move if untracked, `git mv` if tracked.

**`design.md` is never moved.** It is not a plan; it is the founding design record. If a plan contradicted one of its `D<n>` decisions, that is an ADR (Phase 2e), not an edit to `design.md`.

**Reconcile the tags first.** The plan's `**Tags:**` header (stamped by pre-impl from its grounding) may predate scope drift — before writing the index row, adjust it to match **what actually shipped** (add the area/component the work grew into, drop what fell out), then copy the reconciled tags into the row's Tags cell as backticked slugs. Tags draw from the vocabulary block at the top of `docs/artifacts/README.md` (`prd:FR-N` / `arch:<slug>` / `area:<slug>`; ≥1 `area:`, ≤6 total); if the work genuinely needs a new `arch:` slug, add it to the vocabulary table **deliberately in the same run** — never write an undeclared slug.

**`<impl-date>` is the LOCAL author-date of the implementation commits** — `git log -1 --format=%cd --date=format:%Y-%m-%d` on the branch head — **never "today" and never a UTC stamp**. A session that straddles local/UTC midnight will otherwise stamp a date that disagrees with every commit the plan ships. One authoritative source, and it's the commits. (If the implementation is still uncommitted in the working tree, the local calendar date is what the commit will get — use that.) Use the same `<impl-date>` everywhere this run writes a date: the moved filename, the README row, and any story text.

**Only the date prefix changes.** A **bug** plan keeps its `bug` segment: `docs/plans/YYYY-MM-DD-bug-<slug>.md` → `docs/artifacts/<impl-date>-bug-<slug>.md`. Never drop it — the segment is how a bug fix is told from a feature at a glance, forever.

**Carry the friction log through verbatim.** If the plan ends in a `<!-- FRICTION:START -->` … `<!-- FRICTION:END -->` block, it moves with the plan **unedited** — do not strip, summarize, or "tidy" it. That block is much of the point of archiving the plan; its `Open` entries are the backlog for improving these skills. Each entry ends in a `**Status:**` line (`Open` / `Resolved <date> — …` / `Declined <date> — …`) — friction is marked, never deleted.

This happens in **both** trunk and feature modes. On a stacked feature branch this archives the shared plan while later phases may still be open — that is intended; later phases reference the archived plan in `docs/artifacts/`.

---

### Phase 4 — Update Agent Instructions and Help

- **4a. Embedded agent guide** (`Supervisor/Commands/AgentGuideCommand.cs`) — this generates `supervisor agent-guide --json`, which is what the content-free `supervisor` skill routes to, so it is the **primary interface between this repo and every supervised Agent**. Sync the categories it tracks: **trigger terms, workflows, error codes, best practices.** If the feature introduced none, the file needs **no change** — say so.
- **4b. Program.cs help** — only if a command/subcommand/flag changed: verify `.WithDescription()`/`.WithExample()` and the known-subcommands dict.
- **4c. README.md** (root) — **always check it when the change is user-facing.** Update any section the change touches: command/usage listing and examples (new or changed command, subcommand, flag, or output mode); install / setup / skill-install instructions; and the project-structure section if new directories were added. Do not skip the README just because the SDD docs were updated — it is the human entry point and drifts easily. Genuinely internal changes → "No change needed".
- **4d. CLAUDE.md** — command-branch listing only if a new branch/notable subcommand changed.
- **4e. Skill mirrors** — if this run changed any skill under `skills/`, **redeploy them** (`supervisor skills install --profile developer --yes`) so the harness copies match the tracked source (the deployed copies are untracked and are not committed).

> Many changes touch **no CLI surface** (internal Hub behavior, renderer changes). Then 4a–4d are "No change needed" — **verify, don't manufacture edits.**

---

### Phase 5 — Audit for Stale Documentation

Grep the codebase for each check below:
1. Every command in `Program.cs` appears in the architecture command tree.
2. Every agent-guide command has matching examples.
3. `AgentGuideCommand.cs` error codes include any new codes.
4. Any contract enumeration (agent guide, skill, PRD, error-code table) is **complete** — grep the old enumeration string and fix every stale copy.
5. **Domain-term drift.** Grep the diff for the banned aliases in `CONTEXT.md` (`session` for **Agent**, `supervisor` for **Hub**, `remote agent` for **Foreign Agent**, `repo path` for **Repository**). New code and new docs are where drift enters.
6. **Stale-plan sweep.** List `docs/plans/*.md` (excluding `README.md` and `design.md`) and check each against `docs/sdd/thesupervisor-stories.md`: any plan whose story is already delivered **shipped but was never archived**. That is a real hazard — `docs/plans/` is the impl skill's backlog, so a shipped plan left there invites re-implementation. Report each one and offer to move it (it is *not* automatic — a plan may legitimately be mid-flight across phases; ask).

The plan should have moved to `docs/artifacts/` (Phase 3) in every mode — flag it if it's still in `docs/plans/`. Fix gaps found.

> **Optional (Claude Code):** if the audit surface is large, dispatch a read-only Explore sub-agent to run these greps in parallel and report gaps. This is a speed-up only — running the greps inline is a complete, first-class way to do this phase.

---

### Phase 6 — Build Verification

Confirm doc-adjacent code edits didn't break anything.

**Run this ONCE, and only for the layers this run actually touched.** The impl skill already left a green build + full suite behind, so re-running a layer post-impl did not change is pure cost. Decide per layer from the edits **this run** made:

| Touched this run | Run |
|---|---|
| `AgentGuideCommand.cs` (4a), `Program.cs` (4b), or any other `.cs` | `dotnet build TheSupervisor.slnx --verbosity quiet --nologo` then `dotnet test TheSupervisor.slnx --no-build --verbosity quiet` |
| Markdown / docs only — no `.cs` edited | **Skip the .NET suite** — with ONE exception: this skill always edits `docs/artifacts/README.md` and the plan file, so **always run the tags meta-test** if it exists (`dotnet test TheSupervisor.slnx --no-build --filter ArtifactIndexTags`). Say the skip out loud in the Phase 9 table rather than omitting the row. |
| Any `WebUI/**` source | `npm --prefix WebUI run build && npm --prefix WebUI test` |

**Order matters: this phase comes AFTER Phases 1–5 on purpose.** Those phases are the ones that edit `.cs`, so building earlier would just force a second build.

**A green `--no-build` test run does NOT imply a green build.** `dotnet test --no-build` runs whatever binaries are already on disk, so it happily reports a full pass over a build that just failed. This bites when a `supervisor ui` Hub left running from a smoke test holds `Supervisor.Web.dll` and the build dies with `MSB3021` copy errors. **Check the build's own exit status / `0 Error(s)` line before trusting the test counts**, and `supervisor hub stop` before building.

**If the built web assets (`wwwroot`) are committed, rebuild them so the committed output matches source before staging** — the .NET build only *copies* `wwwroot`, so a stale bundle ships silently otherwise. Capture final pass counts for the story AC + summary. Fix any failure before proceeding.

**A verification that CANNOT execute must be named, not skipped over.** "Fix any failure" has no answer for a suite that cannot run at all in this environment — a Playwright spec authored but never executed, a two-machine federation test, a real-Claude smoke. Running the suites that *do* work does not satisfy this phase for the ones that don't: state explicitly **what** could not run, **why**, and **where it will first run**, and carry it as the `Unrun verification` row in the Phase 9 table so it reaches the story, the commit message, and the PR body.

---

### Phase 7 — Commit (separate impl + docs), branch-aware push

**Resolve the target ref:** trunk default / `--push-main` → `main`; feature default / `--pr` → the current (or `--branch`) branch.

**7a. main→PR migration** (only when on `main` and `--pr`):
- Clean local `main`, dirty WT only → `git switch -c <target>` (derive `<target>` = `<prefix>/<slug>` from the invocation arguments or the moved plan slug; **a bug plan — one whose file carries the `-bug-` segment or declares `**Type:** Bug fix` — derives `bug/<slug>`**; otherwise prefix by diff: docs-only→`docs`, net-additions→`feat`, else `chore`).
- Local commits ahead of `origin/main` → print `git log --oneline origin/main..main`, **confirm with the user**, then `git switch -c <target>; git switch main; git reset --keep origin/main; git switch <target>`. If `reset --keep` would discard uncommitted work, surface the error and stop.
- On a feature branch already: the current branch **is** the target — skip 7a.

**7b. Stage** with explicit paths; exclude the unrelated files from Phase 0.

**7c. Commit:**

> **Which type?** `fix:` when the plan is a **bug** plan (`-bug-` segment / `**Type:** Bug fix`); `feat:` otherwise — **feature is the default**. Same rule the impl skill used, so the two commits on a branch never disagree.

- **Impl still uncommitted → TWO commits.** First the code (`<feat|fix>(<scope>): <feature> — <what>`, **never `docs:`**), then the docs:
  ```
  docs: post-impl updates for <feature>

  Updates architecture, PRD, stories (S-…), agent guide. Plan moved to artifacts/. <N> total tests, 0 failures.
  ```
- **Impl already committed** (via the impl skill or per-MSU) → make **only** the `docs:` commit.
- Both end with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

**7d. Push:** main → `git push`; new feature branch → `git push -u origin <target>`; existing tracked branch → `git push`. Non-fast-forward → STOP ("branch diverged — pull/rebase first"); never `--force`.

---

### Phase 8 — Open or refresh the PR

Skip only when target = `main` **and** direct-push (no `--pr`).

**Detect existing:** `gh pr list --head <target> --state open --json number,url,baseRefName`. If one exists → capture it, refresh the body if scope changed, skip create.

**Resolve the base:**
- **Trunk `--pr`** → base `main`.
- **Feature (stacked)** → base = the **prior phase's branch** (the branch this was cut from). Inspect the stack (`gh pr list --state open --json number,title,headRefName,baseRefName`); if the prior phase already merged, base = `main`; `--base` overrides; if genuinely ambiguous, **ask the user**. Confirm the base exists on origin.

**Create:** `gh pr create --base <base> --head <target> --title "<type>(<scope>): <feature>[ (Phase N)]" --body "@<body-file>"` — `<type>` follows the **same bug/feature rule as 7c** — body = the Phase 9 summary + `git log --oneline <base>..<target>` + (stacked) the bottom-up merge order. **Include the Phase 1 test-plan reconciliation and any `Unrun verification` row** — a reviewer reads the PR body, not the plan. End the body with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. If `gh pr create` fails, surface it verbatim (the branch is already pushed).

---

### Phase 9 — Release tag (main only; when `<release-type>` given)

Skip if no `<release-type>` (or if not on `main` — already refused in the grammar).

1. Working tree clean (`git status --porcelain` empty).
2. Resolve the tag: `noteworthy <version>` → verbatim; `noteworthy` (none) → patch-bump the latest stable `git tag --list 'v*' --sort=-v:refname | head -1` (ask if the bump dimension is ambiguous); `silent <version>` → verbatim; `silent` (none) → `v<base>-alpha.<N>` (N = max existing alpha at this base + 1); cold start → ask the user.
3. Validate shape: `noteworthy` MUST NOT contain `-`; `silent` MUST; tag must not already exist locally or on origin.
4. Verify the tagged commit **exists on origin** (`git rev-parse HEAD` appears in `git ls-remote origin`); if not, the push hasn't landed — retry briefly, then fail.
5. `git tag -a <tag> -m "Release <tag>"` then `git push origin <tag>` (separate from the branch push).
6. Print the tag + the Actions URL (`https://github.com/RyanParsell/TheSupervisor/actions`); the release workflow runs automatically — do not poll.

**Hard rules:** do not edit the release workflow or `*.csproj` to effect a release; no manual `.nupkg`/`gh release create`; no `--force`/`git tag -f`; never push a tag before its commit is on origin; never tag from a dirty tree.

---

### Phase 10 — Actions taken (summary table)

Every skill in the trio ends with the **same table**, so three runs read alike. **One table — the document ledger and mode-specific footers are absorbed as rows, not printed alongside.**

```markdown
## Actions taken (<trunk | feature branch>)

| Action | Target | Result |
|--------|--------|--------|
| Test plan reconciled | <n> planned | <n> delivered, <n> changed level, <n> NOT delivered — <which> |
| Manual smokes | <named> | performed (or: ⚠ named but not performed — unverified) |
| Story added | docs/sdd/thesupervisor-stories.md | S-YYMMDD.HHMMSSx under Area: <area>; area Stories cell extended |
| Architecture | docs/sdd/thesupervisor-architecture.md | <what changed> (or: No change needed) |
| PRD | docs/sdd/thesupervisor-prd.md | FR-N extended (or: No change needed — <why>) |
| CONTEXT.md | — | <term added/sharpened> (or: No change needed — no new domain term) |
| ADR | docs/adr/ | ADR-000N added (or: No change needed) |
| Agent guide (cs) | Supervisor/Commands/AgentGuideCommand.cs | <what> (or: No change needed — no CLI surface) |
| README / CLAUDE.md / Program.cs | — | Updated (or: No change needed) |
| Plan moved | docs/artifacts/<date>[-bug]-<slug>.md | friction log carried through verbatim; tags reconciled → `area:…` `arch:…` |
| Stale-plan sweep | docs/plans/ | N shipped plans found (or: clean) |
| Build + tests | TheSupervisor.slnx | 412 ×2 TFMs, 0 failures |
| Unrun verification | <suite/spec> | could not execute — <why>; first runs: <where> (row REQUIRED whenever one exists) |
| Committed | <feat\|fix>: + docs: | <impl-hash> + <docs-hash> (or: docs only — impl already committed) |
| Pushed | origin/main | <hash> (or: ⚠ HOTBUG — direct to trunk, no PR) |
| PR | <url> | → base <base> (stacked: merge order …) |
| Release tag | <tag> | pushed; Actions: <url> |
```

**Rules that make it useful rather than decorative:**
- **Every Result is verifiable** — a hash, a story id, a URL, a count. Never "done" or "✅" on its own.
- **"No change needed" rows are REQUIRED, with the reason.** This is the load-bearing half of *verify, don't manufacture*: the table is how a reader tells "correctly skipped" from "forgotten," and dropping the row destroys that distinction.
- **The test-plan reconciliation row is never omitted**, and it never reads "all good" without counts.
- **Omit only rows for actions this mode cannot take** (no PR row on a direct trunk push; no tag row when no release type was given).
- **This is the only summary** — no second table, no prose recap of the same facts.

---

### Phase 11 — Friction log (self-improvement)

You are the **last** skill to touch the plan before it lives in `docs/artifacts/` for good — so this is the final chance to record what the trio got wrong. Look back over **this session** and ask whether anything about the **post-impl skill itself** should change.

Append to the friction block at the tail of the (now moved) plan in `docs/artifacts/`:

```markdown
### F-3 — post-impl · Phase 2b (SDD stories)
**What happened:** <Enough narrative that a fresh context window — one that never saw this session —
can reason about a fix: what the skill told you to do, what you actually did, where it misled you or
wasted effort, and what that cost. Name the phase, the file, the command.>
**Recommendation:** <Only when there is a clear one. Omit the line entirely otherwise.>
**Status:** Open
```

**Every entry MUST end with a `**Status:**` line** — `Open` when you log it, or, if you *resolved* the point during this run, `**Status:** Resolved <date> — <what/where>` (or `Declined <date> — <reason>`) — **not** an ad-hoc heading suffix or an `*(applied)*` note. Open work is found by grepping `Status: Open`; friction is marked, never deleted.

**Continue** the `F-N` numbering from whatever pre-impl and impl left — do not restart at F-1. Create the `<!-- FRICTION:START/END -->` block if the plan predates it; replace a `_No friction logged._` placeholder if you are the first to add an entry.

**The bar.** Log friction that is **recurring or structural** — something an edit to the skill would actually prevent next time. Do **not** log: one-off environment hiccups (a flaky network call, a locked `.dll`, an expired token); trivia (a typo, a stale line number); anything you already fixed in-session; or feedback about the *codebase* rather than the *skill*. **Writing nothing is the expected outcome of a clean run** — an empty log is a signal, not a failure.

**Ask before you write.** Draft the candidate entries, show them to the developer, let them confirm/edit/add/drop, then write the confirmed set. Include it in the `docs:` commit (Phase 7) — if that commit has already been made and pushed, make a follow-up `docs:` commit and push it; the log must not be left uncommitted.
