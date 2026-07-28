# Control is tiered: Owned Agents are steerable, Foreign Agents are not

A running Claude Code session cannot have a turn injected into it by anything that does not own its
pseudoterminal. Rather than hide that, we make it a first-class distinction: an **Owned Agent** was
launched by TheSupervisor into a Hub-owned ConPTY and accepts injected input; a **Foreign Agent**
enrolled from someone else's terminal and is fully observable but only cooperatively controllable,
picking up queued commands on its next tool call. The UI never offers an action it cannot perform on
that row.

## Considered Options

- **Cooperative control for everything, uniformly.** Rejected: an idle or input-blocked Agent can
  never be woken, which removes the single highest-value action — unblocking an Agent that is
  waiting on you.
- **OS-level input injection into foreign terminal windows.** Rejected: brittle against window
  focus and VS Code versions, effectively untestable, and behaviourally indistinguishable from
  malware to endpoint security.

## Consequences

- The in-app terminal is load-bearing architecture, not a convenience: launching an Agent there is
  the *only* way to make it fully controllable.
- Ownership is fixed at launch and cannot be transferred, so a Foreign Agent can never be promoted.
- Every UI action must be gated on tier, and the roster row must make the tier legible at a glance —
  otherwise the product looks broken when an action is missing.

## Scope clarified — 2026-07-27: steering, not lifecycle

This ADR governs **steering** — injecting input into a running Agent — which genuinely requires
owning its pseudoterminal. It does **not** govern termination, which needs only a pid. A Foreign
Agent may therefore be stopped, by hard kill, even though it cannot be steered. That action is
presented as visibly distinct and more forceful than an Owned Agent's graceful ladder (Ctrl+C →
exit instruction → Job Object kill on timeout), and is acceptable because the Agent's transcript is
already persisted and its conversation remains resumable.

## Challenged and upheld — 2026-07-27

Investigation found that Claude Code's background-agent daemon publishes each worker's PTY named
pipe and auth token in a **user-readable** `~/.claude/daemon/roster.json`, meaning a background Agent
we did not launch could in principle be attached to and steered. We considered adding an
"Attachable" third tier for that case and **rejected it**: control would then depend on an
undocumented pipe protocol that can change in any release, producing UI actions that silently stop
working after an upgrade. Foreign remains Foreign regardless of Agent kind.

