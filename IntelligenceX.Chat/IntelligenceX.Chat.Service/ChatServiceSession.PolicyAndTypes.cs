using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    internal static SessionPolicyDto BuildSessionPolicy(ServiceOptions options, IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        IReadOnlyList<string> startupWarnings, IReadOnlyList<string> pluginSearchPaths, ToolRuntimePolicyDiagnostics runtimePolicy) {
        var roots = options.AllowedRoots.Count == 0 ? Array.Empty<string>() : options.AllowedRoots.ToArray();

        var packList = new List<ToolPackInfoDto>();
        foreach (var pack in packAvailability) {
            var normalizedDisabledReason = string.IsNullOrWhiteSpace(pack.DisabledReason)
                ? null
                : pack.DisabledReason.Trim();
            packList.Add(new ToolPackInfoDto {
                Id = pack.Id,
                Name = ResolvePackDisplayName(pack.Id, pack.Name),
                Description = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
                Tier = MapTier(pack.Tier),
                Enabled = pack.Enabled,
                DisabledReason = pack.Enabled ? null : normalizedDisabledReason,
                IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite,
                SourceKind = MapSourceKind(pack.SourceKind, pack.Id)
            });
        }
        packList.Sort(static (a, b) => {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) {
                return byName;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        var dangerousEnabled = packList.Exists(static p => p.Enabled && (p.IsDangerous || p.Tier == CapabilityTier.DangerousWrite));

        return new SessionPolicyDto {
            ReadOnly = !dangerousEnabled,
            AllowedRoots = roots,
            Packs = packList.ToArray(),
            DangerousToolsEnabled = dangerousEnabled,
            ToolTimeoutSeconds = options.ToolTimeoutSeconds <= 0 ? null : options.ToolTimeoutSeconds,
            TurnTimeoutSeconds = options.TurnTimeoutSeconds <= 0 ? null : options.TurnTimeoutSeconds,
            MaxToolRounds = Math.Clamp(options.MaxToolRounds, 1, ChatRequestOptionLimits.MaxToolRounds),
            ParallelTools = options.ParallelTools,
            AllowMutatingParallelToolCalls = options.AllowMutatingParallelToolCalls,
            MaxTableRows = options.MaxTableRows <= 0 ? null : options.MaxTableRows,
            MaxSample = options.MaxSample <= 0 ? null : options.MaxSample,
            Redact = options.Redact,
            StartupWarnings = startupWarnings.Count == 0 ? Array.Empty<string>() : startupWarnings.ToArray(),
            PluginSearchPaths = pluginSearchPaths.Count == 0 ? Array.Empty<string>() : pluginSearchPaths.ToArray(),
            RuntimePolicy = new SessionRuntimePolicyDto {
                WriteGovernanceMode = ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(runtimePolicy.WriteGovernanceMode),
                RequireWriteGovernanceRuntime = runtimePolicy.RequireWriteGovernanceRuntime,
                WriteGovernanceRuntimeConfigured = runtimePolicy.WriteGovernanceRuntimeConfigured,
                RequireWriteAuditSinkForWriteOperations = runtimePolicy.RequireWriteAuditSinkForWriteOperations,
                WriteAuditSinkMode = ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(runtimePolicy.WriteAuditSinkMode),
                WriteAuditSinkConfigured = runtimePolicy.WriteAuditSinkConfigured,
                WriteAuditSinkPath = runtimePolicy.WriteAuditSinkPath,
                AuthenticationRuntimePreset = ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(runtimePolicy.AuthenticationPreset),
                RequireAuthenticationRuntime = runtimePolicy.RequireAuthenticationRuntime,
                AuthenticationRuntimeConfigured = runtimePolicy.AuthenticationRuntimeConfigured,
                RequireSuccessfulSmtpProbeForSend = runtimePolicy.RequireSuccessfulSmtpProbeForSend,
                SmtpProbeMaxAgeSeconds = runtimePolicy.SmtpProbeMaxAgeSeconds,
                RunAsProfilePath = runtimePolicy.RunAsProfilePath,
                AuthenticationProfilePath = runtimePolicy.AuthenticationProfilePath
            }
        };
    }

    private static ToolRuntimePolicyOptions BuildRuntimePolicyOptions(ServiceOptions options) {
        return ToolRuntimePolicyBootstrap.CreateOptions(options);
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

        return ToolResponseShaping.AppendSessionResponseShapingInstructions(
            instructions,
            options.MaxTableRows,
            options.MaxSample,
            options.Redact);
    }

    private static string RedactText(string text) {
        return ToolResponseShaping.RedactEmailLikeTokens(text);
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
