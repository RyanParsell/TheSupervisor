# Enrollment runs beside the stdio transport, never in front of it

The shim serves MCP immediately and enrols its Agent on a background task with a bounded retry,
rather than resolving session context before starting the transport. Claude Code spawns MCP servers
roughly 3.5 seconds *before* it writes `~/.claude/sessions/<pid>.json`, so a single resolve at
startup succeeded 1 time in 5 against real sessions — intermittently and silently, leaving the
Roster randomly missing rows with nothing to indicate why.

## Considered Options

- **Resolve once at startup.** What was originally built. Rejected on evidence: it fails most of the
  time, and its failure mode is invisible.
- **Wait for the session file before serving.** Rejected: it delays every session's stdio handshake
  by seconds, which is exactly the "never disturb the session" rule in NFR-1 — and it would risk
  Claude Code timing the server out.

## Consequences

- A Roster row can appear a few seconds after its Agent starts. That lag is acceptable; a missing
  row is not.
- The shim has two concurrent lifetimes (transport and enrollment) and must cancel the second when
  the first ends, or a very short session leaks a pending task.
- Deregistration depends on enrollment having completed, so a session shorter than the resolve
  window enrols nothing and deregisters nothing — correct, but it means the Hub's reconnect grace,
  not the shim, is what eventually reaps such an Agent.
- This was found only by smoking against real `claude` sessions. No unit test over the shim would
  have caught it, because the race is in another product's startup sequence.
