# Local work state and live updates

`IntelligenceX.Telemetry.Agents` provides provider-neutral, read-only work state for native applications. Snapshots carry session/provider/root identifiers, explicit titles, workspace, repository, branch, machine, timestamps, state, attention and PR/check links. Messages, prompts, answers, tool arguments, PR bodies and credentials are not projected or retained in parsed caches. Titles and paths can still be private.

## Native library consumption

Use `AgentWorkWatcher` for a live strip. Its default activity window is two days. `ActiveWithin` excludes session files by modification time before opening or parsing them, including before deserializing their cached state. The collector enumerates paths and file metadata; it does not parse every historical session. Provider activity timestamps apply a second filter, so copying an old transcript does not make the conversation recent. Metadata without a known unfinished turn does not establish Running.

```csharp
using IntelligenceX.Telemetry.Agents;

await using var watcher = new AgentWorkWatcher(options: new AgentWorkOptions {
    ActiveWithin = TimeSpan.FromDays(2)
});
watcher.SnapshotChanged += (_, change) => {
    // Marshal change.Snapshot to the application's UI thread.
};
await watcher.StartAsync(cancellationToken);
var first = watcher.CurrentSnapshot;
```

Keep the watcher for the application's lifetime. `StartAsync` awaits the first local snapshot on a background worker; it does not await optional GitHub or Desktop metadata. Events run on the background collection worker, so handlers should return promptly. Subscriber exceptions are isolated and produce `subscriber-failed`. `DisposeAsync` cancels and awaits owned workers and releases filesystem handles. Synchronous `Dispose` requests that teardown without blocking the caller.

The watcher queues the file paths named by `FileSystemWatcher` and coalesces bursts for one second. Existing files update their own parsed observations without enumerating roots, statting other sessions, or loading the cache again. The delay starts with the first event, so continuous writing cannot postpone delivery indefinitely. Creation of a candidate file or directory can request discovery; unrelated provider files and directories do not. Directory membership is reused while its timestamp is unchanged, with a five-minute maximum age checked at reconciliation so missed notifications cannot hide new files indefinitely. An independent thirty-second reconcile catches missed events, refreshes automatic root discovery, finds roots created after startup and reevaluates time-based state. Filesystem activity does not postpone that cadence. `ReconcileInterval` is configurable.

Reconciliation caches directory membership by directory modification time and checks known files for missed appends even when membership is unchanged. New directories and watcher overflow invalidate membership. Failed listings produce `unreadable-source`, retain previous observations and retry rather than becoming cached empty directories. Removed sessions, directories and unused repository identities leave the live caches. Watch failures produce `watch-unavailable` or `watch-overflow`; affected roots continue polling, and failed registrations retry only on the independent reconcile cadence. This includes filesystems that report an error synchronously during watcher registration.

Null roots reuse the existing Codex/Claude telemetry discovery. Explicit roots replace discovery, and an empty list watches nothing. `LocalAgentWorkStateService.ReadLocal` provides the same recent-only options for a one-shot read. `ActiveWithin = null` explicitly requests full history for analysis, not a live-strip default. Stale historical sessions outside the selected window require that wider analysis view; GitHub PR/review attention is independent of local history selection.

## Incremental parsing

The watcher retains per-root, per-file parsed observations in memory, including when a caller supplies `AgentWorkOptions.Cache`. The optional store is loaded once per root, rather than once per event. Unchanged path, size, modification time and parser version reuse a parsed observation. Each entry holds metadata and the parser continuation needed to preserve canonical identity, open-turn/input state and Claude sidechain exclusion. It contains no transcript lines.

An append resumes only at a previous complete UTF-8 JSONL boundary, with hashed prefix/boundary anchors and, where stable, matching file creation time. Linux filesystems without birth time can report a creation-time fallback that changes on each append, so that value is not an append identity there. Unchanged-cache reuse still checks creation time before returning a stored observation. The byte offset includes multibyte UTF-8 correctly. An incomplete suffix is replayed from that boundary when more bytes arrive, without rereading completed history. A valid final object without a newline remains observable, while the continuation stays at the last complete boundary. Completed malformed records retain diagnostics; an incomplete record's transient diagnostic clears after completion. Truncation, replacement and detected rewrites fall back to a full parse. A file changing during parsing is not stamped with a newer cache fingerprint. Deleted files do not reappear from cache. Explicit title indexes, Desktop metadata, clock aging and PR association remain independent of parser reuse. Repository identity, including failed lookups, is cached by working directory and refreshed during reconciliation, so a Git branch/config change appears at that cadence.

Large UTF-8 records stream through a bounded metadata projection. It keeps state-bearing types, timestamps, identity, explicit titles, workspace and branch fields; tool-output bodies are skipped without decoding or materializing their strings. Metadata strings are limited to 64 KiB, projected envelopes to 256 KiB and nesting to 64 levels. Async question metadata is limited to 64 questions per request; invalid counts produce a per-record diagnostic without allocating answer arrays. Selected metadata and framing are validated; ignored payload internals are not a transcript-validation surface. Existing recovered UTF-16 files use the bounded legacy parser and do not support UTF-8 tailing. Parser cache version 8 causes a one-time reparse of recent files saved by an older parser, including corrected input and completion semantics.

Snapshot construction caches the canonical merge and attention result per provider/machine/session group. A single changed session reevaluates that group and reuses unaffected rows. Desktop metadata, PR state and clock boundaries invalidate the relevant results, including quiet expiry driven by merged Desktop activity. An unchanged collection reuses the session array instead of rebuilding every row; changed results still rebuild the sorted aggregate arrays.

Live appends advance the in-memory checkpoint immediately. The optional durable cache is checkpointed during reconciliation instead of writing SQLite for every append burst. A shutdown before reconciliation can leave an older saved checkpoint; startup verifies it and tails the intervening bytes. One-shot `ReadLocal` calls persist their parsed results immediately. Cache failures retain in-memory reuse and report `cache-unavailable`.

These logs are append-only provider artifacts. Editing an old interior while deliberately preserving size, timestamps, creation identity and checkpoint anchors is outside that contract; discard the parsed cache after such edits. A caller-owned cache remains caller-owned on watcher disposal. The optional SQLite companion's `SqliteRawArtifactStore` can persist parsed state across application restarts; use a dedicated application cache database and protect its metadata like exported snapshots.

The snapshot's `scan` reports session files read, reused, skipped and tailed, plus bytes actually consumed by the parser. Small checkpoint anchors, title indexes, workspace and database reads are excluded from `bytesRead`.

### Live workload diagnostic

`IntelligenceX.Performance` is an opt-in diagnostic workload. It creates 1,400 synthetic files split between Codex and Claude, a synthetic Git repository, a dedicated SQLite cache and four writers targeting five appends per second each. It uses the public watcher with a two-day window and the default thirty-second reconcile. No discovery, real transcripts, authentication or network access is involved.

Build the project in Release, then run its executable with a task-owned scratch directory, duration in seconds and file count. Append `control` for the same writers without a watcher. The executable retains its unique synthetic output directory for inspection; remove it after recording the result. This is a settled-process workload rather than a microbenchmark: startup and five seconds of warmup are excluded from `Process.TotalProcessorTime`, and the sixty-second default includes periodic reconciliation. CPU seconds per minute and percent of one core include the writers and snapshot subscriber. Latency measures the oldest append represented by each coalesced update; `singleAppendMs` is a separate append after the writers stop. Every writer must produce updates and drain to its final sequence within ten seconds before the separate probe. Drain deliveries contribute latency samples without extending the CPU window. Initial collection and final membership must also match the requested file count. Timing is diagnostic evidence, outside correctness CI.

```powershell
dotnet build IntelligenceX.Performance/IntelligenceX.Performance.csproj -c Release --artifacts-path D:/scratch/ix-workload-build
dotnet D:/scratch/ix-workload-build/bin/IntelligenceX.Performance/release/IntelligenceX.Performance.dll D:/scratch/ix-workload 60 1400
```

On 1 November 2026, Windows build 26300 / .NET 10.0.14 on a Ryzen 9 9950X3D2 (32 logical processors), normal priority and default scheduler placement, the sixty-second workload measured:

| Implementation | CPU seconds per minute | Percent of one core | Oldest append p95 / maximum | Separate append |
| --- | ---: | ---: | ---: | ---: |
| Before (`3a579993`) | 41.7940 | 69.657% | 2,695 / 3,035 ms | 1,618 ms |
| Targeted updates and coalesced signalling | 1.2655 | 2.109% | 1,099 / 1,194 ms | 1,011 ms |
| Writers without the watcher | 0.2500 | 0.417% | — | — |

Both collection runs observed all four writers' final sequences. The final run handled 1,119 appends during measurement and emitted 56 snapshot events. Total process CPU fell by about 97%; subtracting the separate writer control gives an approximate watcher cost of 1.69% of one core. This subtraction is an estimate from separate launches.

These are workstation observations, not a real-time guarantee. Earlier development runs ranged from 76.27–88.61% before the change. Before the final signalling optimization, candidate launches measured 1.88–2.53%; one run with concurrent validation reached 2,659 ms p95 and 4,032 ms maximum. The final pair ran after task-owned validation processes exited, with other normal workstation activity left alone. Large-history cold parsing, detected rewrites and real application CPU still need their own measurement; this workload isolates settled collection over many files.

### Read-only real-root measurement

On Windows, the same executable's `real` mode discovers existing roots, uses a two-day activity window, a dedicated SQLite cache and the Desktop metadata callback. It opens provider sources read-only and creates its cache only under the supplied scratch directory. It does not fetch GitHub or print session identifiers, titles, source paths, JSON, exception messages or credentials. The cache still contains private metadata; remove the generated directory after recording aggregate results.

```powershell
dotnet D:/scratch/ix-workload-build/bin/IntelligenceX.Performance/release/IntelligenceX.Performance.dll real D:/scratch/ix-real-workload 120 30
```

The arguments select scratch, measurement seconds and reconcile seconds. Startup and five seconds of warmup are excluded. Measurement boundaries wait for instrumented collection scopes to finish, so the actual window can exceed the requested duration. Total process CPU includes runtime, callback and asynchronous Desktop metadata work. `GetThreadTimes` measures exclusive CPU for synchronous reconcile, append parsing and snapshot scopes; nested scopes are subtracted from their parents. The remaining process CPU is not separately attributed. Small values can round to zero at the CPU clock's resolution. Counts include file checks, tail bytes, resolved session groups and watcher failures.

Use this measurement alongside the synthetic workload: actual directory layout, unsupported filesystem notifications and large provider output can change the result substantially. Real activity is observed rather than replayed, so compare duration, appended bytes and collection counts as well as CPU. These diagnostics are opt-in and do not collect native thread clocks on the normal library path.

On the same workstation on 1 November 2026, concurrent 120-second observations of four discovered roots, 256 recent sessions, Desktop metadata and the default thirty-second reconcile measured:

| Measurement | Before (`22e21258`, measurement hooks only) | With membership, projection and snapshot caches |
| --- | ---: | ---: |
| Process CPU seconds per minute | 6.4046 | 1.2344 |
| Percent of one core | 10.674% | 2.057% |
| Reconcile CPU seconds per minute, excluding nested parser work | 5.4361 | 0.6328 |
| Append-parser CPU seconds per minute | 0.0078 | Below clock resolution |
| Snapshot CPU seconds per minute, excluding canonical group evaluation | 0.3359 | 0.0781 |
| Canonical snapshot/group evaluation CPU seconds per minute | 0.0312 | Below clock resolution |
| Reconcile calls / file checks | 89 / 126,736 | 8 / 11,546 |
| Appended bytes read | 2,193,407 | 2,286,779 |
| Session groups reevaluated | Full batch snapshots | 180 |

The windows overlap rather than replay identical input. CPU fell by about 81% while the candidate read more appended bytes. A preceding paired observation measured 10.677% versus 1.849%. Both had two roots reporting Windows watcher error 1 during registration: the previous implementation repeatedly rearmed them and requested discovery, whereas paced recovery kept those roots polling and retained their diagnostics. This explains why the earlier synthetic workload understated real-root CPU. Both observations also reported an existing malformed title index and unavailable Desktop metadata for a source; they are partial-source observations, not proof that every metadata source was available. No provider files were modified.

A final sixty-second observation after narrowing repository-cache refresh to eligible local roots measured **1.6872 CPU seconds per minute / 2.812% of one core**. It read 2,456,398 appended bytes, discovered one new 23,465-byte session, and resolved 119 session groups across 41 snapshot events. Exclusive phase CPU seconds per minute were 0.9530 reconcile, 0.0156 append parsing, 0.1875 snapshot preparation and below clock resolution for canonical group evaluation; event-update bookkeeping used another 0.0469. Five reconciles checked 7,237 files. Session membership grew from 257 to 258. The empty-cache startup took 35.829 seconds and is excluded from the settled CPU measurement; this work does not establish a cold-start latency improvement.

Qualification of the input/failure evidence contract on 2 November used one further finite observation. Four real roots produced 238 sessions; the first local snapshot took 8.929 seconds, parsing 242 files / 3,995,346,127 bytes and skipping 1,038 files before parsing. Desktop metadata had not succeeded at that first capture. The sixty-second settled window used 0.7031 CPU seconds per minute / 1.172% of one core, read 138,540 appended bytes and checked 3,851 files in three reconciles. Exclusive reconcile/snapshot CPU was 0.2812/0.0312 seconds per minute; parser/group-evaluation CPU was below clock resolution. The same source diagnostics remained. Activity was lighter than the preceding observations, so this is a bounded compatibility measurement rather than a comparable speedup claim. A concurrent thirty-second synthetic workload with 1,400 files drained all four writers and measured a 1,013.4 ms separate warm append, 1,046.7 ms burst p95 and 1,084.4 ms maximum. Its process CPU, including writers, was 2.551% of one core.

## Optional metadata and GitHub

The core package has no SQLite dependency. The companion's `CodexAgentSessionMetadataReader.ReadAsync(root)` reads archive flags, workspace, branch and second/millisecond timestamps from `state_5.sqlite` using DbaClientX read-only queries. Pass these records to `ReadLocal(codexMetadata: ...)`, or supply the watcher's `readMetadataAsync` callback. Each record retains `sourceRootId`, and metadata merges only within that root before cross-root session deduplication. A recovered database never inherits liveness merely from a machine label.

Desktop callbacks run independently on `MetadataRefreshInterval` (thirty seconds by default), must honor cancellation, and retain the last successful result on failure. The optional database reader uses one-second command/busy limits and one attempt; filesystem and mapping time are additional. Missing/changed tables and inaccessible sources produce diagnostics rather than suppressing local events.

Pass a caller-owned `GitHubDashboardService` and optional `gitHubLogin` to the watcher for remote attention. The default `GitHubRefreshInterval` is five minutes. GitHub refreshes never run on the local collection worker, and failures retain the last successful PR state with a diagnostic. A per-PR check endpoint failure retains visible PR/review metadata and marks `ChecksUnavailable`. Previous check evidence is retained only for the same commit-scoped checks link. Partial refreshes report `checks-unavailable` and preserve the last complete `gitHubCheckedAtUtc`; successful recovery clears that diagnostic. Each request, including authenticated-user lookup, has a deadline covering headers and body, capped at thirty seconds and respecting a shorter client timeout; the whole refresh is bounded to two minutes. A stalled body cannot hold the service fetch gate indefinitely. `gitHubCheckedAtUtc` distinguishes the last successful remote revalidation from local `capturedAtUtc`.

GitHub uses conditional REST GET requests with ETags for searches, PR metadata, commit statuses and check runs. A 304 search does not freeze CI: current-head checks and statuses are separately revalidated. The service caches normalized metadata, not response bodies, and bounds both fan-out and cache size. Searches and check pages are paginated; incomplete results and GitHub's 1,000-result search ceiling are reported. Authored open PRs and incoming review requests are read independently. Team-only reviews and coordination boards/inboxes remain outside this adapter. See [GitHub's conditional-request guidance](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api#use-conditional-requests-if-appropriate).

Credentials remain with the host and its existing GitHub client. The watcher starts no sign-in or provider process. Fine-grained credentials need read access to the selected repositories' PRs, commit statuses and checks. The caller disposes the GitHub service after the watcher has stopped.

## State and attention

| Signal | Result |
| --- | --- |
| Known unfinished turn with local activity in the last five minutes | `Running`, with `stateIsInferred: true` |
| Unfinished turn that has gone quiet | `Idle`, `lastTurnOutcome: Incomplete`; inspect the possibly interrupted turn |
| Explicit failure or abort | `FailedTurn` or `InterruptedTurn` attention |
| Codex blocking question/approval or synchronous input-tool call | `WaitingOnPerson`; a matching input response resumes activity |
| Codex asynchronous input-tool call | Execution continues independently; `PendingInputs` and `UnconfirmedInput` retain the question after the immediate acknowledgement |
| Codex completed turn | `Idle`, `lastTurnOutcome: Completed`; it does not establish task completion |
| Claude `end_turn`, `stop_sequence` or successful result | `Idle`, `lastTurnOutcome: Completed`; normal completion does not create a human question |
| Claude `turn_duration` | Ends the turn without creating human attention; preserves an explicit failure or incomplete outcome |
| Claude `AskUserQuestion` | `WaitingOnPerson`; only the matching `tool_result.tool_use_id` resolves identified input |
| Closed/archived session | `Finished`; archive is lifecycle closure, not task acceptance |
| Inactive session linked to an open PR at the configured age | `Stale` attention |
| Failing head checks on an authored open PR | `FailedChecks` with a CI link |
| Review requested from the selected user | `ReviewRequested` |

No natural-language keywords determine state. Recency is inferred from provider events, not a process heartbeat; a long silent phase can look incomplete. Recovered sources do not claim local running activity. Discovered WSL logs can establish local activity without resolving Linux workspace paths through Windows Git metadata. Claude subagent logs cannot replace their parent session.

### Questions, failure evidence and consumer identity

`PendingInputs` separates execution from unresolved input. Each entry contains only the provider request ID, question/approval kind, blocking flag, request timestamp and `AnswerIsUnconfirmed`. Several questions can coexist. A synchronous tool result resolves its matching blocking request. The asynchronous input tool returns before a person answers, so its matching tool return is only delivery acknowledgement. Desktop question-reply records resolve the matching asynchronous request after every question in the call has a correlated answer. Partial and repeated replies preserve unanswered questions. Only native question identities are retained; answer bodies are skipped. Without a supported correlated answer record, that request remains unconfirmed across completion, new turns and unrelated user activity. Missing request IDs also remain unconfirmed. Neither question text nor answers are read into the exported contract. An unconfirmed response is a visibility gap, not a reason to resend the question or wake the chat.

Use `State` with `LastTurnOutcome`: idle/completed is a finished turn; idle/unknown does not establish successful completion; idle/incomplete is an unfinished observation. `Finished` is session closure. None establishes task acceptance. Recognized structured failure codes populate `FailureCause` with capacity, usage limit, rate limit, authentication, transport or context limit, and `FailureEvidence: ProviderCode`. Unsupported or absent codes retain `Unknown` with `TerminalEvent`. New observed work clears the prior cause. The supported identifiers follow [Codex's structured error protocol](https://github.com/openai/codex/blob/main/codex-rs/protocol/src/protocol.rs) and [Claude's API error types](https://platform.claude.com/docs/en/api/errors); messages and screenshots are not classifiers. Provider stream failures do not establish a client-delivery gap. Client acceptance/execution/delivery receipts need their own qualified source; these local parsers do not observe that transport.

For a native view, use these contracts together:

- Key a session by `(Provider, Machine, Id)`. Key session attention by that identity plus `Kind` and `InputId`; keep `SourceRootId` for provenance. Equal titles and URLs are not identity.
- Display blocking `WaitingOnPerson` separately from `UnconfirmedInput`. The latter can coexist with Running or a completed idle turn. Join its `InputId` to `PendingInputs` for request metadata.
- Preserve `StateIsInferred`, `LastTurnOutcome`, failure cause/evidence, `ObservationSources`, `ObservedAtUtc` and source diagnostics. Remove any consumer-side `IncompleteTurn` to Running inference; IX already evaluates the observed turn and activity window.
- Keep unresolved attention visible when collapsing old completed history. A running watcher retains already-observed unresolved input past `ActiveWithin` without reopening old logs. A cold recent-only collection does not discover questions hidden in excluded history; `scan.filesSkipped` makes that coverage limit visible. Deletion or removal of a root ends its local observation rather than proving an answer.
- Show freshness and availability independently. `ObservedAtUtc` is the last session-log parse time and stays fixed on parsed-cache reuse. `ObservationSources` identifies log, Desktop metadata and title-index evidence. `CapturedAtUtc` is collection/evaluation time; `MetadataCheckedAtUtc` and `GitHubCheckedAtUtc` track the last successful independent refreshes. Failed metadata refreshes retain their previous rows/time and report a diagnostic; content-free source diagnostics now include `SourceRootId` when known. Refresh timestamps alone do not raise `SnapshotChanged`, but `CurrentSnapshot` carries the latest capture.

Snapshots and cached observations grant no permission to execute, resume, replay delivery, push or publish. Explicit Stop/pause, budget, authentication and quota limits, native Goal controls and uncertain operation outcomes remain with the execution client and its existing admission rules.

`capturedAtUtc` is sampled after local collection and used for the same state evaluation. Activity arriving during a scan is therefore evaluated against completion time. The latest same-root Desktop/log timestamp is used, while explicit completion, failure, interruption and waiting remain authoritative. Provider clock skew or an explicitly supplied evaluation clock can still produce future timestamps; those timestamps are preserved and never infer Running.

Session copies are deduplicated by provider, machine and ID after merging each root. The newest provider activity wins, with a live copy preferred over an archive on a tie. Codex titles require explicit name events or `session_index.jsonl`; the prompt-derived database `title` and ambiguous Desktop title cache are excluded. Repository enrichment reads Git metadata only for sources mapped to this host, supports linked worktrees, and reduces GitHub origin URLs to credential-free `owner/name`. PR links require exact case-sensitive head branch and case-insensitive head repository matches.

## Package and Native AOT qualification

The candidate package version is **0.1.2**:

```xml
<ItemGroup>
  <PackageReference Include="IntelligenceX" Version="0.1.2" />
  <!-- Optional Desktop database metadata or a persistent parsed cache. -->
  <PackageReference Include="IntelligenceX.Storage.SQLite" Version="0.1.2" />
</ItemGroup>
```

The currently published 0.1.1 packages do not contain this API. Publishing 0.1.2 is a separate release step. Once it is on NuGet.org, self-hosted CI can restore that exact version through the ordinary configured NuGet source and cache; it needs no checkout path, CLI installation or subprocess. A private-feed release instead requires that feed in `NuGet.config`, runner network access and process-local feed credentials. A package installed only in one workstation's cache is not available to CI.

Before publication, pack the candidate into a task-owned local feed and restore a consumer with an isolated package cache. Keep the feed configuration and private qualification cache outside the repository and shared NuGet cache. That proves package contents and native consumption locally; it does not prove remote runner restore or publish anything. Use the repository's normal release entrypoint when publication is selected.

Use `AgentWorkJson.Serialize` and `AgentWorkJsonContext.Default.AgentWorkSnapshot` for generated serialization/deserialization. The watcher, parser continuation and conditional GitHub path use no reflection-based JSON fallback. This is a scoped trim/AOT guarantee, not a claim about every existing SDK API. Put `IsAotCompatible` and warning errors on the native application. Both libraries treat `PublishAot` as a local property and keep it false: the executable owns native publishing, even when the command-line property flows through project references during restore. The consumer's native linker still checks its reachable library code. Normal builds and packages keep their existing target frameworks.

`IntelligenceX.AotSmoke` is a .NET 10 Native AOT consumer with JSON reflection disabled and warnings treated as errors. It creates only synthetic logs/databases, checks metadata and persistent cache reuse, observes a real filesystem append event, and removes its unique fixture. Fake HTTP tests cover GitHub revalidation without authentication or external network activity.

Publish this consumer with the command-line property too, so restore covers the same project-reference graph as a native application:

```powershell
dotnet publish IntelligenceX.AotSmoke/IntelligenceX.AotSmoke.csproj -c Release -r win-x64 /p:PublishAot=true
```

## Secondary CLI

```powershell
intelligencex agents status --json
intelligencex agents status --json --since 2d --github
intelligencex agents status --json --all-history
intelligencex agents status --json --since 1d --no-cache
```

The CLI uses the same two-day default and strict file timestamp cutoff. `--since` accepts positive whole `d`, `h` or `m` values; `--all-history` is the explicit forensic path. `--github` is opt-in and reuses existing local credentials without sign-in. Explicit provider roots replace discovery. `--stale-days` controls historical PR inactivity, independently of the selected history window.

The CLI's optional parsed cache is `IntelligenceX/agents/scan-cache.db` under local application data; `--cache-path PATH` isolates it and `--no-cache` disables it. Exit codes are 0 for complete, 1 for invalid arguments and 2 for partial/unavailable sources or timeout. A native live consumer should use the library watcher instead of repeatedly launching this batch command.
