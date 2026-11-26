# Local ask route while the Ledger is unreachable

## Problem

The Ledger service runs on one machine. When that machine is off, `ask` from any other machine returns
`unreachable` (IntelligenceX #1523). That is correct for asks to another machine, but Claude and Codex on the *same*
machine also lose the ability to hand each other work, ask questions and get results, although nothing that work needs
lives on the Ledger host.

Goal: while the Ledger can't be reached, an ask for a helper on this machine runs here, with questions, answers,
results and status reads, through the same tools. When the Ledger is back, the request appears there under the same id,
finished, and nothing runs twice. Asks to another machine keep failing fast.

## Design

### A local coordination store in the agent service

The agent service already does all the work: it starts helpers, relays questions and answers, publishes results and
delivers them into sessions. It reads that work from five Ledger feeds (assigned requests, results, questions, wakes,
retirements) and writes receipts back. The coordination engine, store and HTTP endpoints are self-contained
(`CoordinationStore` over its own SQLite file, `CoordinationEndpoints.Map`).

So the agent service hosts a second, machine-scoped coordination store and serves its endpoints on loopback
(`http://127.0.0.1:<port>/`, which `LedgerClientSettings` already allows):

- Its own database under the agent's state folder (`agent/local-coordination/coordination.sqlite`).
- Its policy is the Ledger's, narrowed to this machine. The heartbeat answer carries the policy revision and the
  host's projects; when either changes, the agent reads `GET /api/coordination/v1/hosts/policy` (the policy with only
  the caller's machine, so a host still can't read the full policy) and caches it. The local store never admits work
  for another machine, and it keeps finished requests live for an hour (or the Ledger's retention, when shorter), so
  a follow-up ask (`continues`) works during an outage; the heartbeat then archives them for import.
- Before the Ledger has ever answered (nothing cached yet), the store runs on a built-in bootstrap policy: this
  machine only, Codex and Claude helpers with one agent slot each, no build, test or benchmark capacity, and principals
  scoped to the repositories checked out directly under the agent's `repositoryRoots` (the origin remote's repository
  name, read from `.git/config`). It is built in memory and never cached, so the Ledger's first policy replaces it: in
  place when the projects match, by a store restart when they differ. A cached policy without this machine, or a cache
  file that can't be read, closes the store; the bootstrap policy is never used again once a cache file exists. The
  installer keeps `repositoryRoots`, takes `--repository-root`, and falls back to `EVOTEC_GITHUB_ROOT`.
- Two principals the agent creates on first start and keeps in its private state (mode 600): `<machine>-host` for
  itself and `<machine>-sessions` for this machine's sessions, the same ids as on the Ledger, so an imported request
  is visible there to the same sessions. When the Ledger gives the host other projects, the agent restarts the store with the new scopes. They exist only in the local store; the Ledger's principals and tokens are
  not touched.

A second `AgentService` instance in the same process runs against the local endpoint with its own state subfolder.
Every helper, question, result, delivery and retirement path is then the existing code: there is no second
implementation to keep in step. It reads the boot identity and the Claude permission modes the session hooks record from the Ledger instance's state
folder. Only the Ledger instance asks the user to sign in, so a signed-out provider produces
one prompt, not two.

### Routing in the MCP tools

The agent writes `local-client.json` (endpoint, sessions token, machine id; mode 600) beside the machine's Ledger
client files when its local store starts. When the store stops, it removes the file only if the Ledger has every
request it held; otherwise the file stays, so those ids keep resolving to the (stopped) store and are reported
unreachable rather than sent to the Ledger. The MCP reads the file again whenever it changes, so sessions started
before the store use it too; a missing or damaged file changes nothing.

- **`ask` without an id** (a new ask): try the Ledger. On `unreachable`, if the target is this machine (`machine` equals
  the local machine id, or no machine is named), submit the same ask to the local store for this machine and return it
  with `"route": "local"`. Otherwise, or when the local store isn't running or refuses it, return `unreachable` as
  today, with `local` naming why the local store didn't take it.
- **`ask` with an id** (a retry): if the local store has that id, return it. Only the store's own "not found" sends it
  to the Ledger: when the file is there but the store doesn't answer (the agent crashed), the retry is reported
  unreachable, since the store may hold it. An id that may already be in the Ledger (an earlier `uncertain`) is never
  routed locally, so it can't run twice.
- **`ask` with `continues`** (a follow-up): to the store holding the ask it continues, so a local conversation can be
  continued after the Ledger is back, until that ask is imported.
- **Request-scoped tools** (`ask_answer`, `ask_images`, `ask_usage`, `request_status`, `request_result`,
  `request_consume_result`, `request_accept`, `request_cancel`): the local store when it holds the id, otherwise the
  Ledger. A request is never in both stores, so this needs no Ledger call to decide.
- **`coordination_status`:** while the Ledger is unreachable, the local summary with `"route": "local"`.
- Wakes can use the same route later. They are not part of the first change.

### Reconciliation when the Ledger is back

Local requests stay local until they are finished: an ask finishes once its result reaches the asking session and its
helper is retired. The local store then archives it, and once a minute the agent posts each archived entry to
`POST /api/coordination/v1/imports` as the machine's existing host principal: the archive entry as the local store
wrote it (work, binding, result, retirement, questions, usage, goal) and the result content.

The Ledger writes the result file and the archive entry, records an `imported` change with `local:<machine>`, and
never schedules it:

- An import is idempotent by id. A repeat with identical content returns the stored record.
- An id the Ledger already has with different content is rejected with `import-conflict`, and the local record is kept
  for a person to look at.

The Ledger checks the host's project scope and machine as for a submission. An import may use the service's 1 MiB
request limit, since an entry with questions, answers and the result can exceed a command's 64 KiB. The images the
ask and its result name are uploaded first under the same content ids, and the ask's display title travels with the
entry; the Ledger writes the entry, the title and the result file in one store operation, only after the import is
accepted. The agent pages through the whole local archive and remembers each id it has sent (imported, refused or too
large), so it sends each once, and one that isn't imported never holds up the ones after it. Only an imported (or
already held) id lets the agent remove the local client file when its store stops.

Requests still running when the Ledger returns finish locally. New asks go to the Ledger again from the first
successful call.

## What changes where

| Component | Change |
| --- | --- |
| Agent service | Host the local store on loopback, run a second `AgentService` instance against it, cache policy, export settled requests. |
| MCP | Route new same-machine asks and request-scoped calls as above. |
| Ledger service | Import endpoint and engine operation; `hosts/policy`; policy revision and projects in the heartbeat answer. |

`localCoordinationPort` in `agent.json` defaults to 7349, and 0 turns the local store off. The installer only adds
`repositoryRoots` for the bootstrap policy.

## Limits and risks

- **Capacity.** Local and Ledger admission count slots separately. While the Ledger is down it admits nothing new for
  this machine. When it returns while local helpers still run, the machine can briefly run more helpers than
  `agentSlots`. The local agent can report its running helpers in the Ledger heartbeat as occupied, which closes this.
- **Follow-ups after the import.** The Ledger resolves `continues` from live work only, so once a local ask is imported
  (an hour after it finished) a follow-up starts fresh rather than continuing it.
- **Dependencies.** A local ask can't depend on a Ledger request, and a Ledger request can't depend on a local one,
  imported or not: the Ledger resolves dependencies from live work only.
- **Status after recovery.** Once the Ledger answers, `coordination_status` shows the Ledger's work only, so a local
  ask still running is visible through `request_status` (which finds it locally) but not in the project summary.
- **Retries after the import.** A local ask that named no machine is stored with this machine's id. While the local
  file is there, a retry goes to the local store and matches; after the file is removed, a retry without `machine`
  doesn't match the Ledger's copy and is refused as `request-id-conflict` (nothing runs twice).
- **Bootstrap scope.** The bootstrap policy trusts the checkouts on disk: any repository cloned under a repository
  root with an origin remote can be asked about locally until the Ledger's policy arrives. A clone added during an
  outage restarts the store, like a project change from the Ledger.
- **Other machines can't see local work** until the import, apart from what the reporter sends (the helper sessions
  themselves).
- **New local credentials.** They never leave the machine and grant nothing on the Ledger, but they are new
  credentials and need agreement.

## Phase 2: asks to another machine

The store on the helper's machine owns the ask. The Ledger already delivers across machines: the helper's machine
reads the work assigned to it, and the asker's machine reads the results and questions addressed back to it. A peer's
local store can play the Ledger's part in that exchange without engine changes:

1. A new ask for another machine, with the Ledger unreachable, goes to that machine's local store.
2. That machine's agent runs it as usual.
3. The asking machine's agent also reads each peer store's results and questions for its own sessions and delivers
   them with the existing code.

That needs a listener on the LAN with TLS and a pinned certificate (as the Ledger has), a host and a sessions principal
for each peer in each store, created and copied while everything is up, the peers' endpoints and pins in the cached
configuration, and macOS Local Network permission for the agent. Asks to win-pc still fail fast while the PC is off.

## Tests

- MCP: a new ask to this machine with the Ledger unreachable goes to the local store. An ask to another machine still
  returns `unreachable`. A retry whose id isn't in the local store is never sent there.
- Agent service: a local ask runs a Claude helper, delivers a question and its answer, and delivers the result. A
  script stands in for the `claude` CLI, since the agent tests have no end-to-end helper run yet. Codex helpers use
  the app server, so the recorded run covers them.
- Import: identical repeats return the stored record. A conflicting id is rejected. An imported request is never
  scheduled.
- Recorded run on a Mac with the Ledger unreachable: a Claude-to-Codex and a Codex-to-Claude ask complete. After the
  Ledger returns, `request_status` on the Ledger shows each one once, as finished.
