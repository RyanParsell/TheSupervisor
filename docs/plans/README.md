Markdown files in this folder are plans that are currently or will soon be implemented. The preceding date is the date the plan was originally documented.

## Naming

| | Bug fix | Feature (default) |
|---|---|---|
| Plan | `YYYY-MM-DD-bug-<slug>.md` | `YYYY-MM-DD-<slug>.md` |
| Branch | `bug/<slug>` | `feature/<slug>` |
| Commits | `fix(<scope>): …` | `feat(<scope>): …` |

A change is a **bug fix** only if its entire deliverable is restoring intended behavior. If it ships anything with utility beyond closing the defect — a new flag, a new surface, a reusable capability — it is a **feature**. **When in doubt, feature.**

## Tags

Every plan carries a `**Tags:**` header line (alongside `**Branch:**`) of backticked SDD-anchored
tags — `prd:FR-N`, `arch:<slug>`, `area:<slug>` — drawn from the vocabulary declared in
`docs/artifacts/README.md` (≥1 `area:` tag mandatory, ≤6 total). The pre-impl skill stamps it from
its grounding; the post-impl skill reconciles it against what actually shipped and copies it into
the artifacts index row when the plan is archived.

The pre-impl skill classifies the work and applies this; impl and post-impl follow it. When post-impl moves a plan to `docs/artifacts/` it re-dates the prefix to the implementation date and **keeps the `bug` segment**.

## Test plan — mandatory

Every plan in this folder carries a **Test plan** section, and every work unit within it names the
tests that will prove it before the code that satisfies them is written. TheSupervisor is developed
test-first: the pre-impl skill refuses to finish a plan without one, and the impl skill treats it as
the gate for each work unit (red → green → refactor).

This is not ceremony. The system is multi-process and multi-machine by construction — Agents,
Hub, Peers, pseudoterminals — so the defects that matter are integration defects, and the only way
to catch those without two laptops and a live Claude session is to design the seams up front. See
`docs/sdd/thesupervisor-architecture.md` for the fake-at-the-edges, real-transport-in-the-middle
strategy the test plans are written against.

## Design documents

`design.md` is the exception to the naming rule above: it is the durable output of the founding
design session, not a plan, and it is not archived. Plans reference its decision ids (`D1`…`Dn`).
The living specification lives in `docs/sdd/`; `design.md` records how it came to be.

## Hotbugs — the one trunk-direct path

Work normally lives on a branch: pre-impl always creates one, and **impl refuses to implement while on `main`**. The single exception is a **hotbug** — an urgent fix that goes straight to trunk with **no branch and no PR**:

```
/pre-impl --hotbug <the broken thing>
```

Such a plan is still a `YYYY-MM-DD-bug-<slug>.md` bug plan, but it opens with a `⚠️ HOTBUG — TRUNK-DIRECT` callout and a `**Hotbug:** YES` line. That declaration is the *only* thing that lets impl run on `main`, and it tells post-impl the direct push to trunk is intentional rather than an accident. A hotbug is by definition a bug fix — if it ships any new capability, it is a feature and belongs on a branch.
