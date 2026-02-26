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
        private static string? TryGetResponseId(TurnInfo turn) {
            if (turn is null) {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(turn.ResponseId)) {
                return turn.ResponseId;
            }
            var responseId = turn.Raw.GetString("responseId");
            if (!string.IsNullOrWhiteSpace(responseId)) {
                return responseId;
            }
            var response = turn.Raw.GetObject("response");
            return response?.GetString("id");
        }

        private async Task<TurnInfo> ChatWithToolSchemaRecoveryAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken) {
            for (var attempt = 0; attempt < MaxModelPhaseAttempts; attempt++) {
                var attemptOptions = options.Clone();
                try {
                    return await ChatWithToolSchemaRecoverySingleAttemptAsync(input, attemptOptions, cancellationToken).ConfigureAwait(false);
                } catch (Exception ex) when (ShouldRetryModelPhaseAttempt(ex, attempt, MaxModelPhaseAttempts, cancellationToken)) {
                    if (_options.LiveProgress) {
                        _status?.Invoke("transient model error; retrying...");
                    }

                    var delayMs = ModelPhaseRetryBaseDelayMs * (attempt + 1);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Model phase retry loop exhausted without returning a result.");
        }

        private async Task<TurnInfo> ChatWithToolSchemaRecoverySingleAttemptAsync(ChatInput input, ChatOptions options,
            CancellationToken cancellationToken) {
            try {
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
                options.Tools = null;
                options.ToolChoice = null;
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool ShouldRetryModelPhaseAttempt(Exception ex, int attempt, int maxAttempts, CancellationToken cancellationToken) {
            if (attempt + 1 >= maxAttempts) {
                return false;
            }

            if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException) {
                return false;
            }

            if (ex is OpenAIAuthenticationRequiredException) {
                return false;
            }

            if (LooksLikeToolOutputPairingReferenceGap(ex)) {
                return true;
            }

            return HasRetryableTransportFailureInChain(ex);
        }

        private static bool HasRetryableTransportFailureInChain(Exception ex) {
            var depth = 0;
            for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
                if (current is TimeoutException || current is IOException || current is HttpRequestException) {
                    return true;
                }

                var statusCode = TryGetStatusCodeFromExceptionData(current);
                if (statusCode is >= 500) {
                    return true;
                }

                var message = (current.Message ?? string.Empty).Trim();
                if (message.Length == 0) {
                    continue;
                }

                if (message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("unexpected end of stream", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("server disconnected", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("server had an error processing your request", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("bad gateway", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("gateway timeout", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static int? TryGetStatusCodeFromExceptionData(Exception ex) {
            if (ex?.Data is null) {
                return null;
            }

            var raw = ex.Data["openai:status_code"];
            return raw switch {
                int intCode => intCode,
                long longCode => (int)longCode,
                short shortCode => shortCode,
                byte byteCode => byteCode,
                string text when int.TryParse(text, out var parsed) => parsed,
                _ => null
            };
        }

        private static bool LooksLikeToolOutputPairingReferenceGap(Exception ex) {
            var depth = 0;
            for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
                var message = (current.Message ?? string.Empty).Trim();
                if (message.Length == 0) {
                    continue;
                }

                if (message.Contains("No tool call found for custom tool call output", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("custom tool call output with call_id", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool output found for function call", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool output found for custom tool call", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool call found for function call output", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRetryWithoutTools(Exception ex, ChatOptions options) {
            return ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);
        }
    }
}
