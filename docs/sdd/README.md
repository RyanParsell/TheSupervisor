Markdown files in this folder are durable spec-driven development documents. These documents are continually updated.

| Document | Holds |
|---|---|
| `thesupervisor-prd.md` | Requirements — `FR-N` functional, `NFR-N` non-functional. The `prd:` tag namespace. |
| `thesupervisor-architecture.md` | How it is built — components, decisions, data flow. The `arch:` tag namespace. |
| `thesupervisor-stories.md` | User stories grouped under `## Area:` headings. The `area:` tag namespace. |

## Story ids

Stories carry a timestamp id: `S-YYMMDD.HHMMSS` plus a lowercase letter for stories created in the
same second — for example `S-260727.143012a`. The id is permanent and is cited by plans, commits,
and artifact index rows, so a shipped change can always be traced back to the story that asked for
it. Never renumber a story; supersede it and link forward.

## Relationship to `docs/plans/` and `design.md`

These documents are the **living specification** — they describe the system as it is intended to be
right now. A plan in `docs/plans/` describes one change to it, and post-impl reconciles these
documents against what actually shipped before archiving the plan.

`docs/plans/design.md` is different again: it is the record of the founding design session that
produced the first version of these documents, including the alternatives that were rejected. It is
not updated as the system evolves. When these documents and `design.md` disagree, **these documents
win** — and the divergence is worth an ADR in `docs/adr/`.
