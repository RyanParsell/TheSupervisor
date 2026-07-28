# Agents and humans have equal authority over the Fleet

Any Agent can issue the same Commands a human can — list, inspect, send, launch, stop — against any
Agent on any paired machine, because every Agent in the Fleet belongs to the same person and is
equally trusted. This deliberately makes TheSupervisor a cross-repo, cross-machine coordination bus
rather than a read-only dashboard, since the requirement was to drive the fleet *from any agent*, not
only from the UI.

## Considered Options

- **Agents read and self-report only; humans command.** Rejected: it makes runaway loops impossible
  by construction, but also makes unattended cross-agent orchestration impossible, which is the
  capability that motivates the CLI being available inside every Agent at all.
- **Agents propose, human approves.** Rejected: it only functions while someone is watching, which
  defeats the unattended fan-out that gives orchestration its value.

## Consequences

- Loop safety must be structural, not permission-based: Agent-originated Commands carry a hop count
  and die after N forwards, and are rate-limited per originating Agent.
- Every Command is attributed to its originator in the UI, so an unattended ping-pong between two
  Agents is visible immediately rather than discovered via a token bill.
- A confused Agent can stop another Agent's work. This is accepted; the blast radius is one
  developer's own machines, and the attribution trail makes it diagnosable.
- The CLI surface and the MCP tool surface must delegate to one shared service so they cannot drift
  — the pattern WExpert proved with `UiControlService`.
