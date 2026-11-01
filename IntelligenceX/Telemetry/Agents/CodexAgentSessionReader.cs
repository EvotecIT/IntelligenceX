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
    internal static AgentSessionState Read(string path, SourceRootRecord root, DateTimeOffset now,
        TimeSpan activeWindow, Action<string> diagnostic, CancellationToken cancellationToken) {
        var session = new AgentSessionState {
            Provider = "codex", Id = CodexSessionImportSupport.TryExtractSessionIdFromFileName(path) ?? Path.GetFileNameWithoutExtension(path),
            Machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null),
            IsArchived = IsArchivedPath(path)
        };
        var active = false;
        var hasSessionMetadata = false;
        string? inputCallId = null;
        foreach (var line in ReadLines(path, cancellationToken)) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { diagnostic("malformed-record"); continue; }
            using (document) {
                var entry = document.RootElement;
                var timestamp = Timestamp(entry);
                session = session with {
                    StartedAtUtc = session.StartedAtUtc ?? timestamp,
                    LastActivityAtUtc = Later(session.LastActivityAtUtc, timestamp)
                };
                var type = Text(entry, "type");
                var payload = Object(entry, "payload");
                if (type == "session_meta") {
                    // A fork can embed its parent's metadata after its own canonical first record.
                    if (hasSessionMetadata) continue;
                    hasSessionMetadata = true;
                    var git = Object(payload, "git");
                    session = session with {
                        Id = Text(payload, "id") ?? Text(payload, "session_id") ?? session.Id,
                        WorkingDirectory = Text(payload, "cwd") ?? session.WorkingDirectory,
                        Branch = Text(git, "branch") ?? session.Branch,
                        Repository = AgentRepositoryIdentity.FromRemote(Text(git, "repository_url")) ?? session.Repository
                    };
                } else if (type == "turn_context") {
                    session = session with { WorkingDirectory = session.WorkingDirectory ?? Text(payload, "cwd") };
                } else if (type == "event_msg") {
                    switch (Text(payload, "type")) {
                        case "task_started": case "turn_started":
                            active = true;
                            inputCallId = null;
                            session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Unknown };
                            break;
                        case "task_complete": case "task_completed": case "turn_complete": case "turn_completed":
                            active = false;
                            var error = Object(payload, "error");
                            session = session with { State = AgentWorkState.Idle, LastTurnOutcome =
                                error.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                                    ? AgentTurnOutcome.Completed : AgentTurnOutcome.Failed };
                            break;
                        case "turn_aborted": case "task_aborted": case "turn_interrupted":
                            active = false;
                            session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Interrupted };
                            break;
                        case "turn_failed": case "task_failed":
                            active = false;
                            session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Failed };
                            break;
                        case "request_user_input": case "approval_requested":
                            active = false;
                            session = session with { State = AgentWorkState.WaitingOnPerson };
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
                    }
                } else if (type == "response_item") {
                    var itemType = Text(payload, "type");
                    var name = Text(payload, "name");
                    if ((itemType == "function_call" || itemType == "custom_tool_call") &&
                        (name == "request_user_input" || name == "functions.request_user_input" || name == "functions.request_user_input_async")) {
                        active = false;
                        inputCallId = Text(payload, "call_id");
                        session = session with { State = AgentWorkState.WaitingOnPerson };
                    } else if ((itemType == "function_call_output" || itemType == "custom_tool_call_output") &&
                        inputCallId is not null && Text(payload, "call_id") == inputCallId) {
                        inputCallId = null;
                        active = true;
                        session = session with { State = AgentWorkState.Idle };
                    }
                }
            }
        }
        return Finish(session, active, now, activeWindow, root);
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
