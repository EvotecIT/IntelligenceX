# Local work state and attention

`IntelligenceX.Telemetry.Agents` provides a read-only snapshot of local Codex and Claude Code work. It carries provider/session identifiers, explicit titles, workspace, repository, branch, machine, timestamps, state and navigation links. It never projects messages, prompts, answers, tool arguments or credentials. Titles and paths can still be private; treat exported snapshots accordingly.

## Read a snapshot

```powershell
intelligencex agents status --json
intelligencex agents status --json --github --stale-days 5
intelligencex agents status --json --codex-root ./sample/codex --claude-root ./sample/claude
```

Local collection is the default. `--github` reuses `GitHubCredentialResolver` (existing environment credentials or GitHub CLI login); it does not start sign-in. `--login USER` selects whose authored PRs and incoming review requests to read and requires `--github`. This does not grant access beyond the credential's repository permissions. Explicit roots replace discovery completely, including discovery for the other provider. Supply a Codex home, sessions directory, individual JSONL file, Claude config directory or projects directory.

Exit codes are `0` for a complete snapshot, `1` for invalid arguments and `2` for a partial/unavailable source or collection timeout. Partial snapshots still produce JSON with content-free diagnostic codes. A failed or timed-out optional GitHub lookup preserves already collected local sessions. Consumers should display diagnostics rather than interpret an unavailable source as no work. Collection is bounded to two minutes in the CLI. No polling, client launch, hook installation or provider-state writes occur.

## Library consumption

```csharp
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;

var local = LocalAgentWorkStateService.ReadLocal();
using var github = new GitHubDashboardService(token);
var snapshot = await github.EnrichAgentWorkAsync(
    local, staleAfter: TimeSpan.FromDays(7));
var json = AgentWorkJson.Serialize(snapshot);
```

`ReadLocal` accepts existing `SourceRootRecord` values. Null means reuse the telemetry discoveries; an empty collection reads nothing. It also accepts an explicit clock, activity window and cancellation token. `AgentWorkStateRules.Evaluate` does no I/O and accepts a caller-supplied clock. The snapshot retains open PRs that have no local session.

Local reads scan complete histories synchronously. Native UI consumers should collect on a background task and retain the last snapshot between deliberate refreshes. This initial snapshot API does not provide an incremental watcher or a live process heartbeat.

The CLI additionally uses `IntelligenceX.Storage.SQLite`'s `CodexAgentSessionMetadataReader.ReadAsync(root)` to read Desktop archive flags, workspace, branch and timestamps from `state_5.sqlite`. It uses the existing DbaClientX dependency and its read-only query API. Pass those records to `ReadLocal(codexMetadata: metadata)` to enrich a library consumer the same way. SQLite is optional; the core package remains free of native database dependencies. Schema fields are selected explicitly, with absent optional columns represented as unknown. A missing/changed thread table is reported as a metadata-source failure.

## State and attention semantics

| Signal | Result |
| --- | --- |
| Codex started turn with local activity in the last five minutes | `Running`, with `stateIsInferred: true` |
| Started turn that has gone quiet without a terminal event | `Idle`, `lastTurnOutcome: Incomplete`; attention suggests inspecting the possibly interrupted turn |
| Explicit turn failure or abort | `FailedTurn` or `InterruptedTurn` attention |
| Codex structured question/approval event or input-tool call | `WaitingOnPerson`; a matching input response resumes activity |
| Codex completed turn | `Idle`, with `lastTurnOutcome: Completed`; it does not prove the whole task finished |
| Claude `end_turn`, terminal result or `turn_duration` | `WaitingOnPerson`; this means the conversation is ready for input, not that the text necessarily asks a question |
| Claude `AskUserQuestion` tool use | `WaitingOnPerson` until activity resumes |
| Explicit session close or archive | `Finished`; archive means local lifecycle closure, not task acceptance or PR merge |
| Inactive session with an open PR at or beyond the configurable age | `Stale` attention; an existing waiting state remains visible as well |
| Failed head checks on an authored open PR | `FailedChecks` attention with a CI link |
| Open PR requesting review from the selected user | `ReviewRequested` attention |

No text keywords determine state. Provider formats may omit lifecycle or approval events; absent events remain unknown. Log recency is an inference, not a process heartbeat: a long silent model/tool phase can look incomplete, while recent activity can outlive a process. Recovered/remote roots never claim local running activity. Background work outside the recorded parent session cannot be inferred. Claude subagent logs are excluded so their shared parent session ID cannot overwrite the parent state.

Session copies are deduplicated by provider, machine and session ID. The newest provider timestamp wins, with a live copy preferred over an archive on a tie. Copying a file does not refresh activity. Codex titles come from explicit name events and `session_index.jsonl`. The database `title` column can contain the first prompt, and the Desktop title cache has no explicit-name provenance, so neither is exported. Database metadata enriches archive/workspace fields without replacing an explicit name or transcript turn outcome. Local Git metadata fills missing repository/branch values only for sources mapped to this machine; recovered/remote workspace paths are not resolved against today's checkout. Linked worktrees are supported, and origin URLs are reduced to GitHub `owner/name` without credentials.

PR association requires an exact, case-sensitive branch match and a matching head repository. Repository names are case-insensitive. The head repository keeps similarly named fork branches separate. Matching does not use directory basenames or guess links from message text. GitHub collection reads the current head's `statusCheckRollup`, paginates authored and incoming-review searches, and reports search/API failures instead of claiming an empty result. More than 1,000 matches in either GitHub search is an explicit limit. Team-only review requests and coordination boards/inboxes are outside this adapter.

## Marquee and Native AOT

Until a package containing this feature is published, reference the local checkout from Marquee:

```xml
<ItemGroup>
  <ProjectReference Include="../IntelligenceX/IntelligenceX/IntelligenceX.csproj" />
  <!-- Optional Desktop database metadata; the CLI already includes this. -->
  <ProjectReference Include="../IntelligenceX/IntelligenceX.Storage.SQLite/IntelligenceX.Storage.SQLite.csproj" />
</ItemGroup>
```

Use the actual relative checkout location. This uses the new source directly and does not require a release. A locally packed alternative is to pack these two existing projects at a private qualification version into a task-owned feed, then restore the consumer with a task-owned `NUGET_PACKAGES` directory. Use a temporary NuGet configuration listing that feed and the normal public dependency feed; keep its configuration and package cache outside the repository. Restore into that isolated cache rather than placing private packages in the ordinary shared cache. Do not use the current public package version to claim this feature is available. No package publication is needed for either route.

Use `AgentWorkJsonContext.Default.AgentWorkSnapshot` for deserialization, or `AgentWorkJson.Serialize` for serialization. Both use generated JSON metadata with string enum names. The collector, rules and new GitHub path use no reflection-based serialization. This is a scoped consumption guarantee, not a claim that every existing SDK API is trim/AOT-safe. Do not set `PublishAot` globally on the multi-target library; set it in the native application.

`IntelligenceX.AotSmoke` is a standalone .NET 10 consumer with `IsAotCompatible`, warning errors, Native AOT, and JSON reflection disabled. It creates synthetic Codex/Claude logs and a synthetic Desktop database under the supplied scratch directory, exercises metadata collection and generated JSON exchange, and removes its unique fixture afterward. It also roots the GitHub collection path for native compilation; HTTP behavior is separately tested with fake responses.

```powershell
dotnet publish IntelligenceX.AotSmoke/IntelligenceX.AotSmoke.csproj -c Release -r win-x64 --artifacts-path ./artifacts/aot
./artifacts/aot/publish/IntelligenceX.AotSmoke/release_win-x64/IntelligenceX.AotSmoke.exe ./artifacts/fixtures
dotnet test IntelligenceX.UnitTests/IntelligenceX.UnitTests.csproj -c Release -f net10.0 --filter "FullyQualifiedName~AgentWorkStateTests|FullyQualifiedName~GitHubAgentWorkTests"
```

Native publishing requires the platform's normal .NET Native AOT C++ prerequisites. On Windows, the Visual Studio Installer directory must be on the build process's PATH if its toolchain scripts invoke `vswhere.exe` without a full path. The smoke executable has no real authentication, profile scanning or external HTTP activity. The console harness also checks the read-only SQLite projection and the CLI JSON boundary on synthetic fixtures.
