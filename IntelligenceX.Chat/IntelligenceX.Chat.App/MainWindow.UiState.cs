using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private void ClearConversation() {
        var conversation = GetActiveConversation();
        conversation.Messages.Clear();
        conversation.Title = DefaultConversationTitle;
        conversation.ThreadId = null;
        conversation.UpdatedUtc = DateTime.UtcNow;
        _messages = conversation.Messages;
        _assistantStreaming.Clear();
        _threadId = null;
        if (string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            _activeRequestConversationId = null;
        }
        _modelKickoffAttempted = false;
        _modelKickoffInProgress = false;
        _pendingLoginPrompt = null;
        _ = RenderTranscriptAsync();
        _ = PublishOptionsStateAsync();
        _ = PersistAppStateAsync();
    }

    private void AppendSystem(string text) {
        var conversation = GetActiveConversation();
        AppendSystem(conversation, text);
    }

    private void AppendSystem(ConversationRuntime conversation, string text) {
        conversation.Messages.Add(("System", text, DateTime.Now));
        conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            _ = RenderTranscriptAsync();
        }
    }

    private void AppendSystem(SystemNotice notice) {
        AppendSystem(SystemNoticeFormatter.Format(notice));
    }

    private void AppendSystem(ConversationRuntime conversation, SystemNotice notice) {
        AppendSystem(conversation, SystemNoticeFormatter.Format(notice));
    }

    private async Task RenderTranscriptAsync() {
        if (!_webViewReady) {
            return;
        }

        var requestedGeneration = Interlocked.Increment(ref _transcriptRenderGeneration);
        await _transcriptRenderGate.WaitAsync().ConfigureAwait(false);
        try {
            var latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
            if (requestedGeneration < latestGeneration) {
                return;
            }

            if (_isSending && _assistantStreaming.Length > 0) {
                var previousTicks = Interlocked.Read(ref _transcriptLastRenderUtcTicks);
                if (previousTicks > 0) {
                    var elapsedTicks = DateTime.UtcNow.Ticks - previousTicks;
                    var minimumTicks = StreamingTranscriptRenderCadence.Ticks;
                    if (elapsedTicks < minimumTicks) {
                        await Task.Delay(TimeSpan.FromTicks(minimumTicks - elapsedTicks)).ConfigureAwait(false);
                        latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
                        if (requestedGeneration < latestGeneration) {
                            return;
                        }
                    }
                }
            }

            var html = BuildMessagesHtml(_messages, _timestampFormat);
            var json = JsonSerializer.Serialize(html);
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetTranscript(" + json + ");").AsTask()).ConfigureAwait(false);
            Interlocked.Exchange(ref _transcriptLastRenderUtcTicks, DateTime.UtcNow.Ticks);
        } finally {
            _transcriptRenderGate.Release();
        }
    }

    private async Task SetStatusAsync(string text, SessionStatusTone? tone = null, bool? usageLimitSwitchRecommended = null) {
        _statusText = text ?? string.Empty;
        _statusTone = tone ?? InferStatusTone(_statusText);
        _usageLimitSwitchRecommended = usageLimitSwitchRecommended ?? InferUsageLimitSwitchRecommendation(_statusText);
        if (!_webViewReady) {
            return;
        }

        var textJson = JsonSerializer.Serialize(_statusText);
        var toneJson = JsonSerializer.Serialize(MapStatusTone(_statusTone));
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetStatus(" + textJson + "," + toneJson + ");").AsTask())
            .ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

    private Task SetStatusAsync(SessionStatus status) {
        return SetStatusAsync(
            SessionStatusFormatter.Format(status),
            SessionStatusToneResolver.Resolve(status),
            status.Kind == SessionStatusKind.UsageLimitReached);
    }

    private async Task PublishSessionStateAsync() {
        if (!_webViewReady) {
            return;
        }

        var json = JsonSerializer.Serialize(new {
            status = _statusText,
            statusTone = MapStatusTone(_statusTone),
            usageLimitSwitchRecommended = _usageLimitSwitchRecommended,
            queuedPromptPending = !string.IsNullOrWhiteSpace(_queuedPromptAfterLogin),
            connected = _isConnected,
            authenticated = _isAuthenticated,
            loginInProgress = _loginInProgress,
            sending = _isSending,
            cancelable = _isSending && !string.IsNullOrWhiteSpace(_activeTurnRequestId),
            cancelRequested = _isSending && !string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId),
            debugMode = _debugMode,
            windowMaximized = IsWindowMaximized()
        });
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetSessionState(" + json + ");").AsTask()).ConfigureAwait(false);
    }

    private static string MapStatusTone(SessionStatusTone tone) {
        return tone switch {
            SessionStatusTone.Ok => "ok",
            SessionStatusTone.Warn => "warn",
            SessionStatusTone.Bad => "bad",
            _ => "neutral"
        };
    }

    private static SessionStatusTone InferStatusTone(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return SessionStatusTone.Neutral;
        }

        if (normalized.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Bad;
        }

        if (normalized.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("connected", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Ok;
        }

        if (normalized.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("wait", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("open", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("start", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("connecting", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Warn;
        }

        return SessionStatusTone.Neutral;
    }

    private static bool InferUsageLimitSwitchRecommendation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("switch account", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PublishOptionsStateAsync() {
        if (!_webViewReady) {
            return;
        }

        var packs = _sessionPolicy?.Packs is { Length: > 0 }
            ? BuildPackState(_sessionPolicy.Packs)
            : Array.Empty<object>();

        var tools = BuildToolState();
        var conversations = BuildConversationState();
        var json = JsonSerializer.Serialize(new {
            timestampMode = _timestampMode,
            timestampFormat = _timestampFormat,
            export = new {
                saveMode = _exportSaveMode,
                defaultFormat = _exportDefaultFormat,
                lastDirectory = _lastExportDirectory ?? string.Empty
            },
            autonomy = new {
                maxToolRounds = _autonomyMaxToolRounds,
                parallelTools = _autonomyParallelTools,
                turnTimeoutSeconds = _autonomyTurnTimeoutSeconds,
                toolTimeoutSeconds = _autonomyToolTimeoutSeconds
            },
            activeProfileName = _appProfileName,
            profileNames = BuildKnownProfiles(),
            activeConversationId = _activeConversationId,
            conversations,
            profile = new {
                userName = GetEffectiveUserName() ?? string.Empty,
                persona = GetEffectiveAssistantPersona() ?? string.Empty,
                theme = GetEffectiveThemePreset(),
                onboardingCompleted = _appState.OnboardingCompleted,
                sessionOverrides = new {
                    userName = !string.IsNullOrWhiteSpace(_sessionUserNameOverride),
                    persona = !string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride),
                    theme = !string.IsNullOrWhiteSpace(_sessionThemeOverride)
                }
            },
            packs,
            tools,
            policy = _sessionPolicy is null ? null : new {
                readOnly = _sessionPolicy.ReadOnly,
                dangerousToolsEnabled = _sessionPolicy.DangerousToolsEnabled,
                turnTimeoutSeconds = _sessionPolicy.TurnTimeoutSeconds,
                toolTimeoutSeconds = _sessionPolicy.ToolTimeoutSeconds,
                maxToolRounds = _sessionPolicy.MaxToolRounds,
                parallelTools = _sessionPolicy.ParallelTools
            }
        });

        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetOptionsData(" + json + ");").AsTask()).ConfigureAwait(false);
    }

    private object[] BuildConversationState() {
        if (_conversations.Count == 0) {
            return Array.Empty<object>();
        }

        var ordered = new List<ConversationRuntime>(_conversations);
        ordered.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        var list = new List<object>(ordered.Count);
        foreach (var conversation in ordered) {
            var updatedUtc = conversation.UpdatedUtc == default ? DateTime.UtcNow : conversation.UpdatedUtc;
            var updatedLocal = EnsureUtc(updatedUtc).ToLocalTime();
            var preview = string.Empty;
            for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
                var text = (conversation.Messages[i].Text ?? string.Empty).Trim();
                if (text.Length == 0) {
                    continue;
                }

                preview = BuildConversationTitleFromText(text);
                break;
            }

            list.Add(new {
                id = conversation.Id,
                title = string.IsNullOrWhiteSpace(conversation.Title) ? DefaultConversationTitle : conversation.Title,
                messageCount = conversation.Messages.Count,
                preview,
                isActive = string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase),
                updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture)
            });
        }

        return list.ToArray();
    }

    private static object[] BuildPackState(ToolPackInfoDto[] packs) {
        var list = new List<object>(packs.Length);
        foreach (var pack in packs) {
            var normalizedPackId = NormalizePackId(pack.Id);
            list.Add(new {
                id = string.IsNullOrWhiteSpace(normalizedPackId) ? pack.Id : normalizedPackId,
                name = ResolvePackDisplayName(normalizedPackId, pack.Name),
                tier = pack.Tier.ToString(),
                enabled = pack.Enabled,
                isDangerous = pack.IsDangerous,
                sourceKind = pack.SourceKind switch {
                    ToolPackSourceKind.Builtin => "builtin",
                    ToolPackSourceKind.ClosedSource => "closed_source",
                    _ => "open_source"
                }
            });
        }
        return list.ToArray();
    }

    private static string ResolvePackDisplayName(string? id, string? fallbackName) {
        var normalized = NormalizePackId(id);
        return normalized switch {
            "system" => "ComputerX",
            "ad" => "ADPlayground",
            "testimox" => "TestimoX",
            _ => string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim()
        };
    }

    private object[] BuildToolState() {
        if (_toolStates.Count == 0) {
            return Array.Empty<object>();
        }

        var names = new List<string>(_toolStates.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        var list = new List<object>(names.Count);
        foreach (var name in names) {
            _toolDescriptions.TryGetValue(name, out var description);
            _toolDisplayNames.TryGetValue(name, out var displayName);
            _toolCategories.TryGetValue(name, out var category);
            _toolTags.TryGetValue(name, out var tags);
            _toolPackIds.TryGetValue(name, out var packId);
            _toolPackNames.TryGetValue(name, out var packName);
            _toolStates.TryGetValue(name, out var enabled);
            var normalizedPackId = NormalizePackId(packId);
            var normalizedPackName = ResolvePackDisplayName(normalizedPackId, packName);
            list.Add(new {
                name,
                displayName = string.IsNullOrWhiteSpace(displayName) ? FormatToolDisplayName(name) : displayName,
                description = description ?? string.Empty,
                category = string.IsNullOrWhiteSpace(category) ? InferToolCategory(name) : category,
                packId = string.IsNullOrWhiteSpace(normalizedPackId) ? null : normalizedPackId,
                packName = string.IsNullOrWhiteSpace(normalizedPackName) ? null : normalizedPackName,
                tags = tags ?? Array.Empty<string>(),
                enabled
            });
        }

        return list.ToArray();
    }

    private async Task SetActivityAsync(string? text) {
        if (!_webViewReady) {
            return;
        }

        var json = JsonSerializer.Serialize(text ?? string.Empty);
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetActivity(" + json + ");").AsTask()).ConfigureAwait(false);
    }

    private string FormatActivityText(ChatStatusMessage status) {
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            return status.Message!;
        }

        var toolLabel = string.IsNullOrWhiteSpace(status.ToolName)
            ? string.Empty
            : ResolveToolActivityName(status.ToolName!);

        return status.Status switch {
            "thinking" => "Thinking...",
            "tool_call" when toolLabel.Length > 0 => "Preparing " + toolLabel + "...",
            "tool_running" when toolLabel.Length > 0 => "Running " + toolLabel + "...",
            "tool_completed" when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " done (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " done",
            _ => string.IsNullOrWhiteSpace(status.Status)
                ? "Working..."
                : char.ToUpperInvariant(status.Status[0]) + status.Status[1..]
        };
    }

    private string ResolveToolActivityName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "tool";
        }

        if (_toolDisplayNames.TryGetValue(normalized, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
            return displayName.Trim();
        }

        return FormatToolDisplayName(normalized);
    }

    private static string FormatDuration(long durationMs) {
        if (durationMs >= 1000) {
            return (durationMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        return durationMs.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    private static string FormatStatusTrace(ChatStatusMessage status) {
        var text = $"status: {status.Status}"
                   + (string.IsNullOrWhiteSpace(status.ToolName) ? string.Empty : $" tool={status.ToolName}")
                   + (status.DurationMs is null ? string.Empty : $" {status.DurationMs}ms");
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            text += $" msg={status.Message}";
        }

        return text;
    }

}
