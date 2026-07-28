# Hub per machine, federated — not one central Hub

TheSupervisor must eventually span multiple machines, but every Agent connects only to a Hub on its
own machine over loopback, and Hubs peer with each other to serve a merged fleet view. We chose this
over a single network-reachable Hub because it preserves the loopback trust boundary that the
adapted WExpert host model depends on, and because local supervision must keep working when the
network — or the other machine — is unavailable.

## Considered Options

- **One global Hub, agents dial in over the network.** Rejected: it dissolves the loopback boundary,
  forcing real transport auth and TLS into v1, and makes supervision of your *own local* agents
  depend on another machine being awake.
- **Hub per machine, clients fan out.** Rejected: every client would reimplement aggregation, and a
  CLI running inside a remote Agent would need endpoints and credentials for every machine — which
  defeats the requirement that the CLI work identically from any Agent.

## Consequences

- A Hub-to-Hub federation protocol and peer discovery are net-new work with no WExpert prior art.
- Agent identity must be machine-qualified from the first commit, since two machines can host
  Agents with colliding local names.
- A partitioned Hub must degrade to showing its own Agents plus a clearly stale view of peers,
  rather than failing or silently dropping rows.
