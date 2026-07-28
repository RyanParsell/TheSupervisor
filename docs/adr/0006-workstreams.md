# Workstreams give effort continuity across Agent restarts

An Agent is one session, and sessions die — on crash, on `claude -c`, on restart to pick up a config
change — taking their Roster row and history with them. A **Workstream** is a durable thread of
effort identified by repository and branch that successive Agents attach to, so restarting a session
continues a visible effort rather than starting from nothing. We accepted the extra concept because
the Roster answering only "what is running right now" loses the more useful question, "what have I
been working on here."

## Considered Options

- **No continuity — an Agent is a session.** Rejected despite being the smallest data model: a
  pending Command dies with its session, and a branch worked across four restarts looks like four
  unrelated events.
- **Link only on verified resume.** Rejected: it depends on resume semantics we have not verified,
  and produces continuity that appears or vanishes depending on how the session happened to be
  restarted.

## Consequences

- The key is repository + **current** branch, and a session that switches branch **moves** to the
  Workstream for the new branch. This is deliberate: the `pre-impl` flow cuts a feature branch
  mid-session, and the effort genuinely does move with it.
- A Workstream never occupies a Roster row (ADR-0003's pane stays "where am I needed"). It appears
  as context on a live Agent's row and as its own detail view.
- It is the first durable, growing data in the system. Storage is a **bounded session index** —
  session id, machine, time range, final Activity Summary, and a *pointer* to each transcript rather
  than a copy — so it cannot grow without limit and does not duplicate conversation content.
- A Workstream can outlive its branch and its repository, so cleanup must be offered when both are
  gone.
