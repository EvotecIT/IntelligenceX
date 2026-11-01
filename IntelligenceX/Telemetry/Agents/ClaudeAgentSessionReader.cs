using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Telemetry.Usage;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

internal static class ClaudeAgentSessionReader {
    internal static AgentSessionParserState Read(string path, SourceRootRecord root, Action<string> diagnostic, CancellationToken cancellationToken,
        AgentSessionParserState? previous = null, AgentSessionReadWindow? window = null) {
        var session = previous?.Session ?? new AgentSessionState {
            SourceRootId = root.Id,
            Provider = "claude", Id = Path.GetFileNameWithoutExtension(path),
            Machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null)
        };
        var active = previous?.Active ?? false;
        var sidechain = previous?.Sidechain ?? false;
        window ??= new AgentSessionReadWindow();
        window.Checkpoint = previous ?? new AgentSessionParserState { Session = session, Active = active, Sidechain = sidechain };
        foreach (var document in AgentSessionMetadataProjection.Read(path, diagnostic, cancellationToken, window)) {
            using (document) {
                var entry = document.RootElement;
                sidechain |= Object(entry, "isSidechain").ValueKind == JsonValueKind.True;
                var timestamp = Timestamp(entry);
                session = session with {
                    Id = Text(entry, "sessionId") ?? session.Id,
                    WorkingDirectory = Text(entry, "cwd") ?? session.WorkingDirectory,
                    Branch = Text(entry, "gitBranch") ?? session.Branch,
                    StartedAtUtc = session.StartedAtUtc is null || timestamp < session.StartedAtUtc ? timestamp : session.StartedAtUtc,
                    LastActivityAtUtc = session.LastActivityAtUtc is null || timestamp > session.LastActivityAtUtc ? timestamp : session.LastActivityAtUtc
                };
                var type = Text(entry, "type");
                var message = Object(entry, "message");
                if (type == "custom-title") {
                    session = session with { Title = Text(entry, "customTitle") ?? session.Title };
                } else if (type == "user") {
                    active = true;
                    session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Unknown };
                } else if (type == "assistant") {
                    active = true;
                    var stop = Text(message, "stop_reason");
                    if (stop == "end_turn" || stop == "stop_sequence") {
                        active = false;
                        session = session with { State = AgentWorkState.WaitingOnPerson, LastTurnOutcome = AgentTurnOutcome.Completed };
                    } else if (stop == "max_tokens") {
                        active = false;
                        session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Incomplete };
                    }
                    var content = Object(message, "content");
                    if (content.ValueKind == JsonValueKind.Array) {
                        foreach (var block in content.EnumerateArray()) {
                            if (Text(block, "type") == "tool_use" && Text(block, "name") == "AskUserQuestion") {
                                active = false;
                                session = session with { State = AgentWorkState.WaitingOnPerson };
                            }
                        }
                    }
                    if (Object(entry, "isApiErrorMessage").ValueKind == JsonValueKind.True) {
                        active = false;
                        session = session with { State = AgentWorkState.Idle, LastTurnOutcome = AgentTurnOutcome.Failed };
                    }
                } else if (type == "result") {
                    active = false;
                    session = session with {
                        State = AgentWorkState.WaitingOnPerson,
                        LastTurnOutcome = Object(entry, "is_error").ValueKind == JsonValueKind.True
                            ? AgentTurnOutcome.Failed : AgentTurnOutcome.Completed
                    };
                } else if (type == "system" && Text(entry, "subtype") == "turn_duration") {
                    active = false;
                    session = session with { State = AgentWorkState.WaitingOnPerson, LastTurnOutcome = AgentTurnOutcome.Completed };
                }
            }
            if (window.RecordIsComplete) window.Checkpoint = new AgentSessionParserState { Session = session, Active = active, Sidechain = sidechain };
        }
        // Subagents share the parent sessionId. They must not replace its state during deduplication.
        return new AgentSessionParserState { Session = session, Active = active, Sidechain = sidechain };
    }
}
