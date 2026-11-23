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
- **The agent service** on each machine is one long-running agent process next to the reporter: the same .NET
  program everywhere, running as the signed-in user so it can start that user's Codex and
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

Setting up a machine (enroll, kit, `Install-LedgerAgent.ps1`) is in [ledger-agent-setup.md](../ledger-agent-setup.md).

`IntelligenceX.Ledger agent --config <agent.json>` runs the agent service (`AgentSettings`: machine, reporter
configuration for the Ledger address and credentials, coordination client, local state, workspace root, capabilities,
Codex and Claude executables). One lock file keeps it to one process per machine. `IntelligenceX.Ledger agent-check
--config <agent.json>` checks the Codex and Claude routes on a machine without touching the Ledger.

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
  reason `sign-in-required` while sign-in is needed, done with `signed-in` afterwards, and done with `sign-in-unknown`
  while no check has given an answer yet. Its `signIn` field carries `provider`, `required`, `reason` (signed-in, unknown,
  signed-out, expired, usage-limit), `command` (`claude auth login` or `codex login`), `sinceUtc` (when this state
  began), `atUtc` and `held` (Claude deliveries waiting on it). Read one with `GET /api/ledger/v1/items/<machine>:sign-in:<provider>`.
- A check that times out or can't be read is inconclusive: the provider stays usable, nobody is asked to sign in, and
  the state already known is kept, so a slow check doesn't flip the item. The agent log records it as
  `sign-in-check-inconclusive` with the cause (timed out, or the exit code). A signed-out answer from a status check
  counts only when the next check, a minute later, agrees; until then the known state is kept. A send that fails
  because of sign-in or a usage limit counts at once.
- On a Mac the service shows a notification and a dialog whose "Sign in" button opens Terminal running that command.
  AppleScript notifications can't carry an action of their own, so the dialog is the one-tap route.
- When the next check or send succeeds, the item is cleared and held deliveries go out.
- A session the sender can't reach (ended or closed) gets its message held as `claude-session-unreachable` and offered
  again every half hour.
- `IntelligenceX.Ledger.Hook inbox --config <reporter.json>`, run as a Claude Code SessionStart and UserPromptSubmit
  hook, hands the session whatever is waiting for it as hook context, begun before it prints and recorded as submitted
  after. It is a separate Native AOT executable that references only the contracts, so a run with nothing waiting takes
  about 0.1 s instead of the 1.5–4 s the full Ledger program needs to start. `IntelligenceX.Ledger inbox` runs the same
  code.

### Follow-ups, reviews and usage limits

These cover what sessions used to do through a separate Codex bridge plugin, so `ask` is the one way to hand work to
Codex and read its result.

- **Follow-ups keep the context.** `ask` with `continues: <earlier ask id>` runs on the machine that ran the earlier
  helper. The agent service forks that helper's thread (`thread/fork`) into a new workspace, so the new helper has the
  whole earlier conversation and the earlier thread stays as it was, archived or not. The earlier ask must be the same
  requester's, finished, and still in the live state (not yet archived). The service resolves the thread to fork from
  that ask's binding. `request_submit` can't name one.
- **Model and effort per ask.** `model` and `effort` override `ask.codexModel` and `ask.codexEffort` for that ask's
  turns.
- **Reviews.** `output: "review"` asks for a merge-gate review of the branch at the commit. The helper's final answer
  carries a verdict, findings (critical, high, medium, low) and the checks it ran. Only the final attempt of each check
  counts. The published result is JSON with `pass`, `verdict`, `failedChecks`, `summary`, `findings` and `validation`.
  `pass` is true only for `approve` with no critical or high finding and no failed final check. A review over 8 KB
  drops superseded check attempts, then the least severe findings, and says where the full review is.
- **Codex usage limit.** A helper turn that ends with `usageLimitExceeded` doesn't use up a resume or fail the
  request. The machine's `<machine>:sign-in:codex` item turns needs-you with reason `usage-limit`, so its user can sign
  in with another account (`codex login`). The grant stays renewed and the same thread is retried every
  `ask.usageLimitProbeMinutes`. The next completed turn clears the item.

### Wakes

A session that only has to wait (for CI, or for a reviewer's window) asks the Ledger to wake it and ends its turn.
`wake` takes `ci: "owner/repo#N"` or `afterMinutes`, plus the prompt the session wants to receive. The service reads
each watched pull request every minute through the same cached GitHub read as `pr_ready`, with no model involved. A
CI wake becomes due when the checks on the current head have finished (all passed, or failing with none pending) or
when the pull request is merged or closed. A head that moves during the read waits for the next pass. A timed wake is
due at its time. A CI wake still waiting after `ask.wakeExpiryHours` (72 by default) becomes due as expired, so the
session isn't left waiting silently.

The agent service on the session's machine delivers a due wake once: the reason first, then the session's own prompt,
through the same Codex app server and `SendMessage` routes as results. A delivery left sending past
`ask.deliveryUnconfirmedMinutes` becomes uncertain and is never resent. A newer CI wake for the same session and pull
request replaces a waiting one. A session client may hold `ask.maximumWakes` waiting wakes. `wake_cancel` cancels one
that hasn't started delivery. A Claude wake waits for sign-in and for an unreachable session the same way questions
do. Closed wakes are dropped after `retainFinishedDays`.

### Keeping the live state small

The coordination state is one document rewritten by every command. Requests that finished (accepted, result consumed,
helper retired) more than `retainFinishedDays` (policy, default 14) ago move to the `coordination_archive` table, with
their binding, result, retirement, questions, usage and an ask's own goal. A request still named by live work as a
dependency, predecessor or parent stays. Request views and usage totals read the archive, so nothing disappears from
them. Workers last observed before the same cutoff that nothing references are dropped.

Codex availability for the host heartbeat comes from `codex login status`, checked with sign-in, rather than from
starting an app server every minute.

### Known limits

- A Claude desktop session running in a different permission mode from the sender holds incoming SendMessage
  deliveries for its user's approval, and the notice of that hold goes to a sender that has already exited. The inbox
  hook records each session's permission mode from its prompts. `ask.claudeSenderMode` decides the sender's mode:
  `default` (the default) keeps the hold, and a delivery to a session in another mode is recorded as uncertain
  (`claude-may-be-held-for-approval`); `recipient` runs the sender in the session's recorded mode, so a delivery starts a
  turn at once, for installations where every session delivering through the Ledger is trusted. Either way a session
  with no recorded mode yet counts as uncertain. The recorded mode is the one the session had at its last prompt (or
  start): no hook fires when a session switches mode while idle, so a delivery right after such a switch can still be held
  while its receipt says submitted. The mode is taken from the session the delivery is for; the address it is sent to is
  the one that session gave for itself.
- A CI wake's reason names the pull request and how many checks failed, never the check names: those are text from
  GitHub that a workflow's author chooses. The woken session reads the failures from GitHub itself, as data.
- When the Codex app holds the thread, `codex queue --resume` stores the message and asks the local app-server daemon to
  run it. That is recorded as submitted, as is a message whose resume is refused because the app holds the thread: the
  app runs it as its next turn (`codex-queued-in-app`). Without a daemon (or with a CLI too old for `--resume`) the message only waits
  in the queue, which nothing may drain, so the delivery is recorded as uncertain (`codex-queued-not-resumed`), never as
  delivered, and isn't sent again.
- A helper runs for as long as it needs. Each heartbeat lists the grants of the helpers the host is running
  (`activeGrants`), and the service renews those grants, so a long build or test run never lapses while the helper
  and its host are alive. A grant ends only when its host stops reporting it (the helper or the host died), the host
  restarts, its source changes, or someone stops or cancels the work.
- Admitted work whose host never took it up (no binding, no execution) goes back to the queue when its grant lapses
  and is admitted again with a new grant. Hosts that don't send `activeGrants` keep the older never-created
  reconciliation.
- After a reboot, or when a host stops reporting a running helper, the helper is stopped and its work so far is
  returned as a failed result; it isn't resumed in place.
- Capacity is the machine's `agentSlots` in policy. Codex and Claude helpers share it, and each gets its own grant,
  thread and workspace, so concurrent helpers never share a checkout. The Macs run at most four helpers at once.
  The slots are a ceiling: while the machine's reporter shows memory pressure or sustained CPU, the heartbeat records
  a load hold, and a machine that already runs a helper admits no other (`machine-busy`). One with none still admits
  one, and load never stops a running helper.
- Workspace content left by a helper holds its retirement for review instead of being deleted.
