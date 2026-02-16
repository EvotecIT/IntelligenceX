using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Theming;
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
    private async Task PersistAppStateAsync() {
        if (!_appStateLoaded) {
            return;
        }

        await _stateWriteGate.WaitAsync().ConfigureAwait(false);
        try {
            var activeConversation = GetActiveConversation();
            _appState.ProfileName = _appProfileName;
            _appState.TimestampMode = _timestampMode;
            _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
            _appState.AutonomyParallelTools = _autonomyParallelTools;
            _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
            _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
            _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
            _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
            _appState.ExportSaveMode = _exportSaveMode;
            _appState.ExportDefaultFormat = _exportDefaultFormat;
            _appState.ExportLastDirectory = _lastExportDirectory;
            _appState.PersistentMemoryEnabled = _persistentMemoryEnabled;
            _appState.LocalProviderTransport = _localProviderTransport;
            _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
            _appState.LocalProviderModel = _localProviderModel;
            _appState.MemoryFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
            _appState.ActiveConversationId = _activeConversationId;
            _appState.ThreadId = activeConversation.ThreadId;
            if (string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
                _appState.ThemePreset = _themePreset;
            }
            _appState.DisabledTools = BuildDisabledToolsList();
            _appState.Messages = BuildMessageStateSnapshot(activeConversation.Messages);
            _appState.Conversations = BuildConversationStateSnapshot();
            await _stateStore.UpsertAsync(_appProfileName, _appState, CancellationToken.None).ConfigureAwait(false);
            _knownProfiles.Add(_appProfileName);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.StateSaveFailed(ex.Message));
            }
        } finally {
            _stateWriteGate.Release();
        }
    }

    private void QueuePersistAppState() {
        if (!_appStateLoaded) {
            return;
        }

        CancellationTokenSource cts;
        lock (_persistDebounceSync) {
            _persistDebounceCts?.Cancel();
            _persistDebounceCts?.Dispose();
            _persistDebounceCts = new CancellationTokenSource();
            cts = _persistDebounceCts;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(PersistDebounceInterval, cts.Token).ConfigureAwait(false);
                await PersistAppStateAsync().ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected when a newer update supersedes this queued save.
            } catch (ObjectDisposedException) {
                // Best-effort background save path during shutdown.
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    try {
                        await RunOnUiThreadAsync(() => {
                            AppendSystem(SystemNotice.StateSaveFailed(ex.Message));
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                    } catch {
                        // Best-effort diagnostics only.
                    }
                }
            }
        });
    }

    private void CancelQueuedPersistAppState() {
        CancellationTokenSource? cts;
        lock (_persistDebounceSync) {
            cts = _persistDebounceCts;
            _persistDebounceCts = null;
        }

        if (cts is null) {
            return;
        }

        try {
            cts.Cancel();
        } finally {
            cts.Dispose();
        }
    }

    private static List<string> BuildDisabledToolsList(Dictionary<string, bool> toolStates) {
        var list = new List<string>();
        foreach (var pair in toolStates) {
            if (!pair.Value) {
                list.Add(pair.Key);
            }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private List<string> BuildDisabledToolsList() {
        return BuildDisabledToolsList(_toolStates);
    }

    private static List<ChatMessageState> BuildMessageStateSnapshot(List<(string Role, string Text, DateTime Time)> messages) {
        var result = new List<ChatMessageState>(Math.Min(messages.Count, MaxMessagesPerConversation));
        var start = Math.Max(0, messages.Count - MaxMessagesPerConversation);
        for (var i = start; i < messages.Count; i++) {
            var m = messages[i];
            if (string.IsNullOrWhiteSpace(m.Text)) {
                continue;
            }

            result.Add(new ChatMessageState {
                Role = m.Role,
                Text = m.Text,
                TimeUtc = m.Time.ToUniversalTime()
            });
        }

        return result;
    }

    private List<ChatConversationState> BuildConversationStateSnapshot() {
        if (_conversations.Count == 0) {
            return new List<ChatConversationState>();
        }

        var ordered = new List<ConversationRuntime>(_conversations);
        ordered.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        if (ordered.Count > MaxConversations) {
            ordered.RemoveRange(MaxConversations, ordered.Count - MaxConversations);
        }

        var conversations = new List<ChatConversationState>(ordered.Count);
        foreach (var conversation in ordered) {
            var title = ComputeConversationTitle(conversation.Title, conversation.Messages);
            var updatedUtc = conversation.UpdatedUtc == default
                ? (conversation.Messages.Count > 0 ? conversation.Messages[^1].Time.ToUniversalTime() : DateTime.UtcNow)
                : EnsureUtc(conversation.UpdatedUtc);
            conversations.Add(new ChatConversationState {
                Id = conversation.Id,
                Title = title,
                ThreadId = conversation.ThreadId,
                Messages = BuildMessageStateSnapshot(conversation.Messages),
                UpdatedUtc = updatedUtc
            });
        }

        return conversations;
    }

    private string NextId() {
        return Interlocked.Increment(ref _nextRequestId).ToString();
    }

    private static async Task ConnectClientWithTimeoutAsync(ChatServiceClient client, string pipeName, TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        await client.ConnectAsync(pipeName, cts.Token).ConfigureAwait(true);
    }

    private static string FormatConnectError(Exception ex) {
        return ex is OperationCanceledException ? "Timed out waiting for service pipe." : ex.Message;
    }

    private static bool IsDisconnectedError(Exception ex) {
        if (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException) {
            return true;
        }

        if (ex is InvalidOperationException inv && inv.Message.Contains("Not connected", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return ex.Message.Contains("Disconnected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsageLimitError(Exception ex) {
        var message = ex.Message ?? string.Empty;
        return message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
               || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || message.Contains("(429)", StringComparison.OrdinalIgnoreCase)
               || message.Contains(" 429", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCanceledTurn(string requestId, Exception ex) {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId)) {
            return false;
        }

        if (!string.Equals(requestId, _cancelRequestedTurnRequestId, StringComparison.Ordinal)) {
            return false;
        }

        if (ex is OperationCanceledException) {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("chat_canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime EnsureUtc(DateTime value) {
        if (value == default) {
            return DateTime.UtcNow;
        }

        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root) {
        root = default;
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }
            root = doc.RootElement.Clone();
            return true;
        } catch {
            return false;
        }
    }

    private static string? TryGetString(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static bool? TryGetBoolean(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.True) {
            return true;
        }

        if (el.ValueKind == JsonValueKind.False) {
            return false;
        }

        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) {
            return parsed;
        }

        return null;
    }

    private static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var v = value.Trim();
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAppProfileName(string? value) {
        var name = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }

    private static string? NormalizeTheme(string? value) {
        return ThemeContract.Normalize(value);
    }

    private static string ResolveTimestampMode(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "seconds";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "minutes";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "seconds";
        }

        return "custom";
    }

    private static string ResolveTimestampFormat(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "HH:mm:ss";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm:ss";
        }

        try {
            _ = DateTime.Now.ToString(normalized, CultureInfo.InvariantCulture);
            return normalized;
        } catch {
            return "HH:mm:ss";
        }
    }

    private static int? NormalizeAutonomyInt(int? value, int min, int max) {
        if (!value.HasValue) {
            return null;
        }

        var v = value.Value;
        if (v < min || v > max) {
            return null;
        }

        return v;
    }

    private void MinimizeWindow() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped) {
                overlapped.Minimize();
            }
        } catch {
            // Ignore.
        }
    }

    private void ToggleMaximizeWindow() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped) {
                if (overlapped.State == OverlappedPresenterState.Maximized) {
                    overlapped.Restore();
                } else {
                    overlapped.Maximize();
                }
            }
        } catch {
            // Ignore.
        }
    }

    private bool IsWindowMaximized() {
        try {
            return AppWindow?.Presenter is OverlappedPresenter overlapped
                   && overlapped.State == OverlappedPresenterState.Maximized;
        } catch {
            return false;
        }
    }

    private void BeginDragMoveWindow() {
        try {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) {
                return;
            }

            ReleaseCapture();
            _ = SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        } catch {
            // Ignore.
        }
    }

    private Task RunOnUiThreadAsync(Func<Task> work) {
        if (_dispatcher.HasThreadAccess) {
            return work();
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () => {
            try {
                await work().ConfigureAwait(false);
                tcs.TrySetResult(null);
            } catch (Exception ex) {
                tcs.TrySetException(ex);
            }
        })) {
            tcs.TrySetException(new InvalidOperationException("Failed to dispatch work to UI thread."));
        }

        return tcs.Task;
    }

    private static string? ResolveServiceSourceDirectory() {
        var bestDir = string.Empty;
        var bestTicks = long.MinValue;

        TryPick(Path.Combine(AppContext.BaseDirectory, "service"), ref bestDir, ref bestTicks);
        TryPick(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service")), ref bestDir, ref bestTicks);

        return string.IsNullOrWhiteSpace(bestDir) ? null : bestDir;
    }

    private static void TryPick(string dir, ref string bestDir, ref long bestTicks) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return;
        }

        var exe = Path.Combine(dir, "IntelligenceX.Chat.Service.exe");
        var dll = Path.Combine(dir, "IntelligenceX.Chat.Service.dll");
        if (!File.Exists(exe) && !File.Exists(dll)) {
            return;
        }

        var marker = File.Exists(dll) ? dll : exe;
        long ticks;
        try {
            ticks = File.GetLastWriteTimeUtc(marker).Ticks;
        } catch {
            ticks = long.MinValue;
        }

        if (ticks > bestTicks) {
            bestTicks = ticks;
            bestDir = dir;
        }
    }

    private string? EnsureStagedServiceDirectory(string serviceSourceDir) {
        if (string.IsNullOrWhiteSpace(serviceSourceDir) || !Directory.Exists(serviceSourceDir)) {
            return null;
        }

        try {
            var runtimeRoot = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "service-runtime");
            var stageKey = BuildServiceStageKey(serviceSourceDir);
            var stagedDir = Path.Combine(runtimeRoot, stageKey);

            if (!string.IsNullOrWhiteSpace(_stagedServiceDir)
                && PathsEqual(_stagedServiceDir, stagedDir)
                && HasServicePayload(_stagedServiceDir)) {
                TouchDirectory(_stagedServiceDir);
                return _stagedServiceDir;
            }

            Directory.CreateDirectory(runtimeRoot);
            if (!HasServicePayload(stagedDir)) {
                var tempDir = stagedDir + ".tmp-" + Guid.NewGuid().ToString("N");
                DirectoryCopy(serviceSourceDir, tempDir);

                if (!Directory.Exists(stagedDir)) {
                    Directory.Move(tempDir, stagedDir);
                } else if (Directory.Exists(tempDir)) {
                    Directory.Delete(tempDir, recursive: true);
                }
            }

            if (!HasServicePayload(stagedDir)) {
                return null;
            }

            _stagedServiceDir = stagedDir;
            TouchDirectory(stagedDir);
            CleanupStaleServiceStaging(runtimeRoot, stagedDir);
            return stagedDir;
        } catch (Exception ex) {
            AppendSystem(SystemNotice.ServiceStagingError(ex.Message));
            return null;
        }
    }

    private static void DirectoryCopy(string sourceDir, string destinationDir) {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationDir, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent)) {
                Directory.CreateDirectory(parent);
            }
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool HasServicePayload(string? dir) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return false;
        }

        return File.Exists(Path.Combine(dir, "IntelligenceX.Chat.Service.exe"))
               || File.Exists(Path.Combine(dir, "IntelligenceX.Chat.Service.dll"));
    }

    private static string BuildServiceStageKey(string serviceSourceDir) {
        var dll = Path.Combine(serviceSourceDir, "IntelligenceX.Chat.Service.dll");
        var exe = Path.Combine(serviceSourceDir, "IntelligenceX.Chat.Service.exe");
        var marker = File.Exists(dll) ? dll : exe;

        long ticks = 0;
        long length = 0;
        try {
            var info = new FileInfo(marker);
            ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            length = info.Exists ? info.Length : 0;
        } catch {
            // Ignore and keep defaults.
        }

        var fingerprint = Path.GetFullPath(serviceSourceDir).ToUpperInvariant()
                         + "|"
                         + Path.GetFileName(marker).ToUpperInvariant()
                         + "|"
                         + ticks.ToString(CultureInfo.InvariantCulture)
                         + "|"
                         + length.ToString(CultureInfo.InvariantCulture);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
        var key = Convert.ToHexString(hash.AsSpan(0, 8));
        return "v1-" + key.ToLowerInvariant();
    }

    private static bool PathsEqual(string left, string right) {
        try {
            var l = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var r = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    private static void TouchDirectory(string? dir) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return;
        }

        try {
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow);
        } catch {
            // Ignore.
        }
    }

    private static void CleanupStaleServiceStaging(string runtimeRoot, string keepDir) {
        try {
            if (!Directory.Exists(runtimeRoot)) {
                return;
            }

            var keep = Path.GetFullPath(keepDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dirs = new List<DirectoryInfo>(new DirectoryInfo(runtimeRoot).EnumerateDirectories());
            dirs.Sort(static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            var retained = 0;
            for (var i = 0; i < dirs.Count; i++) {
                var dir = dirs[i];
                var fullPath = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (dir.Name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase)) {
                    if ((DateTime.UtcNow - dir.LastWriteTimeUtc) > TimeSpan.FromMinutes(10)) {
                        TryDeleteDirectory(fullPath);
                    }
                    continue;
                }

                if (string.Equals(fullPath, keep, StringComparison.OrdinalIgnoreCase)) {
                    retained++;
                    continue;
                }

                if (retained < 3) {
                    retained++;
                    continue;
                }

                TryDeleteDirectory(fullPath);
            }
        } catch {
            // Ignore cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string dir) {
        try {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        } catch {
            // Ignore.
        }
    }

    private string BuildToolRunMarkdown(ToolRunDto tools) {
        return ToolRunMarkdownFormatter.Format(tools, ResolveToolDisplayName);
    }

    private string ResolveToolDisplayName(string? name) {
        if (!string.IsNullOrWhiteSpace(name)) {
            var key = name.Trim();
            if (_toolDisplayNames.TryGetValue(key, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
                return displayName;
            }
        }

        return FormatToolDisplayName(name);
    }

    private static string FormatToolDisplayName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return "Tool";
        }

        var tokens = name.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) {
            return name;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++) {
            var token = tokens[i];
            var upper = token.ToUpperInvariant();
            var segment = upper switch {
                "AD" => "AD",
                "DN" => "DN",
                "LDAP" => "LDAP",
                "CSV" => "CSV",
                "TSV" => "TSV",
                "CPU" => "CPU",
                "ID" => "ID",
                "GUID" => "GUID",
                "DNS" => "DNS",
                "OU" => "OU",
                _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant())
            };

            if (i > 0) {
                sb.Append(' ');
            }
            sb.Append(segment);
        }

        return sb.ToString();
    }

    private static string[] NormalizeTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }

            set.Add(tag.Trim());
        }

        if (set.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static string InferToolCategory(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "general";
        }

        var idx = toolName.IndexOf('_');
        if (idx <= 0) {
            return "general";
        }

        var prefix = toolName.Substring(0, idx);
        return prefix.ToLowerInvariant() switch {
            "ad" => "active-directory",
            "eventlog" => "event-log",
            "system" => "system",
            "fs" => "file-system",
            "email" => "email",
            "wsl" => "system",
            _ => "general"
        };
    }

    private string[] BuildKnownProfiles() {
        var set = new HashSet<string>(_knownProfiles, StringComparer.OrdinalIgnoreCase) { _appProfileName };
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

}
