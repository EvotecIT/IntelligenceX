# Ledger agent service

One service on every machine that lets Claude and Codex sessions on any machine ask each other for work, get their
questions and answers back without anyone relaying them, and recover when something breaks. It replaces the Node
coordination host and its scripts, and GitHub issues between agents. Issues stay for what needs a person.

## What it must do

1. **Address any agent directly.** A session on one machine asks Codex or Claude on a named machine (or "any machine
   that can") to do something: a new session for new work, or an existing session by id.
2. **Bring questions and answers back.** When the agent doing the work asks something or finishes, the requester
   learns about it on its own.
3. **Spend no tokens waiting.** Nobody polls with a model. A session is woken only when something it must act on
   arrives, and the wake is the only turn spent.
4. **Work both ways.** Codex to Claude and Claude to Codex, on the same machine or across machines.
5. **Notice when things go wrong and fix them** where the fix is mechanical, and say plainly when it isn't.

Rules are not code. Which agent or machine takes a kind of work, when to retry, wake, escalate or give up, what
counts as stuck and how much a request may spend come from the Ledger policy and the shared skills. The service applies
them; changing them needs no rebuild.

## Shape

- **The Ledger service** on the Windows PC stays the hub: the request queue, goals, results and every machine's state.
- **The agent service** on each machine is the existing reporter grown into the machine's one long-running agent
  process: the same .NET program everywhere, running as the signed-in user so it can start that user's Codex and
  Claude. Windows: a sign-in task. macOS: a LaunchAgent in the login session. The platform restarts it when it exits.

Each agent service:

1. **Reports** sessions, accounts, archive state and machine load (today's reporter).
2. **Knows how to reach each live session.** Codex threads are reached through the Codex app server. Claude Code
   sessions are reached through Claude Code's own cross-session messaging (`SendMessage`), which is documented within
   a machine and across machines. The documented inbox socket specifies only its auth line, not the message frame, so
   the service doesn't write to it; it runs a one-shot `claude -p` on the cheapest model whose only job is to send the
   message with `SendMessage`. The model and token count of every such delivery go into the Ledger, so the cost is
   measured rather than assumed. If the inbox frame is documented later, the service writes to it directly instead.
3. **Takes requests for its machine** from the Ledger and holds the machine's lease while working.
4. **Runs them through official clients**: the Codex app server (`thread/start`, `turn/start`) or Claude Code
   (`claude -p` for new work; the session inbox for an existing session).
5. **Returns results and questions** to the Ledger, and **delivers** them to the requester: a Codex `turn/start` in
   the requesting thread, or a `SendMessage` into the requesting Claude session. A session that has ended gets the
   answer at its next start through the session-start hook, and Marquee shows it either way.

## Measuring cost

Waiting costs nothing: no session polls with a model. Every wake and every Claude delivery records its provider,
model and tokens in the Ledger with the request it belongs to, and the Ledger reports totals per request and per day.
Acceptance for each phase includes these figures from real round trips, not estimates.

## Detecting and fixing failures

| What goes wrong | How it's seen | What the service does (policy decides limits) |
| --- | --- | --- |
| A machine's service stops | Its heartbeat goes quiet in the Ledger | The platform restarts it; the Ledger marks the machine unavailable and routes elsewhere |
| A request isn't picked up | Admission deadline passes | Re-offer to another eligible machine, or hold and tell the requester |
| The agent session dies mid-work | Turn ended interrupted, process gone | Resume once in the same session, then report |
| A delivery can't be made | Inbox gone, thread busy, socket refused | Keep it queued; deliver when the session is back; never replay one already delivered |
| Usage limit | The provider reports it | Route to the next provider or account the policy allows, or wait for the reset |
| A wait runs past its time | Due time passes | Wake the requester with what is known |

## Order of work

1. Requests between the Windows PC and the Mac mini through the services, with Codex as the runner, round trip both
   ways, with the cost of each wake recorded.
2. Delivery into a Claude session through a one-shot `claude -p` and `SendMessage` on the cheapest model, measured.
3. Claude as a runner and delivery into Claude sessions across machines.
4. The Mac Studio; Marquee's view of requests and answers.
5. Retire the Node coordination host and agent-to-agent issues.

## Not used

Hooks can't wake an idle session. Claude Code channels are a research preview and terminal-only. `claude -p --resume`
on a session the desktop app has open isn't documented as safe. The inbox socket's message frame isn't documented, so
it isn't written to. Undocumented session files and keys are not read.

## What exists today

The Ledger's coordination engine already has the request queue, and it doesn't depend on any provider. States are
`queued`, `admitted`, `submitted`, `started`, `reconcile`, then `succeeded`, `failed` or `cancelled`. Every route is
under `/api/coordination/v1`, authenticated as a `client`, `host` or `operator` principal or as an enrolled
requester.

- **Submitting** needs an existing goal owned by the caller. The request must carry the goal's `ControlRevision` and
  `AuthorizationRef`, a `Checkpoint` (project, branch, commit) and resources. `help` and `review` also need `ReturnTo`.
  The Node requester creates the goal and enrolls itself automatically.
- **Admission** is internal scheduling that assigns a machine, generation, grant and assignee.
- **A host** pages `/requests`, `/results` and `/retirements`, sends a heartbeat to `/hosts`, announces workers, binds
  a helper to the grant, sends `submitted`, `started`, `renew` and `terminal` receipts, stores the result (8 KB at most)
  and runs delivery (`result/begin`, then delivery receipts). The requester consumes the result, and the host retires
  the helper.
- **The Node host and worker** (`host-service`, `host-controller`, `run-worker`, `assigned-worker`, `codex-worker`,
  `result-return`, `retire-worker`, `codex-requester`) drive all of this:
  - a one-second tick;
  - Codex helpers through `thread/start` and `turn/start`;
  - completion by polling `thread/turns/list`;
  - results delivered by queueing a message into the requester's thread.

  The host and worker side handles Codex only, and parts of it only work on Windows: boot id through `pwsh`, ACL
  checks, the sandbox mode and the scheduled-task launcher.
- **There is no question channel.** A worker can only end as succeeded or failed.

The agent service keeps the engine and its routes. It replaces the Node host and worker with .NET in the machine
service, and adds three things:
1. a `question` state with answer delivery;
2. a single `ask` call that creates the goal and request together for a session that has no goal;
3. Claude as a runner and as a receiver.

## Phase 1 as built

The reporter runs the agent service when its configuration has an `agent` section (`AgentSettings`: coordination
client, local state, workspace root, capabilities, Codex and Claude executables). `IntelligenceX.Ledger agent-check
--config <reporter.json>` checks the Codex and Claude routes on a machine without touching the Ledger.

- **Host.** Heartbeats every grant/3 seconds with the attested capabilities and `codex` as a creatable provider when the
  app server answers. The boot identity is kept in local state, so restarts within one boot keep their grants. Each
  sweep reads the four feeds (assigned work, results, questions, retirements); no model is involved.
- **Codex runner.** `codex app-server proxy` to the Codex app's daemon when its control socket answers (the Mac), or a
  private `codex app-server` (Windows, where the app serves its threads over stdio). Helper threads get `thread/start`
  in an empty workspace, are named `Auto: Ledger <id>: …`, and each turn uses an output schema that asks for either a
  result or one question. Completion comes from `turn/completed`; a lost connection is reconnected and the turn read
  once. Run records are written before each step, so a restart follows the same thread and turn.
- **Ask.** `POST /api/coordination/v1/ask` (MCP `ask`) creates the goal `ask-<id>` and a `help` request for a client
  principal scoped to one machine. Provider and capability fall back to policy routing and the target machine. Asks
  don't hold the asker's branch: helpers work in their own workspace.
- **Questions.** A helper's question moves the work to `question` (occupied, grant renewed). The requester's host
  delivers it; the answer (MCP `ask_answer`) or the policy's expiry is delivered back into the same helper thread as
  its next turn, which returns the work to `started`. Each side's delivery is begun before the send and never repeated
  once sending or uncertain. Queued asks past `ask.admissionMinutes` produce a notice for the requester.
- **Delivery.** Codex: `turn/start` in the requesting thread, `turn/steer` when a turn is running, and `codex queue`
  when the Codex app holds the thread. Claude: a one-shot `claude -p` on `ask.claudeDeliveryModel` whose only tools are
  SendMessage and ListAgents. SendMessage reaches sessions by the name and ref ListAgents shows, so a Claude asker
  passes that address with its ask. An ask ends when its result is submitted to the asking session.
- **Usage.** Every helper turn (`worker`), woken requester turn (`wake`) and Claude sender run (`delivery`) records
  provider, model and tokens on its request. `GET /requests/{id}/usage` and `GET /usage?date=` give totals per request
  and per day.
- **Policy.** `ask.*`: default routing kind, question wait, question limit, admission wait, turn limit, resume attempts,
  wake usage window, Codex model and effort, Claude delivery model.

### Measured round trips (17 November 2026, gpt-6.1-sol helpers, Haiku sender)

| Step | Machine | Tokens | Cached |
| --- | --- | ---: | ---: |
| Windows → Mac: helper turn that asked which volume | mac | 30,503 | 0 |
| Windows → Mac: wake that answered with `ask_answer` | win-pc | 67,977 | 67,200 |
| Windows → Mac: helper turn after the answer | mac | 61,473 | 60,800 |
| Windows → Mac: wake for the result | win-pc | 34,228 | 33,920 |
| Mac → Windows: helper turn | win-pc | 58,664 | 29,056 |
| Mac → Windows: wake for the result | mac | 34,624 | 34,304 |
| Claude delivery: one-shot sender (SendMessage) | win-pc | 8,456 ($0.00045) | 6,847 |

Both Codex round trips completed in under a minute end to end. A Codex wake costs about 34k tokens, almost all of it
cached context; the Claude sender costs about 8k tokens on Haiku.

### Sign-in

The agent service checks `claude auth status --json` and `codex login status` every five minutes (every minute while
one needs attention), and learns of a Claude usage limit or lapsed login from the sender's own failure.

- While the Claude CLI can't send, Claude deliveries are held in the Ledger (`claude-sign-in-required`), never dropped.
- The reporter publishes a `sign-in` item per provider and machine (`<machine>:sign-in:<provider>`). It is needs-you with
  reason `sign-in-required` while sign-in is needed and done with `signed-in` afterwards. Its `signIn` field carries
  `provider`, `required`, `reason` (signed-out, expired, usage-limit), `command` (`claude auth login` or `codex login`),
  `sinceUtc`, `atUtc` and `held` (Claude deliveries waiting on it).
- On a Mac the service shows a notification and a dialog whose "Sign in" button opens Terminal running that command.
  AppleScript notifications can't carry an action of their own, so the dialog is the one-tap route.
- When the next check or send succeeds, the item is cleared and held deliveries go out.
- A session the sender can't reach (ended or closed) gets its message held as `claude-session-unreachable` and offered
  again every half hour.
- `IntelligenceX.Ledger inbox --config <reporter.json>`, run as a Claude Code SessionStart or UserPromptSubmit hook,
  hands the session whatever is waiting for it as hook context, begun before it prints and recorded as submitted after.

### Known limits

- A Claude desktop session running in a different permission mode from the sender holds incoming SendMessage
  deliveries for its user's approval. The delivery is recorded as submitted; the session sees it once approved.
- `codex queue` hands a message to the Codex app, which runs it the next time it loads the thread.
- After a reboot or a lapsed grant the helper is stopped and its work so far is returned as a failed result; it isn't
  resumed in place.
- Workspace content left by a helper holds its retirement for review instead of being deleted.
