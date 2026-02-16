using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static SessionPolicyDto BuildSessionPolicy(ServiceOptions options, IEnumerable<IToolPack> packs, IReadOnlyList<string> startupWarnings,
        IReadOnlyList<string> pluginSearchPaths) {
        var roots = options.AllowedRoots.Count == 0 ? Array.Empty<string>() : options.AllowedRoots.ToArray();

        var packList = new List<ToolPackInfoDto>();
        foreach (var pack in ToolPackBootstrap.GetDescriptors(packs)) {
            packList.Add(new ToolPackInfoDto {
                Id = pack.Id,
                Name = ResolvePackDisplayName(pack.Id, pack.Name),
                Tier = MapTier(pack.Tier),
                Enabled = true,
                IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite,
                SourceKind = MapSourceKind(pack.SourceKind, pack.Id)
            });
        }

        var dangerousEnabled = packList.Exists(static p => p.IsDangerous || p.Tier == CapabilityTier.DangerousWrite);

        return new SessionPolicyDto {
            ReadOnly = !dangerousEnabled,
            AllowedRoots = roots,
            Packs = packList.ToArray(),
            DangerousToolsEnabled = dangerousEnabled,
            ToolTimeoutSeconds = options.ToolTimeoutSeconds <= 0 ? null : options.ToolTimeoutSeconds,
            TurnTimeoutSeconds = options.TurnTimeoutSeconds <= 0 ? null : options.TurnTimeoutSeconds,
            MaxToolRounds = options.MaxToolRounds,
            ParallelTools = options.ParallelTools,
            MaxTableRows = options.MaxTableRows <= 0 ? null : options.MaxTableRows,
            MaxSample = options.MaxSample <= 0 ? null : options.MaxSample,
            Redact = options.Redact,
            StartupWarnings = startupWarnings.Count == 0 ? Array.Empty<string>() : startupWarnings.ToArray(),
            PluginSearchPaths = pluginSearchPaths.Count == 0 ? Array.Empty<string>() : pluginSearchPaths.ToArray()
        };
    }

    private static void RecordBootstrapWarning(ICollection<string> sink, string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        sink.Add(normalized);
        Console.Error.WriteLine($"[pack warning] {normalized}");
    }

    private static string[] NormalizeDistinctStrings(IEnumerable<string> values, int maxItems) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }

            if (!dedupe.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
            if (maxItems > 0 && list.Count >= maxItems) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private static CapabilityTier MapTier(ToolCapabilityTier tier) {
        return tier switch {
            ToolCapabilityTier.ReadOnly => CapabilityTier.ReadOnly,
            ToolCapabilityTier.SensitiveRead => CapabilityTier.SensitiveRead,
            ToolCapabilityTier.DangerousWrite => CapabilityTier.DangerousWrite,
            _ => CapabilityTier.SensitiveRead
        };
    }

    private static string ResolvePackDisplayName(string? descriptorId, string? fallbackName) {
        var packId = NormalizePackId(descriptorId);
        if (!string.IsNullOrWhiteSpace(fallbackName)) {
            return fallbackName.Trim();
        }

        return packId;
    }

    private static ToolPackSourceKind MapSourceKind(string? sourceKind, string descriptorId) {
        var normalized = ToolPackBootstrap.NormalizeSourceKind(sourceKind, descriptorId);
        return normalized switch {
            "builtin" => ToolPackSourceKind.Builtin,
            "closed_source" => ToolPackSourceKind.ClosedSource,
            _ => ToolPackSourceKind.OpenSource
        };
    }

    private static string NormalizePackId(string? descriptorId) {
        return ToolPackBootstrap.NormalizePackId(descriptorId);
    }

    private static string? LoadInstructions(ServiceOptions options) {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(options.InstructionsFile)) {
            path = options.InstructionsFile.Trim();
        } else {
            path = Path.Combine(AppContext.BaseDirectory, "HostSystemPrompt.md");
        }

        string? instructions = null;
        try {
            if (File.Exists(path)) {
                var text = File.ReadAllText(path);
                instructions = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        } catch {
            instructions = null;
        }

        var shaping = BuildShapingInstructions(options);
        if (string.IsNullOrWhiteSpace(shaping)) {
            return instructions;
        }
        if (string.IsNullOrWhiteSpace(instructions)) {
            return shaping;
        }
        return instructions + Environment.NewLine + Environment.NewLine + shaping;
    }

    private static string? BuildShapingInstructions(ServiceOptions options) {
        var maxTableRows = options.MaxTableRows;
        var maxSample = options.MaxSample;
        var redact = options.Redact;

        if (maxTableRows <= 0 && maxSample <= 0 && !redact) {
            return null;
        }

        var lines = new List<string> {
            "## Session Response Shaping",
            "Follow these display constraints for all assistant responses:"
        };

        if (maxTableRows > 0) {
            lines.Add($"- Max table rows: {maxTableRows} (show a preview, then offer to paginate/refine).");
        }
        if (maxSample > 0) {
            lines.Add($"- Max sample items: {maxSample} (for long lists, show a sample and counts).");
        }
        if (redact) {
            lines.Add("- Redaction: redact emails/UPNs in assistant output. Prefer summaries over raw identifiers.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static readonly Regex EmailRegex = new(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RedactText(string text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }
        // Best-effort: redact email/UPN-like tokens.
        return EmailRegex.Replace(text, "[redacted_email]");
    }

    private sealed class ChatRun {
        public ChatRun(string chatRequestId, CancellationTokenSource cts) {
            ChatRequestId = chatRequestId;
            Cts = cts;
        }

        public string ChatRequestId { get; }
        public string? ThreadId { get; set; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }
        public bool IsCompleted { get; private set; }

        public void Cancel() {
            try {
                Cts.Cancel();
            } catch {
                // Ignore.
            }
        }

        public void MarkCompleted() {
            IsCompleted = true;
            try {
                Cts.Dispose();
            } catch {
                // Ignore.
            }
        }
    }

    private sealed class LoginFlow {
        private readonly object _lock = new();
        private PendingPrompt? _pending;

        public LoginFlow(string loginId, string requestId, CancellationTokenSource cts) {
            LoginId = loginId;
            RequestId = requestId;
            Cts = cts;
        }

        public string LoginId { get; }
        public string RequestId { get; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }
        public bool IsCompleted { get; private set; }

        public void Cancel() {
            try {
                Cts.Cancel();
            } catch {
                // Ignore.
            }
            lock (_lock) {
                _pending?.Tcs.TrySetCanceled();
            }
        }

        public void MarkCompleted() {
            IsCompleted = true;
            try {
                Cts.Dispose();
            } catch {
                // Ignore.
            }
        }

        public void SetPendingPrompt(string promptId, TaskCompletionSource<string> tcs) {
            lock (_lock) {
                _pending = new PendingPrompt(promptId, tcs);
            }
        }

        public void ClearPendingPrompt(string promptId) {
            lock (_lock) {
                if (_pending is not null && string.Equals(_pending.PromptId, promptId, StringComparison.Ordinal)) {
                    _pending = null;
                }
            }
        }

        public bool TryCompletePrompt(string promptId, string input) {
            lock (_lock) {
                if (_pending is null || !string.Equals(_pending.PromptId, promptId, StringComparison.Ordinal)) {
                    return false;
                }
                return _pending.Tcs.TrySetResult(input);
            }
        }

        private sealed record PendingPrompt(string PromptId, TaskCompletionSource<string> Tcs);
    }
}
