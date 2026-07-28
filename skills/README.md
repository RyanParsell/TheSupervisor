# `skills/` — the tracked source of truth for TheSupervisor agent skills

This folder holds the repo's agent **skills** (each a directory with a `SKILL.md`). It is the
**source of truth**, deliberately **not** read directly by any harness. Deploy skills to your
local harness with the CLI:

```bash
supervisor skills install     # interactive; defaults: --profile user, --harness claude (multi-select prompt)
supervisor skills status      # show what's deployed where
```

`SKILL.md` is a [cross-harness open standard](https://agentskills.io) read identically by Claude
Code, GitHub Copilot, and OpenAI Codex, so **deployment is a copy, not a conversion.**

## Two personas

Everyone using TheSupervisor is an engineer, so the split isn't "user vs developer" in general —
it's whether you *are supervised by* TheSupervisor or *build TheSupervisor itself*.
`supervisor skills install` asks which you are (the `--profile` value is in parentheses):

| Persona (`--profile`) | Gets | Scope | Why |
|-----------------------|------|-------|-----|
| **Supervised agent** (`user`) | the usage skill: `supervisor` | **global** (`~/.claude/skills`, `~/.agents/skills`, `~/.copilot/skills`) | every Agent, in every repo, on every machine, needs the fleet verbs |
| **TheSupervisor developer** (`developer`) | all skills | usage skill global + everything else **project-level** (this repo's `.claude/skills` / `.github/skills` / `.agents/skills`) | the dev skills (`pre-impl`, `impl`, `post-impl`, `tdd`, the grills) only matter inside this repo |

The `user` profile is deliberately global and deliberately thin. Enrollment is **involuntary**
(ADR-0002) — an Agent joins the Roster through the MCP transport and hooks whether or not it ever
loads a skill. The `supervisor` skill exists only to teach the model the verbs it *already has*, and
one thing it cannot infer: that a Command to a Foreign Agent is queued rather than immediate.

`supervisor` is a **bootstrap stub** — keep it content-free. Instruction content lives in the CLI's
agent-guide command and versions with the binary, so a stale deployed skill can never contradict the
tool.

Harness → directory map (chosen via `--harness` — one or more, repeatable or comma-separated like `--harness claude,codex`, or `all` for every harness; multi-select prompt when omitted):

| Harness | Global | Project |
|---------|--------|---------|
| Claude Code | `~/.claude/skills` | `.claude/skills` |
| OpenAI Codex | `~/.agents/skills` | `.agents/skills` |
| GitHub Copilot | `~/.copilot/skills` | `.github/skills` |

The deploy-target dirs (`.claude/skills`, `.github/skills`, `.agents/skills`) are **git-ignored** —
they are reproducible install output, never edited by hand. Edit skills here in `skills/`, then
re-run `supervisor skills install`.

## `External/`

`External/` holds **vendored** skills (`grill-me`, `grill-with-docs`) pulled from another repo. The
nesting is a deliberate "do not hand-edit these" signal. On install they are **flattened** to top
level (they land beside the other skills, not under an `External/` subdir).

## The workflow trio and TDD

`pre-impl` → `impl` → `post-impl` are the front, middle, and back bookends of every change:

- **pre-impl** grounds itself in `docs/sdd/`, grills the change into a phased plan on a correctly
  named branch, and **writes the test plan**. It will not finish without one.
- **impl** executes that plan work unit by work unit, **test-first** — the test plan is the gate, and
  each unit goes red → green → refactor. UI work additionally stops for a developer smoke per MSU.
- **post-impl** reconciles the SDD documents against what actually shipped, archives the plan into
  `docs/artifacts/` with its tags, and records friction.

`tdd` is the technique those two lean on; it is a pure-technique skill with no project coupling.
TheSupervisor is developed test-first because it is multi-process and multi-machine by construction
— Agents, Hub, Peers, pseudoterminals — so its real defects are integration defects, and the seams
that make those testable have to exist before the code does, not after.

## Authoring for cross-harness (the portability convention)

Skills load in all three harnesses, but *content* can accidentally assume one. When writing or
editing a skill, keep it harness-neutral:

1. **Harness-neutral spine.** State the workflow, its outcomes, and concrete commands
   (`git`, `dotnet`, `npm`) so any agent can follow them. This path must be complete on its own.
2. **Isolate harness-specific accelerations.** Claude Code niceties (parallel worktree sub-agents,
   background tasks) go in a clearly-labeled *optional* section layered on top of the neutral spine —
   never load-bearing.
3. **Never hard-code slash invocation.** Refer to other skills by **name** ("the post-impl skill"),
   not `/post-impl`. Invocation differs by harness (Claude `/name`, Codex `$name` or `/skills`,
   Copilot activates by the skill's `description`).
4. **Invest in the `description` frontmatter.** It is the one universal activation surface across all
   three harnesses — make it say plainly what the skill does and when to use it.
5. **Keep frontmatter valid across harnesses.** Every manifest must declare a `name` that exactly
   matches its directory and a `description` of 1–1024 characters. Plain YAML descriptions cannot
   contain `: ` or ` #`; quote/fold the value or use different punctuation.

Skills that are already fully portable (route to the CLI or are pure technique): `supervisor`,
`grill-me`, `grill-with-docs`, `tdd`. The workflow skills `pre-impl`, `impl`, and `post-impl` follow
the convention above (neutral spine + optional Claude accelerations).
