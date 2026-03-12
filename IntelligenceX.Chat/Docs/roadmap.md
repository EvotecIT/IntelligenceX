# IntelligenceX.Chat Roadmap / TODO

This is an end-to-end checklist for getting from today’s REPL into a usable Windows tray app with strong AD + Event Log tooling.

Conventions:
- `[x]` done
- `[ ]` not done

## Milestone A: Foundations (Build, Contracts, Safety)
- [x] Host REPL exists (`IntelligenceX.Chat.Host`)
- [x] ChatGPT OAuth login cached (no re-login every run)
- [x] OpenAI native tool schema uses nested function tools by default (fixes `tools[0].parameters` rejection)
- [x] Tool id display is human-friendly by default, raw ids optional
- [x] Live progress output in REPL (thinking, tool calls, running tools)
- [x] Service emits progress events (`chat_status`) for UI (thinking, tool calls, tool running/completed)
- [x] System prompt file (`Docs/HostSystemPrompt.md`) and `--instructions-file`
- [x] Read-only stance documented (no implied writes)
- [x] Define “capability tiers” as a first-class concept
- [x] Add a “policy banner” to every session: read-only, allowed roots, enabled packs, enabled dangerous tools
- [x] Define stable typed tool error envelope for tool-runner failures (`ok=false`, `error_code`, `error`, `hints`, `is_transient`)
- [x] Add tool paging contract (cursor/offset) for list tools that can return thousands of rows
- [x] Add per-tool timeouts + a global turn timeout (host/service configurable)
- [x] Codify engine-first layering rules in docs: Chat orchestrates, Tools expose schemas, engines do AD/EventLog/System domain logic (`Docs/engine-catalog.md`)
- [ ] Add a lightweight background job model for long-running tools (queued jobs, progress, “result ready” callbacks)
- [x] Add a session/profile persistence store (SQLite via `DbaClientX`) for user prefs and defaults (`--profile`, `--save-profile`, `--state-db`)
- [ ] Add memory/persona persistence (persona templates, per-org defaults, opt-in retention policy)

## Milestone B: Tool Pack UX (Less Calls, More Signal)
- [x] AD groups listing supports `*` + prefix batching + truncation indicators
- [x] AD expired users tool (`ad_users_expired`) avoids “never expires” false positives (engine: `ADPlayground.DirectoryOps.ExpiredUsersService`)
- [x] AD resolved group members tool (`ad_group_members_resolved`) avoids N-per-member lookups
- [x] AD replication summary tool (`ad_replication_summary`) for repadmin-like replication health
- [x] EVTX Security report tools (4624/4625/4634/4647, 4625, 4740)
- [x] EVTX security reports use EventViewerX AD rule wrappers (typed extraction, AOT-friendly)
- [x] EventViewerX 4740 lockout caller is fixed and trimmed
- [x] Add a single “Domain Admins summary” tool: members resolved, users-only option, computers-only option, nested option, and quick risk flags
- [x] Add AD “object resolve” batch tool (input: list of DNs/SIDs; output: objects) for generic de-N+1 patterns
- [x] Add AD “search with facets” tool (e.g. `by_ou`, `by_enabled`, `by_uac_flags`, `by_pwd_age_bucket`)
- [x] Add LDAP diagnostics tool (`ad_ldap_diagnostics`) to test LDAP/LDAPS/GC ports, binds, and certificate health
- [ ] Add “explain” metadata endpoint per tool (examples, common filters, output schema summary)
- [x] Add host-side response shaping options: `--max-table-rows`, `--max-sample`, `--redact`
- [ ] Standardize a “human summary + optional table” response pattern across packs (agent chooses level of detail, user can ask for raw)
- [ ] Add “exploratory query” affordances: safe defaults, attribute allowlists, paging, and follow-up suggestions for refining queries

## Milestone C: Active Directory Coverage (Admin-First)
- [x] Basic LDAP query tool (`ad_ldap_query`)
- [x] Basic search/object-get tools
- [x] SPN search/stats tools
- [x] Delegation audit tool
- [x] Privileged groups summary tool
- [x] Stale accounts tool (engine: `ADPlayground.DirectoryOps.StaleAccountsService`)
- [ ] Add “account health” tools
- [ ] Add “GPO / policy relevant” tools (or bridge to TestimoX where that’s the source of truth)
- [ ] Add “Tier0 hygiene” tools (privileged groups, DCs, PAWs, LAPS signals, service accounts)
- [ ] Add `userAccountControl` decoding as typed flags in outputs (not raw integers only)
- [ ] Add consistent time conversions in outputs (FILETIME to ISO-8601 UTC alongside raw)
- [ ] Add OU scoping defaults (smart base DN selection for common queries if user provides OU in text)
- [x] Migrate AD tools to use `TestimoX/ADPlayground` as the engine of record (no direct LDAP protocol code in Tools)

## Milestone D: Event Logs Coverage (Detect + Explain)
- [x] EVTX query tool
- [x] EVTX stats tool
- [x] EVTX Security reports (logons/failed/lockouts)
- [ ] Add “report packs” that mirror the EventViewerX built-in reports (where it’s stable and typed)
- [ ] Add “live Security log” equivalents for the key reports (read-only)
- [ ] Add time-window presets (last 1h/24h/7d) and timezone handling (input local, store UTC)
- [ ] Add correlation helpers for common cases (lockout -> preceding 4625 by IP/workstation)
- [ ] Add output schemas that are table-ready (rows + columns + types) for UI rendering

## Milestone E: System / File / Email Packs (Useful Defaults)
- [x] System pack registered (services, scheduled tasks)
- [x] File system tools under allowed roots
- [x] Mailozaurr sources integrated in Tools repo
- [ ] Email: enforce “dangerous/write pack” for SMTP send
- [ ] Email: read-only IMAP search/get hardening (rate limits, size caps, attachment handling)
- [ ] File: add structured “findings” modes (IOC search, known file patterns) in addition to raw search
- [ ] System: add Event Log channel/provider discovery flows for troubleshooting

## Milestone F: OfficeIMO Pack (Read-First, Write-Optional)
Goal: let the assistant parse and generate Office artifacts (Excel/Word/PowerPoint/CSV) using OfficeIMO.

- [ ] Add `IntelligenceX.Tools.Office` (or `IntelligenceX.Tools.OfficeIMO`) project
- [ ] Add read-only tools
- [ ] Add “dangerous/write” tools gated behind an explicit pack flag
- [ ] Add allowed-roots enforcement for Office file paths
- [ ] Add normalization rules for tabular outputs (Excel/CSV -> rows + columns + types)
- [ ] Add deterministic output modes (no random sheet names, stable column ordering)

Suggested tool ids:
- [ ] `office_excel_read`
- [ ] `office_excel_list_sheets`
- [ ] `office_word_extract_text`
- [ ] `office_powerpoint_extract_text`
- [ ] `office_csv_read`
- [ ] `office_excel_write` (dangerous)
- [ ] `office_word_write` (dangerous)
- [ ] `office_powerpoint_write` (dangerous)

## Milestone G: Real App (WinUI 3 Tray) MVP
Definition of done: you can install/run the tray app, chat, see tool traces, and export tables.

- [ ] WinUI 3 tray app skeleton (process model: UI + service)
- [ ] Service lifecycle management (start/stop/restart, health, logs)
- [ ] Chat timeline UI (streaming deltas)
- [ ] Tool trace UI (tool name, args, duration, result preview, errors)
- [ ] Table rendering (WebView2 or native grid) with copy/export
- [ ] Session profiles (allowed roots, packs enabled, AD DC/base DN defaults)
- [ ] Logs and diagnostics panel (copyable)

## Milestone H: Extensibility (Plugins, Meta-Tools, Policies)
- [ ] Tool pack discovery + versioning (explicit enable/disable)
- [ ] “Dangerous tools” workflow
- [ ] Meta-tools for orchestration (batch, paginate, correlate) without reflection-heavy dispatch
- [ ] Plugin sandboxing boundaries (process isolation for risky plugins)
- [ ] Policy engine for tool allow/deny based on path/OU/tool id/time

## Milestone I: Engineering Quality (Typed, AOT-Friendly, Testable)
- [x] Nullable enabled, warnings as errors, XML docs (where code exists)
- [x] Keep files reasonably sized (target <= ~700 LOC)
- [x] Add an OfficeIMO markdown package-mode CI gate so package adoption is validated without a local OfficeIMO checkout
- [ ] Prefer enums/flags over magic strings in tool outputs where it’s stable
- [ ] Avoid reflection in hot paths where possible (AOT-friendly patterns)
- [ ] Add unit tests for tool filters and edge cases (accountExpires, nested groups, paging)
- [ ] Add golden tests for report tools (input EVTX -> stable JSON shape)
- [ ] Add performance budgets for “list” tools (avoid N+1; cap results; paging)

## Next 2-3 Steps (Recommended)
- [ ] Update Host prompt to prefer `ad_users_expired` and `ad_group_members_resolved` (verify behavior with a few interactive prompts)
- [x] Add a Domain Admins summary tool (resolved, users-only option) to reduce friction further
- [ ] Start the WinUI tray MVP with tool trace UI (so “I can start chatting” is the normal path, not REPL) (wait for WinUI to stabilize if needed)
