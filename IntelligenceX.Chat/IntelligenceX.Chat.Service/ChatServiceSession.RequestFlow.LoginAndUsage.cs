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
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task HandleEnsureLoginAsync(IntelligenceXClient? client, StreamWriter writer, EnsureLoginRequest request, CancellationToken cancellationToken) {
        if (_options.OpenAITransport != OpenAITransportKind.Native) {
            await WriteAsync(writer, new LoginStatusMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                IsAuthenticated = true,
                AccountId = null
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (client is null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Native runtime client is not connected.",
                Code = "ensure_login_failed"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.ForceLogin) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Use chatgpt_login_start to run an interactive ChatGPT OAuth login in the service.",
                Code = "use_chatgpt_login_start"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        try {
            var account = await client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            var accountId = account.AccountId;
            var nativeUsage = await TryGetNativeUsageSnapshotAsync(accountId, cancellationToken).ConfigureAwait(false);
            await WriteAsync(writer, new LoginStatusMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                IsAuthenticated = true,
                AccountId = accountId,
                NativeUsage = nativeUsage
            }, cancellationToken).ConfigureAwait(false);
        } catch (OpenAIAuthenticationRequiredException) {
            await WriteAsync(writer, new LoginStatusMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                IsAuthenticated = false,
                AccountId = null
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to probe login state: {ex.Message}",
                Code = "ensure_login_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<NativeUsageSnapshotDto?> TryGetNativeUsageSnapshotAsync(string? accountId, CancellationToken cancellationToken) {
        if (_options.OpenAITransport != OpenAITransportKind.Native) {
            return null;
        }

        var normalizedAccountId = (accountId ?? string.Empty).Trim();
        var nowUtc = DateTime.UtcNow;
        lock (_nativeUsageCacheLock) {
            if (_nativeUsageCache is not null
                && string.Equals(_nativeUsageCacheAccountId, normalizedAccountId, StringComparison.OrdinalIgnoreCase)
                && nowUtc - _nativeUsageCacheUpdatedUtc <= NativeUsageRefreshInterval) {
                return _nativeUsageCache;
            }
        }

        var requestedAccountId = (_options.OpenAIAccountId ?? string.Empty).Trim();
        if (requestedAccountId.Length == 0) {
            requestedAccountId = normalizedAccountId;
        }

        try {
            var options = new OpenAINativeOptions();
            if (requestedAccountId.Length > 0) {
                options.AuthAccountId = requestedAccountId;
            }

            using var usageService = new ChatGptUsageService(options);
            var snapshot = await usageService.GetUsageSnapshotAsync(cancellationToken).ConfigureAwait(false);
            TrySaveUsageCacheSnapshot(snapshot, requestedAccountId, normalizedAccountId);

            var dto = MapNativeUsageSnapshot(snapshot, DateTime.UtcNow, source: "live");
            lock (_nativeUsageCacheLock) {
                _nativeUsageCache = dto;
                _nativeUsageCacheUpdatedUtc = DateTime.UtcNow;
                _nativeUsageCacheAccountId = normalizedAccountId;
            }
            return dto;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch {
            var cached = TryLoadNativeUsageSnapshotFromCache(requestedAccountId, normalizedAccountId);
            if (cached is null) {
                return null;
            }

            lock (_nativeUsageCacheLock) {
                _nativeUsageCache = cached;
                _nativeUsageCacheUpdatedUtc = DateTime.UtcNow;
                _nativeUsageCacheAccountId = normalizedAccountId;
            }
            return cached;
        }
    }

    private static void TrySaveUsageCacheSnapshot(ChatGptUsageSnapshot snapshot, string? requestedAccountId, string? fallbackAccountId) {
        try {
            var cacheAccountId = ResolveUsageCacheAccountId(snapshot.AccountId, requestedAccountId, fallbackAccountId);
            var cachePath = ChatGptUsageCache.ResolveCachePath(cacheAccountId);
            ChatGptUsageCache.Save(snapshot, cachePath);
        } catch {
            // Best-effort usage cache update.
        }
    }

    private static NativeUsageSnapshotDto? TryLoadNativeUsageSnapshotFromCache(string? requestedAccountId, string? fallbackAccountId) {
        var candidates = new List<string?>(3);
        AddCacheAccountCandidate(candidates, requestedAccountId);
        AddCacheAccountCandidate(candidates, fallbackAccountId);
        AddCacheAccountCandidate(candidates, null);

        for (var i = 0; i < candidates.Count; i++) {
            var candidate = candidates[i];
            try {
                var cachePath = ChatGptUsageCache.ResolveCachePath(candidate);
                if (!ChatGptUsageCache.TryLoad(out var entry, cachePath) || entry is null) {
                    continue;
                }

                return MapNativeUsageSnapshot(
                    entry.Snapshot,
                    entry.UpdatedAt.UtcDateTime,
                    source: "cache");
            } catch {
                // Try next candidate.
            }
        }

        return null;
    }

    private static void AddCacheAccountCandidate(List<string?> candidates, string? accountId) {
        var normalized = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        for (var i = 0; i < candidates.Count; i++) {
            if (string.Equals(candidates[i], normalized, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
        }

        candidates.Add(normalized);
    }

    private static string? ResolveUsageCacheAccountId(string? snapshotAccountId, string? requestedAccountId, string? fallbackAccountId) {
        var normalizedSnapshot = string.IsNullOrWhiteSpace(snapshotAccountId) ? null : snapshotAccountId.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSnapshot)) {
            return normalizedSnapshot;
        }

        var normalizedRequested = string.IsNullOrWhiteSpace(requestedAccountId) ? null : requestedAccountId.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedRequested)) {
            return normalizedRequested;
        }

        return string.IsNullOrWhiteSpace(fallbackAccountId) ? null : fallbackAccountId.Trim();
    }

    private static NativeUsageSnapshotDto MapNativeUsageSnapshot(ChatGptUsageSnapshot snapshot, DateTime retrievedAtUtc, string source) {
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        return new NativeUsageSnapshotDto {
            AccountId = string.IsNullOrWhiteSpace(snapshot.AccountId) ? null : snapshot.AccountId.Trim(),
            Email = string.IsNullOrWhiteSpace(snapshot.Email) ? null : snapshot.Email.Trim(),
            PlanType = string.IsNullOrWhiteSpace(snapshot.PlanType) ? null : snapshot.PlanType.Trim(),
            RateLimit = MapNativeRateLimit(snapshot.RateLimit),
            Credits = MapNativeCredits(snapshot.Credits),
            RetrievedAtUtc = retrievedAtUtc.Kind == DateTimeKind.Utc ? retrievedAtUtc : retrievedAtUtc.ToUniversalTime(),
            Source = normalizedSource
        };
    }

    private static NativeRateLimitStatusDto? MapNativeRateLimit(ChatGptRateLimitStatus? status) {
        if (status is null) {
            return null;
        }

        return new NativeRateLimitStatusDto {
            Allowed = status.Allowed,
            LimitReached = status.LimitReached,
            Primary = MapNativeRateLimitWindow(status.PrimaryWindow),
            Secondary = MapNativeRateLimitWindow(status.SecondaryWindow)
        };
    }

    private static NativeRateLimitWindowDto? MapNativeRateLimitWindow(ChatGptRateLimitWindow? window) {
        if (window is null) {
            return null;
        }

        return new NativeRateLimitWindowDto {
            UsedPercent = window.UsedPercent,
            LimitWindowSeconds = window.LimitWindowSeconds,
            ResetAfterSeconds = window.ResetAfterSeconds,
            ResetAtUnixSeconds = window.ResetAtUnixSeconds
        };
    }

    private static NativeCreditsSnapshotDto? MapNativeCredits(ChatGptCreditsSnapshot? credits) {
        if (credits is null) {
            return null;
        }

        return new NativeCreditsSnapshotDto {
            HasCredits = credits.HasCredits,
            Unlimited = credits.Unlimited,
            Balance = credits.Balance,
            ApproxLocalMessages = credits.ApproxLocalMessages,
            ApproxCloudMessages = credits.ApproxCloudMessages
        };
    }

    private async Task HandleStartChatGptLoginAsync(IntelligenceXClient? client, StreamWriter writer, StartChatGptLoginRequest request,
        CancellationToken cancellationToken) {
        if (_options.OpenAITransport != OpenAITransportKind.Native) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "ChatGPT sign-in is only available for native runtime transport.",
                Code = "login_not_supported_for_transport"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (client is null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Native runtime client is not connected.",
                Code = "login_start_failed"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.TimeoutSeconds < ChatRequestOptionLimits.MinPositiveTimeoutSeconds
            || request.TimeoutSeconds > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"timeoutSeconds must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginFlow flow;
        lock (_loginLock) {
            if (_login is not null) {
                flow = _login;
                if (!flow.IsCompleted) {
                    // One login per connection/session for now.
                    _ = WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = $"A login flow is already in progress (loginId={flow.LoginId}).",
                        Code = "login_in_progress"
                    }, cancellationToken);
                    return;
                }
                _login = null;
            }

            flow = new LoginFlow(Guid.NewGuid().ToString("N"), request.RequestId,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _login = flow;
        }

        await WriteAsync(writer, new ChatGptLoginStartedMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            LoginId = flow.LoginId
        }, cancellationToken).ConfigureAwait(false);

        // Run in the background so the session can continue to process prompt responses.
        flow.Task = Task.Run(async () => {
            try {
                await client.LoginChatGptAndWaitAsync(
                        onUrl: url => {
                            _ = WriteAsync(writer, new ChatGptLoginUrlMessage {
                                Kind = ChatServiceMessageKind.Event,
                                RequestId = flow.RequestId,
                                LoginId = flow.LoginId,
                                Url = url
                            }, CancellationToken.None);
                        },
                        onPrompt: prompt => OnLoginPromptAsync(writer, flow, prompt),
                        useLocalListener: request.UseLocalListener,
                        timeout: TimeSpan.FromSeconds(request.TimeoutSeconds),
                        cancellationToken: flow.Cts.Token)
                    .ConfigureAwait(false);

                InvalidateModelListCache();
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = true,
                    Error = null
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OpenAIUserCanceledLoginException) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = "Canceled."
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = "Canceled."
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = ex.Message
                }, CancellationToken.None).ConfigureAwait(false);
            } finally {
                lock (_loginLock) {
                    if (ReferenceEquals(_login, flow)) {
                        _login = null;
                    }
                }
                flow.MarkCompleted();
            }
        }, CancellationToken.None);
    }

    private async Task<string> OnLoginPromptAsync(StreamWriter writer, LoginFlow flow, string prompt) {
        var promptId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        flow.SetPendingPrompt(promptId, tcs);

        await WriteAsync(writer, new ChatGptLoginPromptMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = flow.RequestId,
            LoginId = flow.LoginId,
            PromptId = promptId,
            Prompt = prompt
        }, CancellationToken.None).ConfigureAwait(false);

        try {
            using var reg = flow.Cts.Token.Register(() => tcs.TrySetCanceled(flow.Cts.Token));
            var input = await tcs.Task.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(input)) {
                throw new OpenAIUserCanceledLoginException();
            }
            return input;
        } finally {
            flow.ClearPendingPrompt(promptId);
        }
    }

    private async Task HandleChatGptLoginPromptResponseAsync(StreamWriter writer, ChatGptLoginPromptResponseRequest request,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.PromptId)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "loginId and promptId are required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
        }

        if (flow is null || !string.Equals(flow.LoginId, request.LoginId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Login flow not found.",
                Code = "login_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!flow.TryCompletePrompt(request.PromptId, request.Input)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Prompt not found or already completed.",
                Code = "prompt_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Accepted."
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCancelChatGptLoginAsync(StreamWriter writer, CancelChatGptLoginRequest request, CancellationToken cancellationToken) {
        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
        }

        if (flow is null || !string.Equals(flow.LoginId, request.LoginId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Login flow not found.",
                Code = "login_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        flow.Cancel();
        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Canceled."
        }, cancellationToken).ConfigureAwait(false);
    }

}
