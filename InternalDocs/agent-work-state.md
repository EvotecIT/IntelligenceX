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

The watcher uses `FileSystemWatcher` with a short fixed coalescing delay. An independent thirty-second reconcile catches missed events, refreshes automatic root discovery, finds roots created after startup and reevaluates time-based state. Filesystem activity does not postpone that cadence. Unchanged capture times and scan counters do not generate notification storms. `ReconcileInterval` is configurable. Watch failures and buffer overflow produce content-free diagnostics; repeated identical failures do not requeue scans. Polling continues, failed watchers are rearmed, and their diagnostics clear on recovery.

Null roots reuse the existing Codex/Claude telemetry discovery. Explicit roots replace discovery, and an empty list watches nothing. `LocalAgentWorkStateService.ReadLocal` provides the same recent-only options for a one-shot read. `ActiveWithin = null` explicitly requests full history for analysis, not a live-strip default. Stale historical sessions outside the selected window require that wider analysis view; GitHub PR/review attention is independent of local history selection.

## Incremental parsing

The watcher keeps an in-memory `IRawArtifactStore` unless a caller supplies `AgentWorkOptions.Cache`. Unchanged path, size, modification time and parser version reuse a parsed observation. Each entry holds metadata and the parser continuation needed to preserve canonical identity, open-turn/input state and Claude sidechain exclusion. It contains no transcript lines.

An append resumes only at a previous complete UTF-8 JSONL boundary, with matching file creation time and hashed prefix/boundary anchors. The byte offset includes multibyte UTF-8 correctly. Partial final records, truncation, replacement and detected rewrites fall back to a full parse. A file changing during parsing is not stamped with a newer cache fingerprint. Deleted files do not reappear from cache. Explicit title indexes, workspace identity, Desktop metadata, clock aging and PR association are reapplied independently of parser reuse.

These logs are append-only provider artifacts. Editing an old interior while deliberately preserving size, timestamps, creation identity and checkpoint anchors is outside that contract; discard the parsed cache after such edits. A caller-owned cache remains caller-owned on watcher disposal. The optional SQLite companion's `SqliteRawArtifactStore` can persist parsed state across application restarts; use a dedicated application cache database and protect its metadata like exported snapshots.

The snapshot's `scan` reports session files read, reused, skipped and tailed, plus bytes actually consumed by the parser. Small checkpoint anchors, title indexes, workspace and database reads are excluded from `bytesRead`.

### Measured local behavior

A read-only Windows/.NET 10 run on 1 November 2026, including discovery and watcher startup with an empty in-memory cache and `ActiveWithin = 2 days`, took **14.48 seconds**. It parsed 262 recent files containing 4.32 GB, skipped 1,144 old files before parsing, and returned 254 sessions with no activity timestamp later than capture. This first cold read still pays for history inside recent files; it is not an instant-start guarantee.

With those real roots still attached, a synthetic 34.1 MB history measured **366.15 ms** from appending a line to `SnapshotChanged`. That update reused 262 other files and tailed exactly the 103 appended bytes rather than rereading the history. No real provider file was modified. These are single-run observations with live source changes and filesystem scheduling, not latency thresholds or unit-test performance gates.

## Optional metadata and GitHub

The core package has no SQLite dependency. The companion's `CodexAgentSessionMetadataReader.ReadAsync(root)` reads archive flags, workspace, branch and second/millisecond timestamps from `state_5.sqlite` using DbaClientX read-only queries. Pass these records to `ReadLocal(codexMetadata: ...)`, or supply the watcher's `readMetadataAsync` callback. Each record retains `sourceRootId`, and metadata merges only within that root before cross-root session deduplication. A recovered database never inherits liveness merely from a machine label.

Desktop callbacks run independently on `MetadataRefreshInterval` (thirty seconds by default), must honor cancellation, and retain the last successful result on failure. The optional database reader uses one-second command/busy limits and one attempt; filesystem and mapping time are additional. Missing/changed tables and inaccessible sources produce diagnostics rather than suppressing local events.

Pass a caller-owned `GitHubDashboardService` and optional `gitHubLogin` to the watcher for remote attention. The default `GitHubRefreshInterval` is five minutes. GitHub refreshes never run on the local collection worker, and failures retain the last successful PR state with a diagnostic. Each request has a deadline covering headers and body, capped at thirty seconds and respecting a shorter client timeout; the whole refresh is bounded to two minutes. A stalled body cannot hold the service fetch gate indefinitely. `gitHubCheckedAtUtc` distinguishes the last successful remote revalidation from local `capturedAtUtc`.

GitHub uses conditional REST GET requests with ETags for searches, PR metadata, commit statuses and check runs. A 304 search does not freeze CI: current-head checks and statuses are separately revalidated. The service caches normalized metadata, not response bodies, and bounds both fan-out and cache size. Searches and check pages are paginated; incomplete results and GitHub's 1,000-result search ceiling are reported. Authored open PRs and incoming review requests are read independently. Team-only reviews and coordination boards/inboxes remain outside this adapter. See [GitHub's conditional-request guidance](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api#use-conditional-requests-if-appropriate).

Credentials remain with the host and its existing GitHub client. The watcher starts no sign-in or provider process. Fine-grained credentials need read access to the selected repositories' PRs, commit statuses and checks. The caller disposes the GitHub service after the watcher has stopped.

## State and attention

| Signal | Result |
| --- | --- |
| Known unfinished turn with local activity in the last five minutes | `Running`, with `stateIsInferred: true` |
| Unfinished turn that has gone quiet | `Idle`, `lastTurnOutcome: Incomplete`; inspect the possibly interrupted turn |
| Explicit failure or abort | `FailedTurn` or `InterruptedTurn` attention |
| Codex structured question/approval or input-tool call | `WaitingOnPerson`; a matching input response resumes activity |
| Codex completed turn | `Idle`, `lastTurnOutcome: Completed`; it does not establish task completion |
| Claude `end_turn`, result or `turn_duration` | `WaitingOnPerson`; ready for input, regardless of prose |
| Claude `AskUserQuestion` | `WaitingOnPerson` until activity resumes |
| Closed/archived session | `Finished`; archive is lifecycle closure, not task acceptance |
| Inactive session linked to an open PR at the configured age | `Stale` attention |
| Failing head checks on an authored open PR | `FailedChecks` with a CI link |
| Review requested from the selected user | `ReviewRequested` |

No natural-language keywords determine state. Recency is inferred from provider events, not a process heartbeat; a long silent phase can look incomplete. Recovered sources do not claim local running activity. Discovered WSL logs can establish local activity without resolving Linux workspace paths through Windows Git metadata. Claude subagent logs cannot replace their parent session.

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

Use `AgentWorkJson.Serialize` and `AgentWorkJsonContext.Default.AgentWorkSnapshot` for generated serialization/deserialization. The watcher, parser continuation and conditional GitHub path use no reflection-based JSON fallback. This is a scoped trim/AOT guarantee, not a claim about every existing SDK API. Put `IsAotCompatible`, `PublishAot` and warning errors on the native application; do not set `PublishAot` globally on the multi-target SDK.

`IntelligenceX.AotSmoke` is a .NET 10 Native AOT consumer with JSON reflection disabled and warnings treated as errors. It creates only synthetic logs/databases, checks metadata and persistent cache reuse, observes a real filesystem append event, and removes its unique fixture. Fake HTTP tests cover GitHub revalidation without authentication or external network activity.

## Secondary CLI

```powershell
intelligencex agents status --json
intelligencex agents status --json --since 2d --github
intelligencex agents status --json --all-history
intelligencex agents status --json --since 1d --no-cache
```

The CLI uses the same two-day default and strict file timestamp cutoff. `--since` accepts positive whole `d`, `h` or `m` values; `--all-history` is the explicit forensic path. `--github` is opt-in and reuses existing local credentials without sign-in. Explicit provider roots replace discovery. `--stale-days` controls historical PR inactivity, independently of the selected history window.

The CLI's optional parsed cache is `IntelligenceX/agents/scan-cache.db` under local application data; `--cache-path PATH` isolates it and `--no-cache` disables it. Exit codes are 0 for complete, 1 for invalid arguments and 2 for partial/unavailable sources or timeout. A native live consumer should use the library watcher instead of repeatedly launching this batch command.
