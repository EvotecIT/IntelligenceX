using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Telemetry.Usage;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

internal static class ClaudeAgentSessionReader {
    private const int MaximumPeerMessages = 256;
    internal static AgentSessionParserState Read(string path, SourceRootRecord root, Action<string> diagnostic, CancellationToken cancellationToken,
        AgentSessionParserState? previous = null, AgentSessionReadWindow? window = null) {
        var session = previous?.Session ?? new AgentSessionState {
            SourceRootId = root.Id,
            Provider = "claude", Id = Path.GetFileNameWithoutExtension(path),
            Machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null)
        };
        var active = previous?.Active ?? false;
        var sidechain = previous?.Sidechain ?? false;
        var lastRecordId = previous?.ClaudeLastRecordId;
        var lastParentId = previous?.ClaudeLastParentId;
        var peerMessages = session.PeerMessages.ToList();
        var associations = previous?.ProviderPullRequestUrlsBySession ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
        window ??= new AgentSessionReadWindow();
        window.Checkpoint = previous ?? Checkpoint();
        foreach (var document in AgentSessionMetadataProjection.Read(path, diagnostic, cancellationToken, window)) {
            using (document) {
                var entry = document.RootElement;
                var type = Text(entry, "type");
                if (type == "pr-link") {
                    // Provider association is separate from conversation activity and the
                    // primary checkout's branch. Recovered filenames are only provisional
                    // identities: keep the recorded owner through cache/tail continuation.
                    var owner = Text(entry, "sessionId");
                    if (!string.IsNullOrEmpty(owner) && ProviderPullRequestUrl(entry) is { } url) {
                        associations.TryGetValue(owner!, out var urls);
                        if (urls is null || !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                            associations = new Dictionary<string, string[]>(associations, StringComparer.Ordinal) {
                                [owner!] = (urls ?? Array.Empty<string>()).Concat(new[] { url }).ToArray()
                            };
                    }
                    session = session with { ProviderPullRequestUrls = AssociationsFor(session.Id) };
                    if (window.RecordIsComplete) window.Checkpoint = Checkpoint();
                    continue;
                }
                sidechain |= Object(entry, "isSidechain").ValueKind == JsonValueKind.True;
                var timestamp = Timestamp(entry);
                session = session with {
                    Id = Text(entry, "sessionId") ?? session.Id,
                    ParentSessionId = Text(entry, "parentSessionId") ?? session.ParentSessionId,
                    ProviderPullRequestUrls = AssociationsFor(Text(entry, "sessionId") ?? session.Id),
                    WorkingDirectory = Text(entry, "cwd") ?? session.WorkingDirectory,
                    Branch = Text(entry, "gitBranch") ?? session.Branch,
                    StartedAtUtc = session.StartedAtUtc is null || timestamp < session.StartedAtUtc ? timestamp : session.StartedAtUtc,
                    LastActivityAtUtc = session.LastActivityAtUtc is null || timestamp > session.LastActivityAtUtc ? timestamp : session.LastActivityAtUtc
                };
                var message = Object(entry, "message");
                var recordId = Text(entry, "uuid");
                var parentId = Text(entry, "parentUuid");
                var turnRecord = type is "user" or "assistant" or "result" or "error" ||
                    type == "system" && Text(entry, "subtype") == "turn_duration";
                // A native parent on a different branch cannot terminate this peer's turn.
                // Repeated assistant envelopes may carry the same uuid while streaming.
                if (turnRecord && parentId is not null && parentId != lastRecordId &&
                    (recordId is null || recordId != lastRecordId || parentId != lastParentId)) {
                    session = session with { SourceMessageId = null };
                }
                var limited = session.FailureCause is AgentFailureCause.RateLimit or AgentFailureCause.UsageLimit;
                if (type is "user" or "assistant" or "result" or "error" && timestamp.HasValue)
                    session = session with { LastTurnActivityAtUtc = timestamp > session.LastTurnActivityAtUtc || session.LastTurnActivityAtUtc is null
                        ? timestamp : session.LastTurnActivityAtUtc };
                if (type == "custom-title") {
                    session = session with { Title = Text(entry, "customTitle") ?? session.Title };
                } else if (type == "user") {
                    var responses = Object(message, "content");
                    var toolResult = responses.ValueKind == JsonValueKind.Array &&
                        responses.EnumerateArray().Any(block => Text(block, "type") == "tool_result");
                    if (session.TurnOpen != true || !toolResult) {
                        if (session.TurnId != Text(entry, "uuid")) session = session with { TurnCount = session.TurnCount + 1 };
                        session = session with { TurnOpen = true, TurnId = Text(entry, "uuid"), TurnStartedAtUtc = timestamp, TurnEndedAtUtc = null,
                            SourceMessageId = Text(Object(entry, "origin"), "kind") == "peer" &&
                                Guid.TryParse(Text(Object(entry, "origin"), "msg_id"), out var messageId) ? messageId : null };
                        lastRecordId = recordId;
                        lastParentId = parentId;
                    }
                    active = true;
                    session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Unknown,
                        FailureCause = limited ? session.FailureCause : AgentFailureCause.Unknown,
                        FailureEvidence = limited ? session.FailureEvidence : AgentFailureEvidence.None };
                    if (responses.ValueKind == JsonValueKind.Array)
                        foreach (var block in responses.EnumerateArray())
                            if (Text(block, "type") == "tool_result")
                                session = AgentInputRequests.CompleteBlocking(session, Text(block, "tool_use_id"));
                } else if (type == "assistant") {
                    if (session.TurnOpen != true)
                        session = session with { TurnCount = session.TurnCount + 1, TurnOpen = true, TurnId = Text(entry, "uuid"), TurnStartedAtUtc = timestamp, TurnEndedAtUtc = null,
                            SourceMessageId = null };
                    active = true;
                    session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Unknown,
                        FailureCause = limited ? session.FailureCause : AgentFailureCause.Unknown,
                        FailureEvidence = limited ? session.FailureEvidence : AgentFailureEvidence.None };
                    var stop = Text(message, "stop_reason");
                    if (stop == "end_turn" || stop == "stop_sequence") {
                        active = false;
                        session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Completed,
                            FailureCause = AgentFailureCause.Unknown, FailureEvidence = AgentFailureEvidence.None };
                    } else if (stop == "max_tokens") {
                        active = false;
                        session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Incomplete };
                    }
                    var content = Object(message, "content");
                    if (content.ValueKind == JsonValueKind.Array) {
                        foreach (var block in content.EnumerateArray()) {
                            if (Text(block, "type") == "tool_use" && Text(block, "name") == "AskUserQuestion") {
                                active = false;
                                session = AgentInputRequests.Add(session, Text(block, "id"), AgentInputKind.Question, true, timestamp);
                            }
                        }
                    }
                    if (Object(entry, "isApiErrorMessage").ValueKind == JsonValueKind.True) {
                        active = false;
                        session = AgentFailureClassifier.Failed(session, Object(entry, "error"), codex: false);
                    }
                } else if (type == "result") {
                    active = false;
                    session = Object(entry, "is_error").ValueKind == JsonValueKind.True
                        ? AgentFailureClassifier.Failed(session, Object(entry, "error"), codex: false)
                        : session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Completed,
                            FailureCause = AgentFailureCause.Unknown, FailureEvidence = AgentFailureEvidence.None };
                } else if (type == "system" && Text(entry, "subtype") == "turn_duration") {
                    active = false;
                    if (session.LastTurnOutcome == AgentTurnOutcome.Unknown)
                        session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Completed };
                } else if (type == "error") {
                    active = false;
                    session = AgentFailureClassifier.Failed(session, Object(entry, "error"), codex: false);
                }
                if (!active && session.LastTurnOutcome is AgentTurnOutcome.Completed or AgentTurnOutcome.Failed or AgentTurnOutcome.Interrupted or AgentTurnOutcome.Incomplete)
                    session = session with { TurnOpen = false, TurnEndedAtUtc = session.TurnEndedAtUtc ?? timestamp };
                if (turnRecord) {
                    if (recordId is not null) {
                        lastRecordId = recordId;
                        lastParentId = parentId;
                    }
                    if (session.SourceMessageId is { } sourceMessage) {
                        var observed = new AgentMessageObservation {
                            SourceMessageId = sourceMessage, SessionId = session.Id, TurnId = session.TurnId,
                            TurnOpen = session.TurnOpen, TurnStartedAtUtc = session.TurnStartedAtUtc,
                            TurnEndedAtUtc = session.TurnEndedAtUtc, LastActivityAtUtc = session.LastTurnActivityAtUtc,
                            Outcome = session.LastTurnOutcome,
                            FailureCause = session.LastTurnOutcome == AgentTurnOutcome.Failed ? session.FailureCause : AgentFailureCause.Unknown
                        };
                        var index = peerMessages.FindIndex(item => item.SourceMessageId == sourceMessage);
                        if (index >= 0) peerMessages[index] = observed;
                        else {
                            if (peerMessages.Count == MaximumPeerMessages) {
                                peerMessages.RemoveAt(0);
                                diagnostic("peer-message-retention-limit");
                            }
                            peerMessages.Add(observed);
                        }
                        session = session with { PeerMessages = peerMessages.ToArray() };
                    }
                }
            }
            if (window.RecordIsComplete) window.Checkpoint = Checkpoint();
        }
        // Subagents share the parent sessionId. They must not replace its state during deduplication.
        return Checkpoint();

        AgentSessionParserState Checkpoint() => new() { Session = session, Active = active, Sidechain = sidechain,
            ProviderPullRequestUrlsBySession = associations, ClaudeLastRecordId = lastRecordId, ClaudeLastParentId = lastParentId };
        string[] AssociationsFor(string id) => associations.TryGetValue(id, out var urls) ? urls : Array.Empty<string>();
    }

    private static string? ProviderPullRequestUrl(JsonElement entry) {
        var repository = Text(entry, "prRepository");
        var number = Object(entry, "prNumber");
        if (repository is null || AgentRepositoryIdentity.FromRemote("https://github.com/" + repository) != repository
            || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var id) || id <= 0) return null;
        var canonical = "https://github.com/" + repository + "/pull/" + id.ToString(CultureInfo.InvariantCulture);
        return string.Equals(Text(entry, "prUrl"), canonical, StringComparison.OrdinalIgnoreCase) ? canonical : null;
    }
}
