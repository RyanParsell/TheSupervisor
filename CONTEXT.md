# TheSupervisor

A CLI, skill, and local UI for supervising the AI coding agents a single developer has running
concurrently — across editor windows, repositories, and machines — from any one of them.

## Language

**Agent**:
One enrolled Claude Code session, interactive or background.
_Avoid_: session, context window, worker, instance

**Owned Agent**:
An Agent launched by TheSupervisor into a Hub-owned pseudoterminal, and therefore fully
controllable — input can be injected into it directly.
_Avoid_: local agent, managed agent, child agent

**Foreign Agent**:
An enrolled Agent launched outside TheSupervisor, and therefore observable but only
cooperatively controllable — it collects queued commands when it next calls a tool.
_Avoid_: remote agent (that means a different machine), external agent, unmanaged agent

**Unenrolled Agent**:
A Claude Code session seen on a machine via `claude agents --json` that has not enrolled —
visible in the Roster, never controllable.
_Avoid_: ghost, orphan, unknown agent

**Subagent**:
Nested work spawned inside an Agent; never itself an Agent.
_Avoid_: child agent, task agent

**Workstream**:
A durable thread of effort identified by repository and branch, which successive Agents attach to.
_Avoid_: project, task, session group, thread

**Repository**:
The normalized git remote URL of an Agent's working directory — identity, not location. Falls back
to machine plus absolute path when there is no remote.
_Avoid_: repo path, folder, project, checkout (a checkout is one machine's copy of a Repository)

**Machine**:
One host running exactly one Hub for the user, identified by a stable generated id and displayed by
hostname.
_Avoid_: node, box, host (ambiguous with the Hub process)

**Enrollment**:
The act by which an Agent becomes known to the Hub and gains a control channel.
_Avoid_: registration, discovery, attach

**Hub**:
The single long-lived process that holds the roster of enrolled Agents and brokers commands to them.
_Avoid_: server, daemon, supervisor (ambiguous — see Flagged ambiguities)

**Roster**:
The Hub's live set of enrolled Agents.
_Avoid_: list, registry, pool

**Activity Summary**:
The one-line answer to "what is this Agent working on right now."
_Avoid_: status (that is the coarse busy/idle/waiting enum), description, title

**Status**:
The coarse lifecycle state of an Agent — busy, idle, or waiting on input.
_Avoid_: state, activity

**Model Backend**:
The configurable model provider the Hub itself calls when a capability needs inference.
Anthropic/Claude by default; a local endpoint (e.g. LM Studio) is a supported alternative.
_Avoid_: the model, the LLM (ambiguous — the Agents also call models, but through their own harness)

**Peer**:
Another machine's Hub that has completed Pairing with this one.
_Avoid_: node, remote hub, server

**Pairing**:
The one-time, deliberate exchange that records a Peer's endpoint and shared secret on both sides.
_Avoid_: connecting, linking, discovery

**Fleet**:
Every Agent across this Hub and all its Peers — the merged view a single UI presents.
_Avoid_: cluster, network, swarm

**Command**:
An instruction delivered to an Agent, carrying its originator, a hop count, and a time to live.
_Avoid_: message, request, task, job

**Pending Command**:
The single Command awaiting pickup by an Agent; sending another replaces it.
_Avoid_: queue, backlog, inbox

## Relationships

- An **Agent** completes **Enrollment** exactly once per session; ending the session ends the enrollment.
- **Enrollment** is involuntary — performed by the harness (MCP transport + hooks), not by the model.
- Many **Agents** enroll with one **Hub**; the **Hub** holds them as its **Roster**.
- An **Agent** has exactly one **Status** and exactly one **Activity Summary**.
- An **Agent** may own zero or more **Subagents**; they roll up onto the Agent's row and never
  appear in the **Roster** in their own right.
- An **Activity Summary** is derived involuntarily (submitted prompt, then active task/tool) and may
  be overridden by the Agent itself through the skill.
- Every Agent is either **Owned** or **Foreign**; the distinction is who holds its pseudoterminal,
  and it determines which actions the UI may offer on that row.
- A **Foreign Agent** can never be promoted to **Owned** — ownership is decided at launch and is
  not transferable.
- An **Unenrolled Agent** is not an **Agent**; it is a gap the Roster reports rather than hides.
- A **Hub** federates with zero or more **Peers**; together they present one **Fleet**.
- **Pairing** is mutual and explicit — a Hub never trusts a Peer it has not paired with.
- A **Command** may originate from a human or from an Agent; both carry equal authority, and both
  are attributed to their originator.
- A **Command** may cross one Peer boundary to reach a remote **Agent**; live terminal output
  never crosses that boundary.
- An **Agent** has at most one **Pending Command**; a new one replaces it rather than stacking, so a
  woken Agent acts on the latest intent rather than a backlog.
- A **Command** is always in exactly one state: queued, delivered, acknowledged, expired, or
  refused — and its originator is told which.
- An **Agent** belongs to exactly one **Workstream** at a time; switching branch moves it to the
  Workstream for the new branch, creating that Workstream if it does not exist.
- A **Workstream** accumulates every **Agent** that has ever worked its branch, across restarts and
  across machines, and outlives all of them.
- A **Workstream** never occupies a **Roster** row. Only live **Agents** do; the Workstream appears
  as context on the Agent's row and as its own detail view.
- A **Repository** is machine-independent when it has a git remote, so one **Workstream** can span
  **Machines** — the same branch worked from two boxes is one effort, not two.
- A **Machine** hosts exactly one **Hub**; **Agent** identity is qualified by Machine, because
  derived session names collide across boxes.

## Example dialogue

> **Dev:** "If an agent never runs the skill, is it still in the list?"
> **Domain expert:** "Yes — **Enrollment** is involuntary. The MCP transport connects at session
> start whether or not the model ever calls a tool. The skill only adds richer verbs on top."
>
> **Dev:** "So a silent agent has no **Activity Summary**?"
> **Domain expert:** "It always has one. It falls back to what you last asked it to do. The skill
> only lets a cooperative agent replace that with something better."
>
> **Dev:** "One of my agents is blocked on input. Can I answer it from the UI?"
> **Domain expert:** "Only if it's an **Owned Agent**. If you launched it in your own VS Code
> terminal it's a **Foreign Agent** — you can see that it's blocked, but you have to go answer it
> where it lives. That's the whole reason to launch agents from TheSupervisor's terminal."

## Flagged ambiguities

- **"Supervisor" is overloaded.** Claude Code's own background-agent daemon already writes
  `supervisorPid` into `~/.claude/daemon.status.json` and `~/.claude/daemon/roster.json`. That is an
  unrelated, pre-existing concept. This project's central process is called the **Hub**; the product
  name TheSupervisor refers to the whole system, never to a process.
- "Register" was used for both **Enrollment** and Claude Code's `claude mcp add` install step —
  resolved: installing the MCP server is *installation*; a session joining the roster is **Enrollment**.
