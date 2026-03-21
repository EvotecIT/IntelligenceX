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
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        IReadOnlyList<string> startupWarnings, SessionStartupBootstrapTelemetryDto? startupBootstrap, IReadOnlyList<string> pluginSearchPaths,
        ToolRuntimePolicyDiagnostics runtimePolicy, ToolRoutingCatalogDiagnostics? routingCatalog = null,
        IReadOnlyList<string>? connectedRuntimeSkills = null,
        IReadOnlyList<string>? healthyToolNames = null, string? remoteReachabilityMode = null,
        ToolOrchestrationCatalog? orchestrationCatalog = null,
        SessionCapabilitySnapshotDto? capabilitySnapshot = null,
        IReadOnlyList<ToolPluginCatalogInfo>? pluginCatalog = null) {
        var roots = options.AllowedRoots.Count == 0 ? Array.Empty<string>() : options.AllowedRoots.ToArray();

        var resolvedCapabilitySnapshot = capabilitySnapshot ?? BuildCapabilitySnapshot(
            options,
            toolDefinitions: null,
            packAvailability,
            pluginAvailability,
            routingCatalog,
            orchestrationCatalog,
            connectedRuntimeSkills,
            healthyToolNames,
            remoteReachabilityMode,
            pluginCatalog: pluginCatalog);
        var packList = BuildPackPolicyList(packAvailability, orchestrationCatalog);

        var pluginList = BuildPluginPolicyList(pluginAvailability, packList, pluginCatalog);

        var dangerousEnabled = resolvedCapabilitySnapshot.DangerousToolsEnabled
            || Array.Exists(
                packList,
                static p => p.Enabled && (p.IsDangerous || p.Tier == CapabilityTier.DangerousWrite));

        return new SessionPolicyDto {
            ReadOnly = !dangerousEnabled,
            AllowedRoots = roots,
            Packs = packList.ToArray(),
            Plugins = pluginList,
            DangerousToolsEnabled = dangerousEnabled,
            ToolTimeoutSeconds = options.ToolTimeoutSeconds <= 0 ? null : options.ToolTimeoutSeconds,
            TurnTimeoutSeconds = options.TurnTimeoutSeconds <= 0 ? null : options.TurnTimeoutSeconds,
            MaxToolRounds = Math.Clamp(options.MaxToolRounds, ChatRequestOptionLimits.MinToolRounds, ChatRequestOptionLimits.MaxToolRounds),
            ParallelTools = options.ParallelTools,
            AllowMutatingParallelToolCalls = options.AllowMutatingParallelToolCalls,
            MaxTableRows = options.MaxTableRows <= 0 ? null : options.MaxTableRows,
            MaxSample = options.MaxSample <= 0 ? null : options.MaxSample,
            Redact = options.Redact,
            StartupWarnings = startupWarnings.Count == 0 ? Array.Empty<string>() : startupWarnings.ToArray(),
            StartupBootstrap = startupBootstrap,
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
                RequireExplicitRoutingMetadata = runtimePolicy.RequireExplicitRoutingMetadata,
                RequireAuthenticationRuntime = runtimePolicy.RequireAuthenticationRuntime,
                AuthenticationRuntimeConfigured = runtimePolicy.AuthenticationRuntimeConfigured,
                RequireSuccessfulSmtpProbeForSend = runtimePolicy.RequireSuccessfulSmtpProbeForSend,
                SmtpProbeMaxAgeSeconds = runtimePolicy.SmtpProbeMaxAgeSeconds,
                RunAsProfilePath = runtimePolicy.RunAsProfilePath,
                AuthenticationProfilePath = runtimePolicy.AuthenticationProfilePath
            },
            RoutingCatalog = MapRoutingCatalogDiagnostics(routingCatalog),
            CapabilitySnapshot = resolvedCapabilitySnapshot
        };
    }

    private static ToolPackInfoDto[] BuildPackPolicyList(
        IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        return ToolCatalogExportBuilder.BuildPackInfoDtos(packAvailability, orchestrationCatalog);
    }

    private static PluginInfoDto[] BuildPluginPolicyList(
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        IReadOnlyList<ToolPackInfoDto> packList,
        IReadOnlyList<ToolPluginCatalogInfo>? pluginCatalog) {
        return ToolCatalogExportBuilder.BuildPluginInfoDtos(pluginAvailability, packList, pluginCatalog);
    }

    private static SessionRoutingCatalogDiagnosticsDto? MapRoutingCatalogDiagnostics(ToolRoutingCatalogDiagnostics? diagnostics) {
        return ToolCatalogExportBuilder.BuildRoutingCatalogDiagnosticsDto(diagnostics);
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
        private int _turnTimeoutCancellationMarked;

        public ChatRun(string chatRequestId, CancellationTokenSource cts, IntelligenceXClient client, StreamWriter writer, ChatRequest request) {
            ChatRequestId = chatRequestId;
            Cts = cts;
            Client = client;
            Writer = writer;
            Request = request;
        }

        public string ChatRequestId { get; }
        public string? ThreadId { get; set; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }
        public bool IsCompleted { get; private set; }
        public bool Started { get; private set; }
        public bool TurnTimeoutCancellationMarked => Volatile.Read(ref _turnTimeoutCancellationMarked) == 1;
        public long EnqueuedUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public int QueuePositionAtEnqueue { get; set; } = 1;
        public IntelligenceXClient Client { get; }
        public StreamWriter Writer { get; }
        public ChatRequest Request { get; }

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

        public void MarkStarted() {
            Started = true;
        }

        public void MarkTurnTimeoutCancellation() {
            Interlocked.Exchange(ref _turnTimeoutCancellationMarked, 1);
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
