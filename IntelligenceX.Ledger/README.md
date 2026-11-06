# Local work ledger

Ledger collects metadata from local Codex and Claude artifacts and evaluates Working, Waiting, Needs you, Stuck and Done. One .NET 10 HTTP service stores append-only observations and current rows in SQLite through DbaClientX.SQLite. Reporters run with the signed-in user's read access to their session files. CLI, tray and tile clients consume the same read model.

Collection reads provider artifacts. Event ingestion, receipts and correctness marks write Ledger's own store. Keep configurations, tokens, certificate files, spools, reports and databases outside source control.

## Run the service and reporter

Build `IntelligenceX.Ledger.csproj` for .NET 10. A framework-dependent deployment needs the .NET 10 and ASP.NET Core runtimes. The same assembly provides these entry points:

```text
dotnet IntelligenceX.Ledger.dll serve --config /absolute/path/service.json
dotnet IntelligenceX.Ledger.dll report --config /absolute/path/reporter.json
```

The service configuration requires an absolute `dataDirectory`, different random `readToken` and `writeToken` values of at least 32 characters, and `expectedReporters`, a map from machine IDs to their reporter IDs. Reporter IDs are unique across machines. Generate at least 256 bits of randomness for each token. `port` defaults to 7348. The service binds IPv4 loopback; `lanAddress` adds one explicitly selected private address on a local interface. LAN binding requires `certificatePath` and its `certificatePassword`. The configured server PFX is loaded directly; certificate-store discovery is not involved.

The reporter configuration requires `endpoint`, `token` (the write token), `machineId`, `reporterId` and an absolute `spoolDirectory`. For a private certificate, configure its exact DER certificate SHA-256 fingerprint as `certificateSha256`. HTTPS is required outside loopback. Redirects are disabled and Ledger connections use the configured endpoint directly. Protect these files using the account's normal private application-data permissions.

An expected machine with no heartbeat is `not-enrolled`. A reporter silent for ten minutes makes its machine Stuck; item rows retain their last-known state. Startup and partial-source diagnostics appear separately in status. Local-only automatic discovery includes new source roots during reconciliation without relabeling recovered history as local activity.

The bounded outbox defaults to 64 MiB. Events have stable IDs and per-epoch sequences. Only acknowledged IDs leave the outbox. Replaying the same observation is idempotent; conflicting ID or sequence reuse is rejected. Capacity exhaustion retains unacknowledged files and reports backpressure while delivery keeps draining. Uploads respect both the 100-event and 1-MiB ingress limits. Source logs remain the reconciliation backstop. Hook hints share the outbox and cannot take its delivery lock.

## Read the ledger

The CLI output directory includes `ix.cmd` for Windows and `ix` for POSIX shells. Keep the launcher beside `IntelligenceX.Cli.dll` and its runtime dependencies. A client configuration contains `endpoint` and `readToken`; the environment alternatives are `IX_LEDGER_ENDPOINT` and `IX_LEDGER_READ_TOKEN`.

```text
ix ledger status --config /absolute/path/client.json
ix ledger needs-you --config /absolute/path/client.json
ix ledger stuck --config /absolute/path/client.json
ix ledger item <ledger-item-id> --config /absolute/path/client.json
ix ledger report 2026-01-12 --config /absolute/path/client.json
```

Lists expose `nextCursor`. Pass it with `--cursor` before `--config` to continue the same snapshot. Cursors expire after five minutes; start a new list if a cursor expires. Native session IDs, when observed, are included as identity metadata; messages, prompts, answers, tool arguments and raw hook payloads are excluded.

All HTTP routes below use `/api/ledger/v1` and require a bearer token. GET routes require the read token; POST requires the write token.

| Route | Result |
| --- | --- |
| `POST /events` | Acknowledges up to 100 typed events after history and projection commit together. |
| `GET /status` | Versions, verdict counts, quiet paused count, expected machines and source coverage. |
| `GET /machines` | Healthy, silent and not-enrolled machine rows. |
| `GET /items` | Stable snapshot pagination, filtered by verdict, machine or provider. |
| `GET /items/{id}` | Verdict, reason, since, evidence and independent delivery-stage knowledge. |
| `GET /items/{id}/events` | Bounded event history in receipt order. |
| `GET /reports/daily?date=YYYY-MM-DD` | Observed UTC-day transitions and correctness marks. |

Requested, admitted, submitted, started, terminal and accepted stages are independent. A terminal result does not fill missing admission or requester-acceptance evidence. Unknown timestamps remain null. A delivery's target links back to its session item when known.

## Shared views and project context

`GET /api/ledger/v1/work` returns current work, separate from all-history counts.
Current work includes claims, explicit holds, open unarchived turns, current
declarations, and activity or deliveries within the last day. Archived sessions
with active claims remain visible. The list contains up to 200 rows, with full
current-work counts and a `truncated` flag. `verdict` filters returned rows;
`limit` bounds their number from 1 to 200. Counts retain the full current scope.
Verdicts come from the existing rules engine. The view retains its `asOfUtc`
while cached for at most ten seconds; admitted events invalidate that cache.

Marquee's Agents settings accept a path to the local read configuration. When
configured, the view reads this projection, including machine coverage and item
evidence. The IX tray's Work button opens the same projection. The tray uses
`IX_LEDGER_READ_CONFIG` or the user's local application-data
`IntelligenceX/Ledger/client.json`; its Read settings button chooses another file.
An unavailable service is displayed explicitly alongside any retained observation.
Archive observations and usage limits do not establish application responsiveness;
unobserved health remains unknown.

Set `projectCatalogPath` in the service configuration to an absolute path to a
private JSON catalog. The catalog's `schemaVersion` is 1 and `observedAtUtc`
records the directory source observation. Each `projects` entry contains `id`,
`name`, `repository`, `revision`, `responsibilities`, `dependencies`, and `sources`.
Responsibility and dependency facts carry their own observation times and HTTPS
`sourceRef` links. A source contains `id`, `title`, an absolute local `path`, an
HTTPS `sourceRef`, and an `outcome` of `reference`, `accepted`, `rejected`, or
`superseded`. Optional `verifiedAtUtc` and `supersededBy` preserve review history.
Sources with `supersededBy` are excluded from search. Optional `workLabels` map
exact Ledger item IDs to display labels and enrolled project IDs.

Enrollment grants the read-token audience access to the selected sources. Keep
the catalog, private documents and credentials outside the repository. Use
project-owned maintained documents; do not enroll raw transcripts or credential
files. Linked source paths require separate enrollment and are refused. Catalogs
are bounded to 1 MiB; sources to 1 MiB each and 16 MiB in total. Refresh occurs
on demand after thirty seconds. Removed or unavailable sources stop contributing
hits, with explicit coverage. A failed directory load remains visible in discovery;
exact reads return 503 until the service can establish project membership.

```text
ix ledger work --config /absolute/path/client.json
ix ledger projects --config /absolute/path/client.json
ix ledger context sample-project "export allocation" --config /absolute/path/client.json
ix ledger search sample-project "rejected experiment" --config /absolute/path/client.json
```

Authenticated reads use `GET /api/coordination/v1/projects`,
`GET /api/coordination/v1/projects/{id}?query=topic`, and
`GET /api/coordination/v1/knowledge?project=id&query=topic`. Discovery returns a
directory envelope with `asOfUtc`, `projects`, and `coverage`. Exact project
context includes up to two excerpts; search returns up to five. Every excerpt
includes a source link, content SHA-256 revision, section, line and outcome.
These are historical source facts, not proof of a current release or deployment.
Responsibility evidence older than ten minutes is identified as stale.

The .NET 10 `IntelligenceX.Ledger.Mcp` stdio host uses the official MCP SDK:

```text
dotnet IntelligenceX.Ledger.Mcp.dll --config /absolute/path/client.json
```

Its read-only tools are `project_context`, `knowledge_search`, `work_status`, and
`work_item`. Omit the project in `project_context` to discover enrolled projects.
`work_status` returns counts, machine coverage and at most eight compact rows;
an optional verdict selects a state. Use `work_item` for full evidence. Retrieval
and state evaluation remain service-owned and make no model calls. These tools
grant no execution or publication authority. Register the host through the
client's supported MCP settings after reviewing the exact configuration change.

## Online backup and restore

An operator can request a DbaClientX online snapshot while the service runs:

```text
dotnet IntelligenceX.Ledger.dll backup --config /absolute/path/service.json --out /absolute/path/backups/ledger.sqlite
```

The destination directory must exist and both the destination and its `.json`
manifest must be new. A unique staging copy is integrity-checked and hashed,
then promoted without overwriting an existing destination. The manifest records
the included event ordinal, event/item counts, SHA-256, size and measured time.
A database without its matching manifest is incomplete; verify the hash before
using a backup. Do not treat manifest publication as an atomic two-file operation.

Retain the protected local configuration, certificate references, catalog and
source enrollment needed to rebuild the service separately. For a restore drill,
copy the verified database into a fresh data directory, configure a separate
loopback port, and inspect its reads before switching any reporter. A real host
migration requires stopping and verifying the old writer before activating the
new writer, then reconciling reporter spools and external receipts. A restored
ordinal is a recovery point; later events require reconciliation. Never run two
service writers against one database or put the SQLite file on network storage.

Work observations update independent dimensions. Null leaves a dimension unobserved. Use false or zero to clear an observed hold or queue. Resolve an explicit wait with `resolved: true` and its matching kind/condition identity. Re-observing the same wait preserves its original boundary. Later direct evidence can fill an unknown field of the same delivery stage. The first receipt and supplementary evidence remain linked.

Rules version 2 applies task generation, turn/condition scope, supersession and native controls before evaluating Needs you, Stuck, Waiting, Working and Done. Each match carries its own episode, scope and evidence. `sinceUtc` follows that episode, including a new occurrence of the same reason. `lastProgressAtUtc` retains observed work activity independently. `alsoMatched` keeps the compact reason list, and `matches` supplies the corresponding details.

Undeclared active turns become quiet after ten minutes and no-progress after thirty. A declared operation uses its own `reviewAfterUtc`. Waits distinguish an actual `dueAtUtc` from a dependency review time. A passed review time produces `wait-review`. A passed deadline produces `wait-overdue`. A completed turn with an open PR owned by its task gets an inferred 24-hour review wait. Each owned PR retains its original review clock. Branch association alone supplies correlation evidence.

Deliberate native Stop, pause and budget controls produce a quiet `paused-by-user` hold. Queued work behind the hold produces a warning and remains included in the paused count. Submission without an observed start becomes Stuck/`delivery-uncertain` after ten minutes. Needs you/`needs-app` requires both app ownership and evidence that human steering is the next step. Unconfirmed input stays explicit. Optional questions have their own item with `parentItemId`. A qualified parent task or turn supersession retires obsolete questions as cancelled while retaining their evidence. Done requires clear delivery and queue gates, and closed unmerged work is `cancelled`.

Items expose `hold`, `nextActor`, `acceptanceBy`, `resultRef` and a read-only `wakeEligibility` explanation. Unavailable metadata remains null or explicitly unknown. Eligibility includes eligible, suppressed or unknown, with its reason, condition and evidence. Versioned urgent-safety facts carry the matching policy, source reference and any required manual action. Phase 1 evaluates and reports these observations.

## Declare a work boundary

Worker declarations use the existing typed events endpoint and durable reporter outbox. A declaration names its immutable fact `id`, stable condition `episodeId`, verified `scope` (`taskId`, increasing `generation` and required `turnId`), `checkpointRef`, `conditionRef`, `kind`, `nextActor`, `blocking`, `recordedAtUtc` and at least one review time or deadline. Kinds are `wait`, `operation`, `question` and `handoff`. References are opaque identifiers. Use `nextActor: human` only when direct evidence requires a person to act. Include `acceptanceBy`, `resultRef`, `candidateRef` and the typed hold when observed.

```text
ix ledger declare codex <source-session-id> /absolute/path/declaration.json --config /absolute/path/reporter.json
```

The command requires an explicit native session UUID and observed turn identity. It derives the same session key as the watcher and appends a deterministic event without opening a connection. Repeating the same declaration preserves its receipt and review time. Changed content requires a new fact ID. Checkpoint updates and resolution retain the original episode ID. A new retry uses a new episode ID. Resolutions target their exact task, turn, candidate and condition occurrence. A late resolution for an older occurrence leaves the current occurrence intact. An optional question is stored under its own input key, leaving the parent's work state independent.

Append `--validate` to check a declaration through the shared contract validator before outbox admission. This supports producers that retain immutable retry inputs after validation.

Task generations increase only at a verified task boundary. Reporter epochs describe transport sessions. Native controls arrive as separate observations. Automatic producers can submit the same declaration envelope through their existing supported wait or handoff integration.

## Project evidence and source coverage

Optional `gitHub` and `projectClaims` reporter settings use the SDK's existing credential resolver. Project configuration supplies the organization, project number, numeric field identities, machine aliases and inactive status values. These values belong in local configuration. The reader performs paginated GET requests and rejects incomplete or duplicate items and empty continuation pages. It retains no issue bodies.

Configure `projectClaims` on the single reporter named by the service's `projectEvidenceReporterId`. That reporter publishes shared project and pull-request evidence. Other reporters may enable `gitHub` for their local work-state observations without publishing global evidence. Watchers refresh full directory membership at least every five minutes, including when filesystem notifications are unavailable or directory timestamps remain unchanged.

The service's `projectEvidenceReporterId` selects the reporter allowed to supply central claim/PR evidence. Repository/branch associations are hashed consistently at each reporter. This lets the service apply central read evidence to sessions on other machines without copying GitHub credentials between hosts. Claim and PR facts carry separate source-check timestamps. Unavailable evidence preserves an established review time. Missing decisive evidence becomes Stuck/unknown with `missingFacts`. General coverage notes stay in status.

Known PR links survive later empty search results. The reporter reads exact lifecycle metadata for links found in the open authored/review-requested search and configured project fields. Multiple project rows for one session contribute all their links, and an uninterpretable field keeps association coverage incomplete. Positive branch and item associations persist during event admission, including when several retained snapshots arrive before evaluation. For a scoped task, ownership evidence carries the matching task ID and generation. Session associations remain available as correlation evidence across generations. Unqualified facts preserve the distinction, and clearing a qualified hold or wait requires its matching task scope.

The local PR catalog holds up to 1,000 refreshable identities. Saturation reports a PR-evidence gap while existing reads, session collection and delivery continue. Open and closed links remain refreshable; immutable merged entries retire only after their proof is durably queued in the outbox, making room for new identities. Up to 100,000 compact admission records prevent rediscovered merged links from repeatedly occupying that capacity. At that bound, remaining entries stay retained and saturation remains explicit. Failed reads retain the previous proof's check time. Item associations and positive timestamps survive a session changing branches.

`evidenceFile` accepts normalized, metadata-only `LedgerEvent` arrays from a local read-only snapshot export, up to 16 MiB. This is suitable for existing delivery and intake records. Publish each snapshot atomically and retain its source records until admission completes. Import progress is checkpointed independently of outbox acknowledgements, so a partially admitted snapshot resumes after restart. Preserve the original observation times, leave unavailable stage times null, and include controlled cause codes rather than raw error text. Snapshot import does not install or change its source. Native Goal or application-queue state requires affirmative structured evidence; session activity and an unavailable live-control route do not establish either state.

## Optional low-latency hints

`hook --config reporter.json` reads one bounded JSON payload from stdin and appends a metadata-only hint. An existing notifier can forward its payload as the final argument with `hook --config reporter.json <payload>`. This lets that notifier retain its incumbent callback while Ledger records the hint.

`notify --config chain.json <payload>` first invokes the exact configured incumbent command with the original payload, then appends the hint. Chain configuration contains `incumbent` (an argument array) and `reporterConfig` (the reporter configuration path). The incumbent exit status is preserved. A Stop or tool call is a reconciliation hint and does not establish task completion or a pending question.

Normal service and reporter startup does not register client hooks or change client settings. Review the local configuration merge when adding hint commands. Keep existing hooks and notification behavior intact.

## Shadow reporting and another machine

The service writes current and previous UTC-day reports under `dataDirectory/shadow`, updating files when their substantive content changes and finalizing receipt coverage at UTC-day close. The read API generates fresh coverage on request. Each transition has a stable ID, verdict, reason, since, evidence and an initially unmarked correctness field. Save a right/wrong mark through the typed review event or:

```text
ix ledger mark <transition-id> right --config /absolute/path/reporter.json
```

This command uses the writer configuration; regenerating a report preserves stored marks. Reports cover observed history and do not imply a complete pre-enrollment transition series.

For a Mac or Linux reporter, publish for its runtime identifier, copy the output to that user's application-data directory, create a private reporter configuration pointing at the service's HTTPS LAN endpoint, and run `report --config ...`. For example, an Apple Silicon build uses `dotnet publish -c Release -f net10.0 -r osx-arm64 --self-contained false`. Keep the spool on local disk. Verify a fresh machine heartbeat and an acknowledged outbox before treating enrollment as complete. A running process or an open TCP port alone does not establish reporting. Record an unavailable host or launch-mode/network restriction explicitly, with the retained spool and the next supported local launch step.
