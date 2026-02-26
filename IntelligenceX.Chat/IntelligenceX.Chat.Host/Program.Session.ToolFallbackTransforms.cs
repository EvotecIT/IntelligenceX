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
        private void TryStoreSessionToolOutputCache(string cacheKey, string output, bool hasSessionCacheKey) {
            if (!hasSessionCacheKey || !ShouldCacheSessionToolOutput(output)) {
                return;
            }

            _sessionToolOutputCache[cacheKey] = output;
        }

        private static bool TryGetSessionToolOutputCacheKey(ToolCall call, out string cacheKey) {
            cacheKey = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (!toolName.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (call.Arguments is not null && call.Arguments.Count > 0) {
                return false;
            }

            var normalizedInput = (call.Input ?? string.Empty).Trim();
            if (normalizedInput.Length > 0 && !string.Equals(normalizedInput, "{}", StringComparison.Ordinal)) {
                return false;
            }

            cacheKey = toolName.ToLowerInvariant();
            return true;
        }

        private static bool ShouldCacheSessionToolOutput(string output) {
            return TryReadToolOutputOk(output, out var ok) && ok;
        }

        private static ToolCall ApplyAdDiscoveryRootDseFallback(ToolCall call, string toolOutput) {
            if (call.Arguments is null || !IsAdDiscoveryToolName(call.Name)) {
                return call;
            }

            if (!TryReadToolInputValuesByKey(call.Arguments, "domain_controller", out var configuredDomainControllers)
                || configuredDomainControllers.Count == 0) {
                return call;
            }

            var pinnedDomainController = configuredDomainControllers
                .Select(NormalizeHostTargetCandidate)
                .FirstOrDefault(static value => value.Length > 0);
            if (string.IsNullOrWhiteSpace(pinnedDomainController)
                || !LooksLikePinnedDomainControllerRootDseFailure(toolOutput, pinnedDomainController)) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            var replacedDomainController = false;
            foreach (var pair in call.Arguments) {
                if (!string.Equals(pair.Key, "domain_controller", StringComparison.OrdinalIgnoreCase)) {
                    rewrittenArguments.Add(pair.Key, pair.Value);
                    continue;
                }

                if (replacedDomainController) {
                    continue;
                }

                rewrittenArguments.Add(pair.Key, string.Empty);
                replacedDomainController = true;
            }

            if (!replacedDomainController) {
                rewrittenArguments.Add("domain_controller", string.Empty);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static ToolCall ApplyAdReplicationProbeFallback(
            ToolCall call,
            string toolOutput,
            IReadOnlyList<string>? knownHostTargets) {
            if (call.Arguments is null
                || !string.Equals(call.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)
                || !IsReplicationProbeCall(call.Arguments)
                || !TryReadToolOutputFailure(toolOutput, out var errorCode, out var errorMessage)) {
                return call;
            }

            var looksLikeTimeout = string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase)
                                   || errorMessage.Contains("Replication query timed out", StringComparison.OrdinalIgnoreCase);
            var looksLikeNoData = errorMessage.Contains("No replication data returned", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeTimeout && !looksLikeNoData) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            foreach (var pair in call.Arguments) {
                rewrittenArguments.Add(pair.Key, pair.Value ?? JsonValue.Null);
            }

            var changed = false;
            if (looksLikeTimeout) {
                var configuredTimeout = rewrittenArguments.GetInt64("timeout_ms") ?? 0;
                if (configuredTimeout <= 0 || configuredTimeout < MinReplicationProbeTimeoutMs) {
                    rewrittenArguments.Add("timeout_ms", MinReplicationProbeTimeoutMs);
                    changed = true;
                }
            }

            if (TryPromoteReplicationProbeHostInputsToFqdn(rewrittenArguments, knownHostTargets)) {
                changed = true;
            }

            if (!changed) {
                return call;
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static ToolCall ApplyDomainDetectiveSummaryTimeoutFallback(ToolCall call, string toolOutput) {
            if (call.Arguments is null
                || !string.Equals(call.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)
                || !TryReadToolOutputFailure(toolOutput, out var errorCode, out var errorMessage)) {
                return call;
            }

            var looksLikeTimeout = string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase)
                                   || errorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeTimeout) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            foreach (var pair in call.Arguments) {
                rewrittenArguments.Add(pair.Key, pair.Value ?? JsonValue.Null);
            }

            var configuredTimeout = rewrittenArguments.GetInt64("timeout_ms") ?? 0;
            if (configuredTimeout >= MinDomainDetectiveSummaryTimeoutMs) {
                return call;
            }

            rewrittenArguments.Add("timeout_ms", MinDomainDetectiveSummaryTimeoutMs);
            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static bool IsReplicationProbeCall(JsonObject arguments) {
            var probeKind = arguments.GetString("probe_kind") ?? string.Empty;
            return string.Equals(probeKind, "replication", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryPromoteReplicationProbeHostInputsToFqdn(
            JsonObject arguments,
            IReadOnlyList<string>? knownHostTargets) {
            if (knownHostTargets is null || knownHostTargets.Count == 0) {
                return false;
            }

            var knownFqdns = OrderHostTargetCandidatesBySpecificity(knownHostTargets)
                .Select(NormalizeHostTargetCandidate)
                .Where(static value => value.Contains('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (knownFqdns.Length == 0) {
                return false;
            }

            var changed = false;
            if (TryPromoteStringHostArgument(arguments, "domain_controller", knownFqdns)) {
                changed = true;
            }

            if (TryPromoteStringArrayHostArgument(arguments, "targets", knownFqdns)) {
                changed = true;
            }

            if (TryPromoteStringArrayHostArgument(arguments, "include_domain_controllers", knownFqdns)) {
                changed = true;
            }

            return changed;
        }

        private static bool TryPromoteStringHostArgument(JsonObject arguments, string key, IReadOnlyList<string> knownFqdns) {
            var current = arguments.GetString(key) ?? string.Empty;
            if (!TryResolveKnownHostFqdn(current, knownFqdns, out var resolved)) {
                return false;
            }

            arguments.Add(key, resolved);
            return true;
        }

        private static bool TryPromoteStringArrayHostArgument(JsonObject arguments, string key, IReadOnlyList<string> knownFqdns) {
            if (arguments.GetArray(key) is not JsonArray values || values.Count == 0) {
                return false;
            }

            var patched = new JsonArray();
            var changed = false;
            for (var i = 0; i < values.Count; i++) {
                var original = values[i]?.AsString() ?? string.Empty;
                if (TryResolveKnownHostFqdn(original, knownFqdns, out var resolved)) {
                    patched.Add(resolved);
                    changed = true;
                    continue;
                }

                patched.Add(original);
            }

            if (!changed) {
                return false;
            }

            arguments.Add(key, patched);
            return true;
        }

        private static bool TryResolveKnownHostFqdn(string value, IReadOnlyList<string> knownFqdns, out string resolved) {
            resolved = string.Empty;
            var normalized = NormalizeHostTargetCandidate(value);
            if (normalized.Length == 0) {
                return false;
            }

            for (var i = 0; i < knownFqdns.Count; i++) {
                var candidate = knownFqdns[i];
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase)) {
                    resolved = candidate;
                    return true;
                }
            }

            for (var i = 0; i < knownFqdns.Count; i++) {
                var candidate = knownFqdns[i];
                if (candidate.StartsWith(normalized + ".", StringComparison.OrdinalIgnoreCase)) {
                    resolved = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadToolOutputFailure(string output, out string errorCode, out string errorMessage) {
            errorCode = string.Empty;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(output)) {
                return false;
            }

            var envelope = JsonLite.Parse(output)?.AsObject();
            if (envelope is null || !TryReadToolOutputOk(output, out var ok) || ok) {
                return false;
            }

            errorCode = envelope.GetString("error_code") ?? string.Empty;
            errorMessage = envelope.GetString("error") ?? string.Empty;
            var failure = envelope.GetObject("failure");
            if (errorCode.Length == 0) {
                errorCode = failure?.GetString("code") ?? string.Empty;
            }
            if (errorMessage.Length == 0) {
                errorMessage = failure?.GetString("message") ?? string.Empty;
            }

            return errorCode.Length > 0 || errorMessage.Length > 0;
        }

        private static bool IsAdDiscoveryToolName(string toolName) {
            var normalized = (toolName ?? string.Empty).Trim();
            return string.Equals(normalized, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ad_forest_discover", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePinnedDomainControllerRootDseFailure(string toolOutput, string pinnedDomainController) {
            if (string.IsNullOrWhiteSpace(toolOutput) || string.IsNullOrWhiteSpace(pinnedDomainController)) {
                return false;
            }

            JsonObject? envelope;
            try {
                envelope = JsonLite.Parse(toolOutput)?.AsObject();
            } catch {
                return false;
            }

            if (envelope is null) {
                return false;
            }

            bool ok;
            try {
                ok = envelope.GetBoolean("ok", defaultValue: false);
            } catch {
                ok = false;
            }

            if (ok) {
                return false;
            }

            var errorCode = envelope.GetString("error_code") ?? string.Empty;
            var errorMessage = envelope.GetString("error") ?? string.Empty;
            var failure = envelope.GetObject("failure");
            if (errorCode.Length == 0) {
                errorCode = failure?.GetString("code") ?? string.Empty;
            }
            if (errorMessage.Length == 0) {
                errorMessage = failure?.GetString("message") ?? string.Empty;
            }

            if (!string.Equals(errorCode, AdDiscoveryRootDseFailureErrorCode, StringComparison.OrdinalIgnoreCase)
                || errorMessage.Length == 0) {
                return false;
            }

            return errorMessage.Contains("RootDSE", StringComparison.OrdinalIgnoreCase)
                   && errorMessage.Contains(pinnedDomainController, StringComparison.OrdinalIgnoreCase);
        }

    }
}
