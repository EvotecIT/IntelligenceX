using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Codex;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

internal static class CodexAgentSessionReader {
    internal static AgentSessionParserState Read(string path, SourceRootRecord root, Action<string> diagnostic, CancellationToken cancellationToken,
        AgentSessionParserState? previous = null, AgentSessionReadWindow? window = null) {
        var session = previous?.Session ?? new AgentSessionState {
            SourceRootId = root.Id,
            Provider = "codex", Id = CodexSessionImportSupport.TryExtractSessionIdFromFileName(path) ?? Path.GetFileNameWithoutExtension(path),
            Machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null),
            IsArchived = IsArchivedPath(path)
        };
        var active = previous?.Active ?? false;
        var hasSessionMetadata = previous?.HasSessionMetadata ?? false;
        var questions = previous?.AsyncQuestions ?? new Dictionary<string, bool[]>();
        window ??= new AgentSessionReadWindow();
        window.Checkpoint = previous ?? new AgentSessionParserState { Session = session, Active = active, HasSessionMetadata = hasSessionMetadata };
        foreach (var document in AgentSessionMetadataProjection.Read(path, diagnostic, cancellationToken, window)) {
            using (document) {
                var entry = document.RootElement;
                var timestamp = Timestamp(entry);
                session = session with {
                    StartedAtUtc = session.StartedAtUtc ?? timestamp,
                    LastActivityAtUtc = Later(session.LastActivityAtUtc, timestamp)
                };
                var type = Text(entry, "type");
                var payload = Object(entry, "payload");
                if ((type == "response_item" || type == "event_msg" && Text(payload, "type") != "thread_name_updated") &&
                    timestamp.HasValue)
                    session = session with { LastTurnActivityAtUtc = Later(session.LastTurnActivityAtUtc, timestamp) };
                if (type == "session_meta") {
                    // A fork can embed its parent's metadata after its own canonical first record.
                    if (!hasSessionMetadata) {
                    hasSessionMetadata = true;
                    var git = Object(payload, "git");
                    session = session with {
                        Id = Text(payload, "id") ?? Text(payload, "session_id") ?? session.Id,
                        WorkingDirectory = Text(payload, "cwd") ?? session.WorkingDirectory,
                        Branch = Text(git, "branch") ?? session.Branch,
                        Repository = AgentRepositoryIdentity.FromRemote(Text(git, "repository_url")) ?? session.Repository
                    };
                    }
                } else if (type == "turn_context") {
                    var cwd = Text(payload, "cwd");
                    // Forks may replay contexts older than their own canonical session metadata.
                    if (cwd is not null && (session.WorkingDirectory is null
                        || (timestamp.HasValue && session.StartedAtUtc.HasValue && timestamp >= session.StartedAtUtc))
                        && cwd != session.WorkingDirectory) {
                        session = session with { WorkingDirectory = cwd, Repository = null, Branch = null };
                    }
                } else if (type == "event_msg") {
                    switch (Text(payload, "type")) {
                        case "task_started": case "turn_started":
                            active = true;
                            session = session with { TurnOpen = true, TurnId = Text(payload, "turn_id") ?? Text(payload, "turnId"), TurnStartedAtUtc = timestamp, TurnEndedAtUtc = null,
                                State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Unknown,
                                FailureCause = AgentFailureCause.Unknown, FailureEvidence = AgentFailureEvidence.None };
                            break;
                        case "task_complete": case "task_completed": case "turn_complete": case "turn_completed":
                            active = false;
                            session = session with { TurnOpen = false, TurnEndedAtUtc = timestamp };
                            var error = Object(payload, "error");
                            session = error.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                                ? session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Completed,
                                    FailureCause = AgentFailureCause.Unknown, FailureEvidence = AgentFailureEvidence.None }
                                : AgentFailureClassifier.Failed(session, error, codex: true);
                            break;
                        case "turn_aborted": case "task_aborted": case "turn_interrupted":
                            active = false;
                            session = session with { TurnOpen = false, TurnEndedAtUtc = timestamp };
                            session = AgentFailureClassifier.Failed(session, payload, codex: true, AgentTurnOutcome.Interrupted);
                            break;
                        case "turn_failed": case "task_failed":
                            active = false;
                            session = session with { TurnOpen = false, TurnEndedAtUtc = timestamp };
                            session = AgentFailureClassifier.Failed(session, payload, codex: true);
                            break;
                        case "error":
                            if (AgentFailureClassifier.IsNonTurnCodexError(payload)) break;
                            active = false;
                            session = session with { TurnOpen = false, TurnEndedAtUtc = timestamp };
                            session = AgentFailureClassifier.Failed(session, payload, codex: true);
                            break;
                        case "request_user_input": case "approval_requested":
                            active = false;
                            session = AgentInputRequests.Add(session, Text(payload, "call_id") ?? Text(payload, "id"),
                                Text(payload, "type") == "approval_requested" ? AgentInputKind.Approval : AgentInputKind.Question, true, timestamp);
                            break;
                        case "thread_name_updated":
                            var namedThread = Text(payload, "thread_id") ?? Text(payload, "threadId");
                            if (namedThread is null || namedThread == session.Id) {
                                session = session with { Title = Text(payload, "thread_name") ?? Text(payload, "title") ?? session.Title };
                            }
                            break;
                        case "session_complete": case "thread_closed":
                            active = false;
                            session = session with { State = AgentWorkState.Finished };
                            break;
                        case "item_completed":
                            var item = Object(payload, "item");
                            var questionId = Text(item, "id");
                            var count = CodexQuestionReplies.QuestionCount(item, diagnostic);
                            if (Text(item, "type") == "AgentMessage" && Text(item, "delivery") == "async"
                                && questionId is not null && count > 0 && !questions.ContainsKey(questionId)) {
                                questions = new Dictionary<string, bool[]>(questions) { [questionId] = new bool[count] };
                            }
                            break;
                    }
                } else if (type == "response_item") {
                    var itemType = Text(payload, "type");
                    var name = Text(payload, "name");
                    if ((itemType == "function_call" || itemType == "custom_tool_call") &&
                        (name is "request_user_input" or "functions.request_user_input" or "request_user_input_async" or "functions.request_user_input_async")) {
                        var asynchronous = name is "request_user_input_async" or "functions.request_user_input_async";
                        active = asynchronous;
                        session = AgentInputRequests.Add(session, Text(payload, "call_id"), AgentInputKind.Question, !asynchronous, timestamp);
                    } else if (itemType is "function_call_output" or "custom_tool_call_output") {
                        var updated = AgentInputRequests.CompleteBlocking(session, Text(payload, "call_id"));
                        if (!ReferenceEquals(updated, session)) { session = updated; active = true; }
                    } else if (itemType == "message" && Text(payload, "role") == "user") {
                        foreach (var reply in CodexQuestionReplies.Read(payload, diagnostic)) {
                            if (!questions.TryGetValue(reply.CallId, out var answered) || reply.Index < 0 || reply.Index >= answered.Length) continue;
                            var updatedAnswers = (bool[])answered.Clone();
                            updatedAnswers[reply.Index] = true;
                            questions = new Dictionary<string, bool[]>(questions) { [reply.CallId] = updatedAnswers };
                            if (Array.TrueForAll(updatedAnswers, static value => value)) {
                                session = AgentInputRequests.CompleteAsync(session, reply.CallId);
                                questions.Remove(reply.CallId);
                            }
                        }
                    }
                }
            }
            if (window.RecordIsComplete) window.Checkpoint = new AgentSessionParserState { Session = session, Active = active, HasSessionMetadata = hasSessionMetadata, AsyncQuestions = questions };
        }
        return new AgentSessionParserState { Session = session, Active = active, HasSessionMetadata = hasSessionMetadata, AsyncQuestions = questions };
    }

    private static bool IsArchivedPath(string path) {
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) {
            if (part == "archived_sessions") return true;
        }
        return false;
    }

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left > right ? left : right;
}
