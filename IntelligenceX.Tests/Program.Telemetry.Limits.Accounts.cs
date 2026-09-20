using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestProviderLimitsRefreshAccountsIndependently() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-limit-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var usageRequests = new List<string>();
            var refreshRequests = 0;
            using var server = new LocalHttpServer((HttpRequest request) => {
                if (request.Path == "/token") {
                    Interlocked.Increment(ref refreshRequests);
                    return new HttpResponse("{\"access_token\":\"renewed\",\"refresh_token\":\"rotated\",\"expires_in\":3600}");
                }
                var account = request.Headers["ChatGPT-Account-Id"];
                usageRequests.Add(account);
                if (account == "current") {
                    return new HttpResponse("private-response-body", StatusCode: 401, StatusText: "Unauthorized");
                }
                if (account == "renew") {
                    AssertEqual("Bearer renewed", request.Headers["Authorization"], "usage request uses refreshed token");
                }
                var extra = account == "dual"
                    ? ",\"secondary_window\":{\"used_percent\":10,\"limit_window_seconds\":18000}"
                    : string.Empty;
                return new HttpResponse("{\"account_id\":\"" + account + "\",\"plan_type\":\"pro\",\"rate_limit\":{"
                    + "\"primary_window\":{\"used_percent\":25,\"limit_window_seconds\":604800}" + extra + "}}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "ix-auth.json"));
            foreach (var id in new[] { "current", "weekly", "dual", "renew", "fifth" }) {
                store.SaveAsync(new AuthBundle("openai-codex", "saved-" + id, "refresh-" + id,
                    id == "renew" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = id, IdToken = "fixture-id-token"
                }).GetAwaiter().GetResult();
            }
            store.SaveAsync(new AuthBundle("chatgpt", "stale-alias", "old-refresh", DateTimeOffset.UtcNow.AddDays(-2)) {
                AccountId = "weekly"
            }).GetAwaiter().GetResult();
            var codexPath = Path.Combine(directory, "auth.json");
            File.WriteAllText(codexPath, "unchanged-active-codex-login");
            var options = new OpenAINativeOptions {
                AuthStore = store, AuthAccountId = "current", CodexHome = directory,
                ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/') + "/backend-api",
                PersistCodexAuthJson = true
            };
            options.OAuth.TokenUrl = new Uri(server.BaseUri, "/token").ToString();
            var result = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(5, result.Accounts.Count, "five distinct accounts remain visible despite current account failure");
            AssertEqual(4, result.Accounts.Count(account => account.IsAvailable), "healthy accounts retain limits");
            AssertEqual(true, result.IsAvailable, "provider remains partially available");
            AssertEqual(0, result.Windows.Count, "current account failure does not borrow another account's windows");
            AssertEqual("current", result.Accounts.Single(account => account.IsSelected).AccountId, "selection preserved");
            AssertEqual(false, result.Accounts.Single(account => account.IsSelected).IsAvailable, "failed current account remains unavailable");
            AssertEqual(false, result.Accounts.Any(account => (account.DetailMessage ?? "").Contains("private-response-body")), "API response bodies are not rendered");
            AssertEqual(1, result.Accounts.Single(account => account.AccountId == "weekly").Windows.Count, "weekly-only Pro account");
            AssertEqual(2, result.Accounts.Single(account => account.AccountId == "dual").Windows.Count, "Pro account may also report five-hour window");
            AssertEqual(5, usageRequests.Count, "aliases do not cause duplicate requests");
            AssertEqual(1, refreshRequests, "expired account refreshed through OAuth");
            AssertEqual("rotated", store.GetAsync("openai-codex", "renew").GetAwaiter().GetResult()!.RefreshToken, "rotated token saved in IX store");
            AssertEqual("unchanged-active-codex-login", File.ReadAllText(codexPath), "background refresh does not switch Codex login");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestProviderLimitsAccountRefreshCancellationAndEmptyStore() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-limit-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var cancellation = new CancellationTokenSource();
            var calls = 0;
            using var server = new LocalHttpServer((HttpRequest request) => {
                Interlocked.Increment(ref calls);
                cancellation.Cancel();
                return new HttpResponse("{}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "ix-auth.json"));
            var options = new OpenAINativeOptions {
                AuthStore = store, AuthAccountId = "absent", ChatGptApiBaseUrl = server.BaseUri.ToString(), CodexHome = directory
            };
            var empty = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(0, empty.Accounts.Count, "empty store does not invent accounts");
            AssertEqual(false, empty.IsAvailable, "empty store is unavailable");
            AssertEqual(0, calls, "empty store does not query arbitrary current login");
            store.SaveAsync(new AuthBundle("openai", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "only"
            }).GetAwaiter().GetResult();
            AssertThrows<OperationCanceledException>(() => ProviderLimitSnapshotService.FetchCodexAsync("codex", options,
                cancellation.Token).GetAwaiter().GetResult(), "caller cancellation propagates even during final account request");
            AssertEqual(1, calls, "cancelled request is not retried with a fallback account");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
#endif
