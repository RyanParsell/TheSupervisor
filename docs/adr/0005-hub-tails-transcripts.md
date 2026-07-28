# The Hub tails Agent transcripts to keep Activity Summaries current

An Activity Summary derived only from the submitted prompt goes stale the moment a long task starts,
so the Hub watches each Agent's transcript file (`~/.claude/projects/<slug>/<sessionId>.jsonl`) and
derives the live line from it — current tool, latest assistant text, turn boundary. We chose this
over hook-based reporting because it adds **zero latency to the Agent**, where a `PreToolUse` hook
would add roughly 130–200 ms to every single tool call, in every session, all day, to populate a
pane that is consulted occasionally.

## Considered Options

- **`PreToolUse` hooks push activity.** Rejected on measured cost: hooks are a supported, documented
  extension point and depend on no file format, but the per-tool-call latency is paid continuously
  by the developer and is felt directly during work.
- **Selective hooks on Bash/Edit/Write/Task only.** Rejected: cuts frequency but goes silent during
  long stretches of reading and searching — precisely when you'd wonder what an Agent is doing.
- **Prompt only, no transcript reading.** Rejected: most private and simplest, but a long-running
  Agent would show an hour-old summary.

## Consequences

- The Hub reads full conversation content. On-machine this is the developer's own data, but if the
  Model Backend is later used to summarize, that content leaves the machine unless a local backend
  is configured. This must be an explicit, visible setting.
- The transcript format is internal and unversioned. The failure mode is deliberately soft: if
  parsing fails, the summary degrades to the prompt-derived baseline and no Agent disappears.
- Rows carry an age indicator, so an Agent whose activity has not advanced reads as visibly stuck
  rather than being silently misreported as current.
