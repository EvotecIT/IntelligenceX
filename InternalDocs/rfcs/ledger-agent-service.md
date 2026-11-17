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
2. **Registers live sessions' inboxes.** Codex threads are reached through the Codex app server. A Claude Code session
   registers its documented cross-session inbox (`CLAUDE_CODE_MESSAGING_SOCKET`, and on Windows the session's
   messaging token) through a session-start hook that hands them to the local service over a private local channel.
   The token never leaves the machine or the service's private state.
3. **Takes requests for its machine** from the Ledger and holds the machine's lease while working.
4. **Runs them through official clients**: the Codex app server (`thread/start`, `turn/start`) or Claude Code
   (`claude -p` for new work; the session inbox for an existing session).
5. **Returns results and questions** to the Ledger, and **delivers** them to the requester: a Codex `turn/start` in
   the requesting thread, or a message into the requesting Claude session's inbox. A session that has ended gets the
   answer at its next start through the session-start hook, and Marquee shows it either way.

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

1. Prove delivery into a Claude session through its inbox on this PC, against the real client, and confirm the message
   format the documentation leaves out.
2. Requests between the Windows PC and the Mac mini through the services, with Codex as the runner, round trip both ways.
3. Claude as a runner and delivery into Claude sessions across machines.
4. The Mac Studio; Marquee's view of requests and answers.
5. Retire the Node coordination host and agent-to-agent issues.

## Not used

Hooks can't wake an idle session. Claude Code channels are a research preview and terminal-only. `claude -p --resume`
on a session the desktop app has open isn't documented as safe. Undocumented session files and keys are not read.
