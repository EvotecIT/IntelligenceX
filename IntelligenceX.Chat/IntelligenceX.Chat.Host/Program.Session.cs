using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private sealed partial class ReplSession {
        private const int MaxNoToolExecutionRetries = 3;
        private const int ScenarioForcedToolChoiceRetryThreshold = 2;
        private const int MaxModelPhaseAttempts = 2;
        private const int ModelPhaseRetryBaseDelayMs = 350;
        private const int MaxRecentHostTargets = 96;
        private const int MaxRetryPromptHostTargets = 16;
        private const int MaxAutoFilledToolTargets = 4;
        private const int HostTargetSpecificityFqdnBonus = 4;
        private const int HostTargetSpecificityShortNameBonus = 1;
        private const int HostTargetSpecificityIpLiteralPenalty = 2;
        private const int HostTargetSpecificityLocalhostPenalty = 3;
        private const string ScenarioExecutionContractMarker = "[Scenario execution contract]";
        private const string ScenarioExecutionContractDirectiveMarker = "ix:scenario-execution:v1";
        private readonly IntelligenceXClient _client;
        private readonly ToolRegistry _registry;
        private readonly ToolOrchestrationCatalog? _orchestrationCatalog;
        private readonly ReplOptions _options;
        private readonly string? _instructions;
        private readonly Action<string>? _status;
        private readonly List<string> _recentHostTargets = new();
        private readonly List<string> _recentAdHostTargets = new();
        private readonly List<string> _recentPublicHostTargets = new();
        private readonly ConcurrentDictionary<string, string> _sessionToolOutputCache = new(StringComparer.Ordinal);
        private string? _previousResponseId;
        private string _preferredDomainIntentFamily = string.Empty;
        private string[] _pendingDomainIntentClarificationFamilies = Array.Empty<string>();
        private long _pendingDomainIntentClarificationSeenUtcTicks;
        private string _rememberedAdDomainTarget = string.Empty;
        private string _rememberedPublicDomainTarget = string.Empty;

        public ReplSession(
            IntelligenceXClient client,
            ToolRegistry registry,
            ReplOptions options,
            string? instructions,
            Action<string>? status,
            ToolOrchestrationCatalog? orchestrationCatalog = null) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
            _status = status;
            _orchestrationCatalog = orchestrationCatalog;
        }

        public void ResetThread() {
            _previousResponseId = null;
            _recentHostTargets.Clear();
            _recentAdHostTargets.Clear();
            _recentPublicHostTargets.Clear();
            _preferredDomainIntentFamily = string.Empty;
            _pendingDomainIntentClarificationFamilies = Array.Empty<string>();
            _pendingDomainIntentClarificationSeenUtcTicks = 0;
            _rememberedAdDomainTarget = string.Empty;
            _rememberedPublicDomainTarget = string.Empty;
        }

        internal IReadOnlyList<ToolDefinition> GetToolDefinitionsSnapshot() {
            return _registry.GetDefinitions();
        }

        internal ToolOrchestrationCatalog? GetToolOrchestrationCatalogSnapshot() {
            return _orchestrationCatalog;
        }

        public async Task<ReplTurnResult> AskAsync(string text, CancellationToken cancellationToken) {
            var withMetrics = await AskWithMetricsAsync(text, cancellationToken).ConfigureAwait(false);
            return withMetrics.Result;
        }

        public async Task<ReplTurnMetricsResult> AskWithMetricsAsync(string text, CancellationToken cancellationToken) {
            var startedAtUtc = DateTime.UtcNow;
            var firstDeltaUtcTicks = 0L;

            void OnDelta(object? _, string __) {
                if (firstDeltaUtcTicks != 0) {
                    return;
                }
                _ = Interlocked.CompareExchange(ref firstDeltaUtcTicks, DateTime.UtcNow.Ticks, 0);
            }

            _client.DeltaReceived += OnDelta;
            try {
                var result = await AskCoreAsync(text, cancellationToken).ConfigureAwait(false);
                var completedAtUtc = DateTime.UtcNow;
                DateTime? firstDeltaAtUtc = null;
                long? ttftMs = null;
                if (firstDeltaUtcTicks != 0) {
                    firstDeltaAtUtc = new DateTime(firstDeltaUtcTicks, DateTimeKind.Utc);
                    ttftMs = (long)Math.Max(0, TimeSpan.FromTicks(firstDeltaUtcTicks - startedAtUtc.Ticks).TotalMilliseconds);
                }

                var metrics = new ReplTurnMetrics(
                    startedAtUtc,
                    firstDeltaAtUtc,
                    completedAtUtc,
                    durationMs: (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds),
                    ttftMs: ttftMs,
                    usage: result.Usage,
                    toolCallsCount: result.ToolCalls.Count,
                    toolRounds: result.ToolRounds,
                    noToolExecutionRetries: result.NoToolExecutionRetries);
                return new ReplTurnMetricsResult(result, metrics);
            } finally {
                _client.DeltaReceived -= OnDelta;
            }
        }

        private async Task<ReplTurnResult> AskCoreAsync(string text, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(text)) {
                return new ReplTurnResult(string.Empty, Array.Empty<ToolCall>(), Array.Empty<ToolOutput>());
            }

            using var turnCts = CreateTimeoutCts(cancellationToken, _options.TurnTimeoutSeconds);
            var turnToken = turnCts?.Token ?? cancellationToken;

            var protocolCalls = new List<ToolCall>();
            var protocolOutputs = new List<ToolOutput>();
            var reportedCalls = new List<ToolCall>();
            var reportedOutputs = new List<ToolOutput>();
            var reportedReadOnlySignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var turnReadOnlyOutputBySignature = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var toolRounds = 0;
            var noToolExecutionRetryCount = 0;
            var noTextToolOutputDirectRetryUsed = false;
            var adEventLogPlatformFallbackUsed = false;

            var toolDefs = _registry.GetDefinitions();
            var runtimeSelfReportAnalysis = RuntimeSelfReportTurnClassifier.Analyze(text);
            var inputText = RuntimeSelfReportSupport.LooksLikeCompactRuntimeSelfReportQuestion(runtimeSelfReportAnalysis)
                ? RuntimeSelfReportSupport.BuildCompactRuntimeSelfReportInput(runtimeSelfReportAnalysis, _options.OpenAITransport, _options.Model, toolDefs)
                : text;
            toolDefs = ApplyDomainIntentTurnContext(inputText, toolDefs, out inputText, out var forcedDomainIntentToolName);
            var input = ChatInput.FromText(inputText);
            var chatOptions = new ChatOptions {
                Model = _options.Model,
                Instructions = _instructions,
                ReasoningEffort = _options.ReasoningEffort,
                ReasoningSummary = _options.ReasoningSummary,
                TextVerbosity = _options.TextVerbosity,
                Temperature = _options.Temperature,
                ParallelToolCalls = _options.ParallelToolCalls,
                Tools = toolDefs.Count == 0 ? null : toolDefs,
                ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto,
                PreviousResponseId = _previousResponseId,
                NewThread = string.IsNullOrWhiteSpace(_previousResponseId)
            };
            if (!string.IsNullOrWhiteSpace(forcedDomainIntentToolName) && toolDefs.Count > 0) {
                chatOptions.ToolChoice = ToolChoice.Custom(forcedDomainIntentToolName);
            }

            if (_options.LiveProgress) {
                _status?.Invoke("thinking...");
            }
            TurnInfo turn = await ChatWithToolSchemaRecoveryAsync(input, chatOptions, turnToken).ConfigureAwait(false);

            var maxRounds = Math.Clamp(_options.MaxToolRounds, ChatRequestOptionLimits.MinToolRounds, MaxToolRoundsLimit);
            for (var round = 0; round < maxRounds; round++) {
                var extracted = ToolCallParser.Extract(turn);
                if (extracted.Count == 0
                    && protocolCalls.Count == 0
                    && protocolOutputs.Count == 0
                    && toolDefs.Count > 0
                    && TryBuildSyntheticDomainIntentReplayCalls(text, toolDefs, out var syntheticReplayCalls, out var syntheticReplayReason)) {
                    extracted = syntheticReplayCalls;
                    if (_options.LiveProgress) {
                        _status?.Invoke(
                            $"host-bootstrap: executing {string.Join(", ", syntheticReplayCalls.Select(static call => GetToolDisplayName(call.Name)))} ({syntheticReplayReason}).");
                    }
                }

                if (extracted.Count == 0) {
                    var rawFinalText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                    var finalText = rawFinalText;

                    if (!adEventLogPlatformFallbackUsed
                        && reportedOutputs.Count > 0
                        && toolDefs.Count > 0
                        && TryBuildSyntheticAdEventPlatformFallbackReplayCalls(
                            text,
                            toolDefs,
                            _preferredDomainIntentFamily,
                            reportedCalls,
                            reportedOutputs,
                            out var eventLogFallbackCalls,
                            out var eventLogFallbackReason)) {
                        adEventLogPlatformFallbackUsed = true;
                        toolRounds++;
                        protocolCalls.AddRange(eventLogFallbackCalls);
                        RememberRecentHostTargets(eventLogFallbackCalls);
                        if (_options.LiveProgress) {
                            _status?.Invoke(
                                $"platform-fallback: executing {string.Join(", ", eventLogFallbackCalls.Select(static call => GetToolDisplayName(call.Name)))} ({eventLogFallbackReason}).");
                            foreach (var call in eventLogFallbackCalls) {
                                var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                                var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                                _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                            }
                        }

                        var eventLogFallbackOutputs = await ExecuteToolsWithTurnReadOnlyReplayAsync(
                            eventLogFallbackCalls,
                            turnReadOnlyOutputBySignature,
                            turnToken).ConfigureAwait(false);
                        RememberRecentHostTargetsFromOutputs(eventLogFallbackCalls, eventLogFallbackOutputs);
                        protocolOutputs.AddRange(eventLogFallbackOutputs);
                        AppendReportedToolInteractions(
                            eventLogFallbackCalls,
                            eventLogFallbackOutputs,
                            reportedCalls,
                            reportedOutputs,
                            reportedReadOnlySignatures);

                        var eventLogFallbackInput = new ChatInput();
                        foreach (var output in eventLogFallbackOutputs) {
                            eventLogFallbackInput.AddToolOutput(output.CallId, output.Output);
                        }

                        chatOptions.NewThread = false;
                        chatOptions.PreviousResponseId = TryGetResponseId(turn);
                        chatOptions.ToolChoice = ToolChoice.Auto;
                        if (_options.LiveProgress) {
                            _status?.Invoke("thinking...");
                        }

                        turn = await ChatWithToolSchemaRecoveryAsync(eventLogFallbackInput, chatOptions, turnToken).ConfigureAwait(false);
                        continue;
                    }

                    var shouldRetryNoToolExecution = noToolExecutionRetryCount < MaxNoToolExecutionRetries
                                                     && protocolCalls.Count == 0
                                                     && protocolOutputs.Count == 0
                                                     && toolDefs.Count > 0
                                                     && ShouldRetryNoToolExecution(text, rawFinalText);
                    var shouldRetryScenarioContractRepair = noToolExecutionRetryCount < MaxNoToolExecutionRetries
                                                            && protocolCalls.Count > 0
                                                            && toolDefs.Count > 0
                                                            && ShouldRetryScenarioContractRepair(text, protocolCalls);
                    if (shouldRetryNoToolExecution || shouldRetryScenarioContractRepair) {
                        noToolExecutionRetryCount++;
                        var hasScenarioContract = TryParseScenarioExecutionContractRequirements(text, out _);
                        var useScenarioRepairPrompt = shouldRetryScenarioContractRepair
                                                      || (shouldRetryNoToolExecution && hasScenarioContract);
                        var retryKnownHostTargets = GetRecentHostTargetsSnapshot();
                        var forcedToolName = useScenarioRepairPrompt
                            ? ResolveScenarioRepairForcedToolName(
                                userRequest: text,
                                calls: protocolCalls,
                                toolDefinitions: toolDefs,
                                retryAttempt: noToolExecutionRetryCount)
                            : null;
                        var retryPrompt = useScenarioRepairPrompt
                            ? BuildScenarioContractRepairRetryPrompt(
                                userRequest: text,
                                assistantDraft: rawFinalText,
                                calls: protocolCalls,
                                retryAttempt: noToolExecutionRetryCount,
                                knownHostTargets: retryKnownHostTargets,
                                forcedToolName: forcedToolName)
                            : BuildNoToolExecutionRetryPrompt(
                                userRequest: text,
                                assistantDraft: rawFinalText,
                                retryAttempt: noToolExecutionRetryCount,
                                toolDefinitions: toolDefs,
                                knownHostTargets: retryKnownHostTargets,
                                orchestrationCatalog: GetToolOrchestrationCatalogSnapshot());
                        if (toolDefs.Count > 0
                            && TryBuildSyntheticDomainIntentReplayCalls(retryPrompt, toolDefs, out var syntheticRetryCalls, out var syntheticRetryReason)) {
                            toolRounds++;
                            protocolCalls.AddRange(syntheticRetryCalls);
                            RememberRecentHostTargets(syntheticRetryCalls);
                            if (_options.LiveProgress) {
                                _status?.Invoke(
                                    $"scenario-bootstrap: executing {string.Join(", ", syntheticRetryCalls.Select(static call => GetToolDisplayName(call.Name)))} ({syntheticRetryReason}).");
                                foreach (var call in syntheticRetryCalls) {
                                    var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                                    var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                                    _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                                }
                            }

                            var syntheticOutputs = await ExecuteToolsWithTurnReadOnlyReplayAsync(
                                syntheticRetryCalls,
                                turnReadOnlyOutputBySignature,
                                turnToken).ConfigureAwait(false);
                            RememberRecentHostTargetsFromOutputs(syntheticRetryCalls, syntheticOutputs);
                            protocolOutputs.AddRange(syntheticOutputs);
                            AppendReportedToolInteractions(
                                syntheticRetryCalls,
                                syntheticOutputs,
                                reportedCalls,
                                reportedOutputs,
                                reportedReadOnlySignatures);

                            var syntheticNext = new ChatInput();
                            foreach (var output in syntheticOutputs) {
                                syntheticNext.AddToolOutput(output.CallId, output.Output);
                            }

                            chatOptions.NewThread = false;
                            chatOptions.PreviousResponseId = TryGetResponseId(turn);
                            chatOptions.ToolChoice = ToolChoice.Auto;
                            if (_options.LiveProgress) {
                                _status?.Invoke("thinking...");
                            }

                            turn = await ChatWithToolSchemaRecoveryAsync(syntheticNext, chatOptions, turnToken).ConfigureAwait(false);
                            continue;
                        }

                        chatOptions.NewThread = false;
                        chatOptions.PreviousResponseId = TryGetResponseId(turn);
                        chatOptions.ToolChoice = !string.IsNullOrWhiteSpace(forcedToolName)
                            ? ToolChoice.Custom(forcedToolName!)
                            : ToolChoice.Auto;
                        if (_options.LiveProgress) {
                            _status?.Invoke(useScenarioRepairPrompt
                                ? "repairing partial scenario tool execution..."
                                : "re-planning tool execution for this turn...");
                        }
                        turn = await ChatWithToolSchemaRecoveryAsync(ChatInput.FromText(retryPrompt), chatOptions, turnToken)
                            .ConfigureAwait(false);
                        chatOptions.ToolChoice = ToolChoice.Auto;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(finalText)) {
                        var shouldPreferDirectToolOutputFallback = ShouldPreferDirectToolOutputFallback(reportedCalls, reportedOutputs);
                        var shouldRetryNoTextToolOutputNarrative = !shouldPreferDirectToolOutputFallback
                            && !noTextToolOutputDirectRetryUsed
                            && reportedOutputs.Count > 0;
                        if (shouldRetryNoTextToolOutputNarrative) {
                            noTextToolOutputDirectRetryUsed = true;
                            var noTextToolOutputRetryPrompt = BuildNoTextToolOutputRetryPrompt(
                                userRequest: text,
                                toolCalls: reportedCalls,
                                toolOutputs: reportedOutputs);
                            chatOptions.NewThread = false;
                            chatOptions.PreviousResponseId = TryGetResponseId(turn);
                            chatOptions.Tools = null;
                            chatOptions.ToolChoice = null;
                            chatOptions.ParallelToolCalls = false;
                            if (_options.LiveProgress) {
                                _status?.Invoke("synthesizing executed tool findings...");
                            }
                            turn = await ChatWithToolSchemaRecoveryAsync(ChatInput.FromText(noTextToolOutputRetryPrompt), chatOptions, turnToken)
                                .ConfigureAwait(false);
                            chatOptions.Tools = toolDefs;
                            chatOptions.ToolChoice = ToolChoice.Auto;
                            chatOptions.ParallelToolCalls = _options.ParallelToolCalls;
                            continue;
                        }

                        finalText = BuildNoTextReplFallbackText(
                            assistantDraft: rawFinalText,
                            toolCalls: reportedCalls,
                            toolOutputs: reportedOutputs,
                            model: _options.Model,
                            transport: _options.OpenAITransport,
                            baseUrl: _options.OpenAIBaseUrl,
                            toolDefinitions: toolDefs,
                            knownHostTargets: GetRecentHostTargetsSnapshot());
                    }
                    else if (reportedOutputs.Count > 0) {
                        finalText = BuildNoTextReplFallbackText(
                            assistantDraft: rawFinalText,
                            toolCalls: reportedCalls,
                            toolOutputs: reportedOutputs,
                            model: _options.Model,
                            transport: _options.OpenAITransport,
                            baseUrl: _options.OpenAIBaseUrl,
                            toolDefinitions: toolDefs,
                            knownHostTargets: GetRecentHostTargetsSnapshot());
                    }

                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, reportedCalls, reportedOutputs, turn.Usage, toolRounds, noToolExecutionRetryCount);
                }

                var effectiveExtracted = ApplyScenarioDistinctHostCoverageFallbacks(
                    userRequest: text,
                    calls: extracted,
                    toolDefinitions: toolDefs,
                    knownHostTargets: GetRecentHostTargetsSnapshot());
                if (_options.LiveProgress && !ReferenceEquals(effectiveExtracted, extracted)) {
                    _status?.Invoke("input-repair: enforced distinct host/DC coverage for scenario execution contract.");
                }

                toolRounds++;
                protocolCalls.AddRange(effectiveExtracted);
                RememberRecentHostTargets(effectiveExtracted);
                if (_options.LiveProgress) {
                    foreach (var call in effectiveExtracted) {
                        var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                        var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                        _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                    }
                }
                var executed = await ExecuteToolsWithTurnReadOnlyReplayAsync(
                    effectiveExtracted,
                    turnReadOnlyOutputBySignature,
                    turnToken).ConfigureAwait(false);
                RememberRecentHostTargetsFromOutputs(effectiveExtracted, executed);
                protocolOutputs.AddRange(executed);
                AppendReportedToolInteractions(
                    effectiveExtracted,
                    executed,
                    reportedCalls,
                    reportedOutputs,
                    reportedReadOnlySignatures);

                var next = new ChatInput();
                foreach (var output in executed) {
                    next.AddToolOutput(output.CallId, output.Output);
                }

                chatOptions.NewThread = false;
                chatOptions.PreviousResponseId = TryGetResponseId(turn);
                chatOptions.ToolChoice = ToolChoice.Auto;
                if (_options.LiveProgress) {
                    _status?.Invoke("thinking...");
                }
                turn = await ChatWithToolSchemaRecoveryAsync(next, chatOptions, turnToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
        }

        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsWithTurnReadOnlyReplayAsync(
            IReadOnlyList<ToolCall> calls,
            IDictionary<string, string> turnReadOnlyOutputBySignature,
            CancellationToken cancellationToken) {
            if (calls.Count == 0) {
                return Array.Empty<ToolOutput>();
            }

            var outputs = new ToolOutput?[calls.Count];
            List<ToolCall>? pendingCalls = null;
            List<int>? pendingIndices = null;
            for (var index = 0; index < calls.Count; index++) {
                var call = calls[index];
                if (TryBuildReadOnlyTurnReplayKey(call, out var signature)
                    && turnReadOnlyOutputBySignature.TryGetValue(signature, out var replayOutput)) {
                    outputs[index] = new ToolOutput(call.CallId, replayOutput);
                    if (_options.LiveProgress) {
                        _status?.Invoke($"cache-hit: reused prior turn output for {GetToolDisplayName(call.Name)}.");
                    }

                    continue;
                }

                pendingCalls ??= new List<ToolCall>();
                pendingIndices ??= new List<int>();
                pendingCalls.Add(call);
                pendingIndices.Add(index);
            }

            if (pendingCalls is not null && pendingCalls.Count > 0) {
                var executed = await ExecuteToolsAsync(pendingCalls, cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < executed.Count; i++) {
                    var originalIndex = pendingIndices![i];
                    outputs[originalIndex] = executed[i];
                    if (!TryBuildReadOnlyTurnReplayKey(calls[originalIndex], out var signature)) {
                        continue;
                    }

                    turnReadOnlyOutputBySignature[signature] = executed[i].Output;
                }
            }

            var finalized = new ToolOutput[calls.Count];
            for (var i = 0; i < outputs.Length; i++) {
                finalized[i] = outputs[i] ?? new ToolOutput(calls[i].CallId, string.Empty);
            }

            return finalized;
        }

        private void AppendReportedToolInteractions(
            IReadOnlyList<ToolCall> protocolCalls,
            IReadOnlyList<ToolOutput> protocolOutputs,
            ICollection<ToolCall> reportedCalls,
            ICollection<ToolOutput> reportedOutputs,
            ISet<string> reportedReadOnlySignatures) {
            var count = Math.Min(protocolCalls.Count, protocolOutputs.Count);
            for (var index = 0; index < count; index++) {
                var call = protocolCalls[index];
                if (ShouldSuppressReportedDuplicateReadOnlySignature(call, reportedReadOnlySignatures)) {
                    if (_options.LiveProgress) {
                        _status?.Invoke("input-repair: suppressed duplicate read-only tool call signature from turn report.");
                    }

                    continue;
                }

                reportedCalls.Add(call);
                reportedOutputs.Add(protocolOutputs[index]);
            }
        }

        private bool ShouldSuppressReportedDuplicateReadOnlySignature(
            ToolCall call,
            ISet<string> reportedReadOnlySignatures) {
            if (!TryBuildReadOnlyTurnReplayKey(call, out var signature)) {
                return false;
            }

            return !reportedReadOnlySignatures.Add(signature);
        }

        private bool TryBuildReadOnlyTurnReplayKey(ToolCall call, out string signature) {
            signature = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || !_registry.TryGetDefinition(toolName, out var definition)) {
                return false;
            }

            if (definition.WriteGovernance?.IsWriteCapable == true) {
                return false;
            }

            signature = BuildToolCallSignature(call);
            return signature.Length > 0;
        }

        private sealed class ScenarioExecutionContractRequirements {
            public ScenarioExecutionContractRequirements(
                int minToolCalls,
                IReadOnlyDictionary<string, int> minDistinctToolInputValues,
                IReadOnlyList<string> requiredTools,
                IReadOnlyList<string> requiredAnyTools,
                IReadOnlyDictionary<string, IReadOnlyCollection<string>> forbiddenToolInputValues) {
                MinToolCalls = Math.Max(0, minToolCalls);
                MinDistinctToolInputValues = minDistinctToolInputValues
                                             ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                RequiredTools = requiredTools ?? Array.Empty<string>();
                RequiredAnyTools = requiredAnyTools ?? Array.Empty<string>();
                ForbiddenToolInputValues = forbiddenToolInputValues
                                           ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            }

            public int MinToolCalls { get; }
            public IReadOnlyDictionary<string, int> MinDistinctToolInputValues { get; }
            public IReadOnlyList<string> RequiredTools { get; }
            public IReadOnlyList<string> RequiredAnyTools { get; }
            public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ForbiddenToolInputValues { get; }
        }
    }

    private sealed class ReplTurnResult {
        public ReplTurnResult(string text, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs,
            TurnUsage? usage = null, int toolRounds = 0, int noToolExecutionRetries = 0) {
            Text = text ?? string.Empty;
            ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
            ToolOutputs = toolOutputs ?? Array.Empty<ToolOutput>();
            Usage = usage;
            ToolRounds = Math.Max(0, toolRounds);
            NoToolExecutionRetries = Math.Max(0, noToolExecutionRetries);
        }

        public string Text { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public IReadOnlyList<ToolOutput> ToolOutputs { get; }
        public TurnUsage? Usage { get; }
        public int ToolRounds { get; }
        public int NoToolExecutionRetries { get; }
    }

    private sealed class ReplTurnMetrics {
        public ReplTurnMetrics(DateTime startedAtUtc, DateTime? firstDeltaAtUtc, DateTime completedAtUtc, long durationMs, long? ttftMs,
            TurnUsage? usage, int toolCallsCount, int toolRounds, int noToolExecutionRetries)
            : this(
                startedAtUtc,
                firstDeltaAtUtc,
                completedAtUtc,
                durationMs,
                ttftMs,
                usage,
                toolCallsCount,
                toolRounds,
                noToolExecutionRetries,
                phaseTimings: null) {
        }

        public ReplTurnMetrics(DateTime startedAtUtc, DateTime? firstDeltaAtUtc, DateTime completedAtUtc, long durationMs, long? ttftMs,
            TurnUsage? usage, int toolCallsCount, int toolRounds, int noToolExecutionRetries, IReadOnlyList<TurnPhaseTimingDto>? phaseTimings) {
            StartedAtUtc = startedAtUtc;
            FirstDeltaAtUtc = firstDeltaAtUtc;
            CompletedAtUtc = completedAtUtc;
            DurationMs = Math.Max(0, durationMs);
            TtftMs = ttftMs.HasValue ? Math.Max(0, ttftMs.Value) : null;
            Usage = usage;
            ToolCallsCount = Math.Max(0, toolCallsCount);
            ToolRounds = Math.Max(0, toolRounds);
            NoToolExecutionRetries = Math.Max(0, noToolExecutionRetries);
            PhaseTimings = phaseTimings is { Count: > 0 }
                ? phaseTimings
                : Array.Empty<TurnPhaseTimingDto>();
        }

        public DateTime StartedAtUtc { get; }
        public DateTime? FirstDeltaAtUtc { get; }
        public DateTime CompletedAtUtc { get; }
        public long DurationMs { get; }
        public long? TtftMs { get; }
        public TurnUsage? Usage { get; }
        public int ToolCallsCount { get; }
        public int ToolRounds { get; }
        public int NoToolExecutionRetries { get; }
        public IReadOnlyList<TurnPhaseTimingDto> PhaseTimings { get; }
    }

    private sealed class ReplTurnMetricsResult {
        public ReplTurnMetricsResult(ReplTurnResult result, ReplTurnMetrics metrics) {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public ReplTurnResult Result { get; }
        public ReplTurnMetrics Metrics { get; }
    }

}
