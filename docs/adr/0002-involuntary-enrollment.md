# Enrollment is involuntary, carried by the MCP transport and hooks

An Agent joins the Roster because the harness connects it, not because the model chose to announce
itself: the CLI is installed once as a user-scope stdio MCP server so every session auto-connects at
startup, and SessionStart/SessionEnd hooks pin lifecycle deterministically. The skill sits on top
purely for rich, model-authored verbs. We did this because a supervisor whose worldview depends on
agents remembering to check in has exactly the wrong failure mode — the busiest agents would be the
least likely to report.

## Consequences

- Install is a one-time, machine-level act (`claude mcp add --scope user` plus hook settings), not a
  per-session or per-repo action.
- Every session on the machine pays the cost of spawning an MCP child process, whether or not the
  user cares about supervising it.
- The MCP stdio pipe doubles as the inbound control channel, so enrollment and control share a
  lifetime — losing one loses the other.
