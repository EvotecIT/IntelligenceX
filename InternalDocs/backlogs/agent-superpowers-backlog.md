# Agent Superpowers Backlog (Ops + Tooling)

This backlog is focused on making the IntelligenceX chat agent behave like a reliable "ops colleague":
it should execute tools deterministically, be explicit about scope (domain vs forest, local vs remote),
and never claim work was done unless tool outputs exist.

Conventions:
- Each item should be small enough for one PR.
- Tags: (P0/P1/P2), (runtime-parallel/dev-parallel), (dangerous), (docs/tests).
- Prefer structured intent (actions/ids) over natural-language phrase matching.

## North Stars
- [ ] (P0) Tool execution is self-verifying (assistant cannot claim "I ran X" without tool outputs attached).
- [ ] (P0) Scope is explicit in every result (domain vs forest, DC list, discovery method, time window).
- [ ] (P0) Capability negotiation is automatic (agent knows what it can/can't do in this session and offers the next-best action).
- [ ] (P1) "One click to run" workflows exist for common admin tasks (replication, LDAP, event logs, services).
- [ ] (P1) Results are table-ready and exportable (stable columns + types, plus a human summary).

## Engine + Chat Orchestration
- [ ] (P0) Add "tool receipt" enforcement: if no tool outputs exist in the turn, prohibit language like "I ran/checked/executed".
- [ ] (P0) Add a capability handshake step at session start (enabled packs/tools, allowed roots, remote reachability mode).
- [ ] (P0) Add a "scope label" block to tool-derived answers (domain/forest, DCs contacted, time window, caveats).
- [ ] (P0) Make pending-action confirmation language-agnostic by preferring `ix_action_selection` payloads and ordinals.
- [ ] (P0) If the agent asks for a required input (DC FQDN, EVTX path), auto-generate actions that populate that input.
- [ ] (P1) Add a small "confirm intent" classifier for ambiguous follow-ups only when pending actions exist (avoid hardcoded English lists).
- [ ] (P1) Add a "plan preview" mode (show which tools will run, in what scope) before executing (optional).
- [ ] (P1) Add runtime parallelization policy: declare tool calls parallel-safe and run them concurrently when possible. (runtime-parallel)
- [ ] (P1) Add a tool-call scheduler with concurrency groups (per-host/DC throttles, global caps). (runtime-parallel)
- [ ] (P2) Add a background jobs model for long-running probes (queued, progress, result-ready callback). (runtime-parallel)

## Event Logs (EventViewerX / PSEventViewer)
Problem statement: the agent can sometimes confirm a remote channel exists (System/Security) but cannot read entries because
the session doesn't expose live query/stat tools or EVTX query tools.

- [ ] (P0) Ensure EventLog tool pack exposes live query and stats tools when enabled (top-N events, filter, time window).
- [ ] (P0) Add a single "top events" tool (channel, provider, level, since, max events) with compact JSON output.
- [ ] (P0) Add an EVTX-local query workflow: find EVTX, query, and summarize (no remote assumptions).
- [ ] (P0) Add explicit "remote vs local" error envelopes with next actions (export EVTX, enable live query, provide creds).
- [ ] (P1) Add preset reports for common AD/DC troubleshooting (DFSR/FRS, replication failures, Kerberos, DNS, Netlogon).
- [ ] (P1) Add correlation helpers (lockout -> preceding 4625, replication failure -> last success, etc.). (runtime-parallel)
- [ ] (P2) Add path allowlist friendly scanning for likely EVTX export locations with user-configurable roots.

## ADPlayground + TestimoX (Forest-Level Reality)
Problem statement: forest-wide checks can silently degrade to domain-only when discovery is incomplete. The agent must be explicit
about what scope it actually achieved, and offer the correct next step.

- [ ] (P0) Add an explicit forest probe tool that enumerates forest, domains, trusts, and DCs (include_trusts default true for "forest").
- [ ] (P0) Add a replication summary that can run at forest scope and returns a per-domain/per-DC breakdown.
- [ ] (P0) Add a "scope discovery" tool that returns: forest name, domains, DCs, naming contexts, and why anything is missing.
- [ ] (P0) Add a "probe receipt" output (which DCs were contacted, which endpoints checked, retries, timeouts).
- [ ] (P1) Add TestimoX monitoring probe wrappers with consistent output schemas (rows+cols+types) and severity mapping.
- [ ] (P1) Add "forest-wide bundle" meta-tool: replication + LDAP + SYSVOL/DFSR evidence + event log hints. (runtime-parallel)
- [ ] (P2) Add a "minimal anchor DC" mode (user provides one DC, tool discovers the rest) with clear fallback reasoning.

## ComputerX (System Admin Superpowers)
- [ ] (P0) Add a read-only "services health bundle" tool (top failures, stopped-but-should-run, recent restarts). (runtime-parallel)
- [ ] (P0) Add a read-only "process snapshot + hot processes" tool (CPU/RAM, top N, suspicious flags).
- [ ] (P1) Add registry read tools for common diagnostics (policy keys, TLS, WinRM, Schannel). (docs/tests)
- [ ] (P1) Add network diagnostics bundle (DNS resolve, TCP connect, route, firewall profile) with clear error codes.
- [ ] (P2) Add dangerous write tools only behind explicit pack flags (service restart, process kill, registry write). (dangerous)

## Tool Pack UX + Documentation
- [ ] (P0) Add a "what I can do right now" tool (lists enabled packs/tools + examples).
- [ ] (P0) Standardize tool output envelopes (summary + data rows + typed errors + hints).
- [ ] (P1) Add examples for "remote event logs" flows: live query enabled vs EVTX export path.
- [ ] (P1) Add examples for "forest-wide" flows: success vs degraded discovery and how to fix it.
- [ ] (P2) Add tool-level "explain" endpoints (examples, common filters, expected runtime, output schema).

## Parallelizable Work (Dev Plan)
- [ ] (dev-parallel) EventLog: live query + top-N tool + error envelopes.
- [ ] (dev-parallel) ADPlayground/TestimoX: forest discovery + scope receipts + forest bundle.
- [ ] (dev-parallel) ComputerX: services/process/network bundles (read-only first).
- [ ] (dev-parallel) Chat engine: tool receipt enforcement + capability handshake + scheduler.
- [ ] (dev-parallel) Docs: remote/local event logs playbooks + forest-wide playbooks.

