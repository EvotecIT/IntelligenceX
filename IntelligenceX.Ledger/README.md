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
| `GET /status` | Versions, counts, expected machines and source coverage. |
| `GET /machines` | Healthy, silent and not-enrolled machine rows. |
| `GET /items` | Stable snapshot pagination, filtered by verdict, machine or provider. |
| `GET /items/{id}` | Verdict, reason, since, evidence and independent delivery-stage knowledge. |
| `GET /items/{id}/events` | Bounded event history in receipt order. |
| `GET /reports/daily?date=YYYY-MM-DD` | Observed UTC-day transitions and correctness marks. |

Requested, admitted, submitted, started, terminal and accepted stages are independent. A terminal result does not fill missing admission or requester-acceptance evidence. Unknown timestamps remain null. A delivery's target links back to its session item when known.

Work observations update independent dimensions. Null leaves a dimension unobserved; it does not erase an explicit wait, Goal hold, queue or application-steering requirement. Use false or zero to clear those observed conditions. Resolve an explicit wait with `resolved: true` and its matching kind/condition identity. Re-observing the same wait preserves its original deadline. Later direct evidence can fill an unknown field of the same delivery stage; the first receipt and the supplementary evidence remain linked.

Rules version 1 evaluates Needs you, Stuck, Waiting, Working, then Done. Other matching reasons remain in `alsoMatched`. Active turns become quiet after ten minutes and no-progress after thirty. Re-observation preserves `sinceUtc` while verdict and reason remain the same. Done is sticky until new work appears. A completed turn with an open PR creates a persisted review wait; repeated polling does not extend its deadline. An invisible application queue is a coverage note; known unaccepted submissions have their own rule.

## Project evidence and source coverage

Optional `gitHub` and `projectClaims` reporter settings use the SDK's existing credential resolver. Project configuration supplies the organization, project number, numeric field identities, machine aliases and inactive status values. These values belong in local configuration. The reader performs paginated GET requests and rejects incomplete or duplicate items and empty continuation pages. It retains no issue bodies.

The service's `projectEvidenceReporterId` selects the reporter allowed to supply central claim/PR evidence. Repository/branch associations are hashed consistently at each reporter. This lets the service apply central read evidence to sessions on other machines without copying GitHub credentials between hosts. Claim and PR facts carry separate source-check timestamps; a new combined observation does not renew cached evidence. Unavailable evidence preserves an established review deadline. Missing decisive evidence becomes Stuck/unknown with `missingFacts`; general coverage notes stay in status.

Known PR links survive later empty search results. The reporter reads exact lifecycle metadata for links found in the open authored/review-requested search and configured project fields; an absent search result does not establish closure. Multiple project rows for one session contribute all their links, and an uninterpretable field keeps association coverage incomplete. Positive branch and item associations persist during event admission, including when several retained snapshots arrive before evaluation.

The local PR catalog holds up to 1,000 refreshable identities. Saturation reports a PR-evidence gap while existing reads, session collection and delivery continue. Open and closed links remain refreshable; immutable merged entries retire only after their proof is durably queued in the outbox, making room for new identities. Up to 100,000 compact admission records prevent rediscovered merged links from repeatedly occupying that capacity. At that bound, remaining entries stay retained and saturation remains explicit. Failed reads retain the previous proof's check time. Item associations and positive timestamps survive a session changing branches.

`evidenceFile` accepts normalized, metadata-only `LedgerEvent` arrays from a local read-only snapshot export, up to 16 MiB. This is suitable for existing delivery and intake records. Publish each snapshot atomically and retain its source records until admission completes. Import progress is checkpointed independently of outbox acknowledgements, so a partially admitted snapshot resumes after restart. Preserve the original observation times, leave unavailable stage times null, and include controlled cause codes rather than raw error text. Snapshot import does not install or change its source. Native Goal or application-queue state requires affirmative structured evidence; session activity and an unavailable live-control route do not establish either state.

## Optional low-latency hints

`hook --config reporter.json` reads one bounded JSON payload from stdin and appends a metadata-only hint. `notify --config chain.json <payload>` first invokes the exact configured incumbent command with the original payload, then appends the hint. Chain configuration contains `incumbent` (an argument array) and `reporterConfig` (the reporter configuration path). The incumbent exit status is preserved. A Stop or tool call is a reconciliation hint and does not establish task completion or a pending question.

Normal service and reporter startup does not register client hooks or change client settings. Review the local configuration merge when adding hint commands. Keep existing hooks and notification behavior intact.

## Shadow reporting and another machine

The service writes current and previous UTC-day reports under `dataDirectory/shadow`, updating files when their substantive content changes and finalizing receipt coverage at UTC-day close. The read API generates fresh coverage on request. Each transition has a stable ID, verdict, reason, since, evidence and an initially unmarked correctness field. Save a right/wrong mark through the typed review event or:

```text
ix ledger mark <transition-id> right --config /absolute/path/reporter.json
```

This command uses the writer configuration; regenerating a report preserves stored marks. Reports cover observed history and do not imply a complete pre-enrollment transition series.

For a Mac or Linux reporter, publish for its runtime identifier, copy the output to that user's application-data directory, create a private reporter configuration pointing at the service's HTTPS LAN endpoint, and run `report --config ...`. For example, an Apple Silicon build uses `dotnet publish -c Release -f net10.0 -r osx-arm64 --self-contained false`. Keep the spool on local disk. Verify a fresh machine heartbeat and an acknowledged outbox before treating enrollment as complete. A running process or an open TCP port alone does not establish reporting. Record an unavailable host or launch-mode/network restriction explicitly, with the retained spool and the next supported local launch step.
