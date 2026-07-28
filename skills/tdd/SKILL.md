---
name: tdd
description: Test-driven development for TheSupervisor — vertical red-green-refactor slices with the project's fakes, seams, and conventions. Use when building or fixing a CLI command, Hub capability, or control-plane feature, or when the user mentions "red-green-refactor", test-first, or integration tests.
user_invocable: true
version: 1.0.0
# ⚠️ IMPORTANT: When editing this file, increment the patch version above (e.g., 1.0.0 → 1.0.1).
# Derived from the WExpert tdd skill (C:\Code\MS\CLI\skills\tdd), retargeted to TheSupervisor.
---

## Test-Driven Development: $ARGUMENTS

This is the single TDD skill for this repo: the general red-green-refactor *philosophy* plus the TheSupervisor-specific *mechanics* (fakes, seams, conventions). Use it for any .NET work. (React/WebUI MSUs use vitest test-first, not this skill.)

## Philosophy

**Core principle**: Tests should verify behavior through public interfaces, not implementation details. Code can change entirely; tests shouldn't.

**Good tests** are integration-style: they exercise real code paths through public APIs. They describe _what_ the system does, not _how_ it does it. A good test reads like a specification - "user can checkout with valid cart" tells you exactly what capability exists. These tests survive refactors because they don't care about internal structure.

**Bad tests** are coupled to implementation. They mock internal collaborators, test private methods, or verify through external means (like querying a database directly instead of using the interface). The warning sign: your test breaks when you refactor, but behavior hasn't changed. If you rename an internal function and tests fail, those tests were testing implementation, not behavior.

See [tests.md](tests.md) for examples and [mocking.md](mocking.md) for mocking guidelines.

## Anti-Pattern: Horizontal Slices

**DO NOT write all tests first, then all implementation.** This is "horizontal slicing" - treating RED as "write all tests" and GREEN as "write all code."

This produces **crap tests**:

- Tests written in bulk test _imagined_ behavior, not _actual_ behavior
- You end up testing the _shape_ of things (data structures, function signatures) rather than user-facing behavior
- Tests become insensitive to real changes - they pass when behavior breaks, fail when behavior is fine
- You outrun your headlights, committing to test structure before understanding the implementation

**Correct approach**: Vertical slices via tracer bullets. One test → one implementation → repeat. Each test responds to what you learned from the previous cycle. Because you just wrote the code, you know exactly what behavior matters and how to verify it.

```
WRONG (horizontal):
  RED:   test1, test2, test3, test4, test5
  GREEN: impl1, impl2, impl3, impl4, impl5

RIGHT (vertical):
  RED→GREEN: test1→impl1
  RED→GREEN: test2→impl2
  RED→GREEN: test3→impl3
  ...
```

> **The plan's test plan is not a horizontal slice.** pre-impl names the tests up front so the *seams* get designed before the code; it does not mean writing them all at once. Take them one at a time, in the order the plan lists, red→green→refactor each.

## Coverage stance

- **Lean and prioritized.** You can't test everything. Test the critical paths and the behavior that actually matters for this feature — not a fixed checklist of every conceivable case.
- **Always-on rules for this system**, because these are the failures that are invisible until they bite:
  - **Tier gating.** Any action that differs between an **Owned** and a **Foreign Agent** gets a test proving it is *refused* on the wrong tier, with the documented error. A test that only proves the happy tier lets the gate rot.
  - **Scope isolation.** Where one Agent, UI client, or Peer could reach another's state, assert it cannot — and assert the **forbidden-vs-not-found distinction** rather than conflating them. Leaking existence is a real bug that a coarse "it failed" assertion hides.
  - **Teardown order.** Where capabilities are revoked and processes killed, assert the *order* — revoke before kill. A surviving descendant holding a live token is the failure this prevents.
- **Known-issue guards — apply when the code path is relevant** (not as a blanket minimum):
  - **Clock-driven behavior:** Command TTL, reconnect grace, host idle timeout, and summary staleness are all clock-driven. Drive them through the **injected clock**, never by waiting. A test that sleeps will be deleted or skipped within a month.
  - **Fail-open paths:** the shim and hooks exit 0 on failure by design. Assert that they exit 0 *and* that the failure was recorded somewhere observable — otherwise "silent" and "broken" are indistinguishable in the test too.
  - **Non-interactive mode:** a missing required arg must return the documented error code, not hang.
  - **Console redirect:** if the code does `Console.Is*Redirected` checks, cover both states.

## Output Style

- Brief phase indicators between tool calls: "Planning…", "Slice 2: happy path…", "Grinding slice 3 (cycle 2)…".
- Do NOT narrate what each file read/edit does, and don't add filler ("Let me now…", "Good, that worked…").

---

### Phase 1 — Planning & Discovery (read-only)

**Goal:** understand the feature and absorb existing patterns before writing code. Vertical work still starts from shared understanding of *which behaviors matter* — not a full test suite.

**1a. Parse `$ARGUMENTS`.** Identify: the component (Hub, enrollment, Roster, control/Commands, terminal, federation, Workstream, CLI surface); whether this is a new command, a new Hub capability, or a modification; and which `arch:` slug it belongs to.

**1b. Read reference tests** to absorb conventions (read at least TWO from the same area). Conventions you MUST follow exactly:
- `[Fact] public async Task` methods; commands invoked via `await cmd.ExecuteAsync(null!, settings, CancellationToken.None)`
- private `CreateCommand()` / `CreateSut()` factory (constructor arg order must match the real type)
- xUnit `Assert.*` only (no fluent assertions)
- raw string literals (`"""…"""`) for test JSON payloads

**1c. Read the fake infrastructure** in `Supervisor.Tests/Fakes/` before writing a new fake. The seams this system is built around — each exists so a real dependency stays out of the test:

| Fake | Stands in for | Because |
|---|---|---|
| `FakeAgent` | An enrolled Claude session | A real one needs auth and burns tokens |
| `FakePeer` | Another machine's Hub | Federation must be testable on one box |
| `FakeTerminalProcess` | A ConPTY child | PTYs are slow, platform-bound, and leak |
| `FakeClock` | Wall time | TTL/grace/idle tests must not sleep |
| Transcript fixtures | A live session's JSONL | Activity Summary derivation needs stable input |

**If the behavior you're building needs a seam that doesn't exist, build the seam first, as its own slice.** That is the correct order, not overhead — and pre-impl's test plan should already have named it.

**1d. For a NEW command**, also read an existing command in the same branch and its Settings class (inherits the global settings base, redeclares `Json` if it is a branch leaf).
**1e. For a MODIFICATION**, read the target file, its existing test file, and its Settings class.

**1f. Agree the behavior list (the plan gate — skippable).** Following the general planning checklist ([deep-modules.md](deep-modules.md), [interface-design.md](interface-design.md)):
- [ ] Confirm the public interface / settings shape.
- [ ] List the **behaviors** to test in priority order (behaviors, not implementation steps).
- [ ] Note which always-on rules and known-issue guards are relevant.

Present that list and get a quick nod. **Skip the gate when the shape is obvious** — don't turn it into ceremony. If a plan's test plan already lists the behaviors, that *is* the list; confirm it still holds and move on.

---

### Phase 2 — Tracer bullet, then the incremental loop (vertical)

Work **one behavior at a time**. Never write the next test until the current one is green.

**2a. Tracer bullet.** Write ONE test for the first, most fundamental behavior (usually the happy path) and drive it to green — this proves the path end-to-end (test file compiles, DI wired, seam reached, output formatted).

**2b. Incremental loop.** For each remaining behavior from the plan list:
```
RED:   write the next single test → it fails
GREEN: minimal code to pass → it passes
```
Rules:
- One test at a time. Only enough code to pass the current test. Don't anticipate future tests.
- **Watch RED fail for the right reason.** A test that errors on a missing type has proven nothing about behavior yet. A test that passes before the code exists is testing nothing — find out why before continuing.
- Keep tests on observable behavior (see [tests.md](tests.md)).

**2c. Autonomous execution *within* a slice.** Driving a single test red→green is hands-off — don't stop for input mid-slice. Grind up to ~10 build-test-fix cycles on that one behavior, then break to the human only if stuck:

```
per behavior:
  loop (max ~10 cycles):
    BUILD:  dotnet build TheSupervisor.sln --no-restore --verbosity quiet
            build fails → fix SOURCE (not the test), next cycle
    TEST:   dotnet test TheSupervisor.sln --verbosity normal --filter "FullyQualifiedName~{TestClassName}"
            pass → behavior done, go to next behavior
            fail → read expected-vs-actual, fix IMPLEMENTATION (not the test), next cycle
    stuck on the same failure 3 cycles, or hit the cap → surface to the human
```

Common failures → fixes: "Object reference not set" → missing null check / uninitialized fake; unexpected missing-argument error → settings not set in test; wrong exit/error code → check exception handling and which exception type is thrown; a test that hangs → you are waiting on real time instead of the injected clock.

> **A locked `Supervisor.Web.dll` means a Hub is running.** `supervisor hub stop`, then rebuild. Do not work around it with `--no-build` — that runs the previous binaries and reports green over a failed build.

**Never modify a test to make it pass** — tests are the spec. The only exception is a genuine test defect (typo, a constant that contradicts the feature description). **Never refactor while RED** — get to green first.

---

### Phase 3 — Refactor (only when GREEN)

After the behaviors are green, look for [refactor candidates](refactoring.md):
- [ ] Extract duplication
- [ ] Deepen modules (move complexity behind simple interfaces)
- [ ] Apply SOLID where natural
- [ ] Consider what the new code reveals about existing code
- [ ] Run tests after each refactor step

**Per-cycle checklist:** test describes behavior not implementation · uses the public interface only · would survive an internal refactor · code is minimal for this test · no speculative features.

**Check the names against `CONTEXT.md`.** Refactoring is when a type quietly acquires the wrong noun. If you have written `RemoteAgent` where the domain says **Foreign Agent** — or worse, where **remote** already means *on another Machine* — rename it now, while it is cheap.

---

### Phase 4 — Summary & follow-ups

Report:

```markdown
## TDD Summary

**Feature**: {from $ARGUMENTS}
**Tests**: {count} in `{test file}` — {one line each}
**Seams built**: {new fakes/fixtures, or: none needed}
**Files created/modified**: {path (new|modified)}
**Result**: {ALL PASS | N still failing}
{if failing} **Unresolved**: {test — suspected root cause}
**Guards applied**: {which always-on rules and known-issue guards were relevant}
```

**Confirm no regressions** — run the full suite once (unfiltered): `dotnet test TheSupervisor.sln --verbosity quiet`. Report any pre-existing test that broke.

**CLI-change checklist.** If a command/subcommand/arg/flag was added, removed, renamed, or changed, remind the user to update: `Supervisor/Program.cs`, the relevant `*Settings.cs`, `README.md`, `Supervisor/Commands/AgentGuideCommand.cs`, and `CLAUDE.md`. The post-impl skill re-checks these, but catching it here is cheaper.
