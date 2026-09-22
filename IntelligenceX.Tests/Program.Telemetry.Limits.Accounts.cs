using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestProviderLimitsSynchronizeOnlyMatchingCodexCredentials() {
        foreach (var scenario in new[] { "matching", "different-account", "newer-login", "export-disabled" }) {
            var directory = Path.Combine(Path.GetTempPath(), "ix-limit-sync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var codexPath = Path.Combine(directory, "auth.json");
                var codexId = scenario == "different-account" ? "unrelated" : "shared";
                var original = new JsonObject().Add("custom_metadata", "preserved").Add("tokens", new JsonObject()
                    .Add("account_id", codexId).Add("access_token", "old-access").Add("refresh_token", "refresh-shared"));
                File.WriteAllText(codexPath, JsonLite.Serialize(JsonValue.From(original)));
                using var server = new LocalHttpServer((HttpRequest request) => {
                    if (request.Path == "/token") {
                        if (scenario == "newer-login" && request.Body.Contains("refresh-shared")) {
                            original.GetObject("tokens")!.Add("refresh_token", "newer-sign-in");
                            File.WriteAllText(codexPath, JsonLite.Serialize(JsonValue.From(original)));
                        }
                        return new HttpResponse("{\"access_token\":\"renewed\",\"refresh_token\":\"rotated\",\"expires_in\":3600}");
                    }
                    return new HttpResponse("{\"rate_limit\":{\"primary_window\":{\"limit_window_seconds\":604800,\"used_percent\":20}}}");
                });
                var store = new FileAuthBundleStore(Path.Combine(directory, "ix-auth.json"));
                foreach (var id in new[] { "other", "shared" }) {
                    store.SaveAsync(new AuthBundle("openai-codex", "old-" + id, "refresh-" + id, DateTimeOffset.UtcNow.AddDays(-1)) {
                        AccountId = id, IdToken = "fixture-id-token"
                    }).GetAwaiter().GetResult();
                }
                var options = new OpenAINativeOptions {
                    AuthStore = store, AuthAccountId = "shared", CodexHome = directory, ChatGptApiBaseUrl = server.BaseUri.ToString(),
                    PersistCodexAuthJson = scenario != "export-disabled"
                };
                options.OAuth.TokenUrl = new Uri(server.BaseUri, "/token").ToString();
                var result = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
                var persisted = JsonLite.Parse(File.ReadAllText(codexPath)).AsObject()!;
                AssertEqual(2, result.Accounts.Count(account => account.IsAvailable), "both accounts refreshed independently");
                AssertEqual(codexId, persisted.GetObject("tokens")!.GetString("account_id"), "Codex account never switched");
                AssertEqual("preserved", persisted.GetString("custom_metadata"), "unrelated Codex metadata retained");
                AssertEqual(scenario == "matching" ? "rotated" : scenario == "newer-login" ? "newer-sign-in" : "refresh-shared",
                    persisted.GetObject("tokens")!.GetString("refresh_token"), "only matching credential generation synchronized");
                AssertEqual(scenario == "matching" ? "renewed" : "old-access",
                    persisted.GetObject("tokens")!.GetString("access_token"), "nonmatching access token unchanged");
            } finally {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

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
                var account = request.Headers.TryGetValue("ChatGPT-Account-Id", out var requestedAccount) ? requestedAccount : "legacy";
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
            store.SaveAsync(new AuthBundle("openai-codex", "opaque-legacy", "legacy-refresh", DateTimeOffset.UtcNow.AddMinutes(30)))
                .GetAwaiter().GetResult();
            options.AuthAccountId = "legacy";
            var identified = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual("legacy", identified.Accounts.Single(account => account.IsSelected).AccountId,
                "usage-reported identity establishes current legacy account");
            AssertEqual("legacy", identified.AccountLabel, "top-level reading uses the identified current account");
            store.SaveAsync(new AuthBundle("openai", "another-opaque-alias", "other-refresh", DateTimeOffset.UtcNow.AddMinutes(30)))
                .GetAwaiter().GetResult();
            var coalesced = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(6, coalesced.Accounts.Count, "provider-resolved aliases yield one row per account");
            AssertEqual(1, coalesced.Accounts.Count(account => account.AccountId == "legacy" && account.IsSelected),
                "resolved duplicate retains current-account selection");
            options.AuthAccountId = "known-but-not-saved";
            var missingCurrent = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(0, missingCurrent.Windows.Count, "another saved account cannot stand in for the active Codex login");
            AssertEqual(null, missingCurrent.AccountLabel, "the primary label does not borrow another account");
            AssertEqual(null, missingCurrent.PlanLabel, "the primary plan does not borrow another account");
            AssertEqual(0, missingCurrent.Accounts.Count(account => account.IsSelected), "no saved account is falsely current");
            AssertEqual(true, missingCurrent.Accounts.Any(account => account.IsAvailable), "other account readings remain available below");
            AssertEqual(false, ProviderLimitSnapshotService.MatchesCurrentCodexAccount(identified, "weekly"),
                "a returned current-account B snapshot cannot be cached under A after an A-B-A switch");
            AssertEqual(true, ProviderLimitSnapshotService.MatchesCurrentCodexAccount(identified, "legacy"),
                "a matching current-account snapshot remains cacheable");
            AssertEqual(false, ProviderLimitSnapshotService.MatchesCurrentCodexAccount(missingCurrent, "weekly"),
                "an unselected inventory containing the current ID is not safe to cache");
            AssertEqual(true, ProviderLimitSnapshotService.MatchesCurrentCodexAccount(missingCurrent, "known-but-not-saved"),
                "another account's rows may be retained without claiming it is current");
            options.AuthAccountId = null;
            var unknownCurrent = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(null, unknownCurrent.AccountLabel, "unknown current identity cannot borrow another account label");
            AssertEqual(null, unknownCurrent.PlanLabel, "unknown current identity cannot borrow another plan");
            AssertEqual(0, unknownCurrent.Windows.Count, "unknown current identity cannot borrow another account's windows");
            AssertEqual(0, unknownCurrent.Accounts.Count(account => account.IsSelected), "unknown current identity has no selected account");
            AssertEqual(true, ProviderLimitSnapshotService.MatchesCurrentCodexAccount(unknownCurrent, null),
                "unknown current identity can cache only an unselected inventory");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestProviderLimitsKeepDistinctUnresolvedCredentials() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-limit-unresolved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var requests = 0;
            using var server = new LocalHttpServer(request => {
                Interlocked.Increment(ref requests);
                var account = request.Headers["Authorization"].EndsWith("access-one", StringComparison.Ordinal) ? "one" : "two";
                return new HttpResponse("{\"account_id\":\"" + account + "\",\"rate_limit\":{\"primary_window\":{\"used_percent\":20}}}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "ix-auth.json"));
            // A shared label is presentation metadata, not an identity key.
            var idToken = "e30." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"email\":\"same@example.com\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";
            foreach (var (provider, token) in new[] { ("openai-codex", "access-one"), ("openai", "access-two") }) {
                store.SaveAsync(new AuthBundle(provider, token, "refresh-" + token, DateTimeOffset.UtcNow.AddHours(1)) {
                    IdToken = idToken
                }).GetAwaiter().GetResult();
            }
            var options = new OpenAINativeOptions { AuthStore = store, AuthAccountId = "one", CodexHome = directory,
                ChatGptApiBaseUrl = server.BaseUri.ToString() };
            var result = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(2, requests, "same-email credentials each reach the usage API");
            AssertEqual(2, result.Accounts.Count, "distinct provider-confirmed accounts remain separate");
            AssertEqual("one", result.Accounts.Single(account => account.IsSelected).AccountId, "current identity survives deduplication");

            var now = DateTimeOffset.UtcNow;
            var unresolved = ProviderLimitSnapshotService.CoalesceResolvedAccounts(new[] {
                new ProviderLimitAccountSnapshot(null, null, null,
                    new[] { new ProviderLimitWindow("weekly", "Weekly", 15, now.AddDays(1)) }, null, null, now),
                new ProviderLimitAccountSnapshot(null, null, null,
                    new[] { new ProviderLimitWindow("weekly", "Weekly", 75, now.AddDays(1)) }, null, null, now)
            }, null);
            AssertEqual(2, unresolved.Count, "unresolved credentials remain distinct");
            AssertEqual(true, unresolved.Select(account => account.AccountLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "unresolved account cards get distinct labels for advisory matching");
            AssertEqual(75d, unresolved[1].Windows[0].UsedPercent, "distinct labels preserve each account's own reading");
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

    private static void TestProviderLimitsLargeInventoryReturnsWithinScanBudget() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-limit-scan-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var server = new LocalHttpServer(_ => {
                Thread.Sleep(400);
                return new HttpResponse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":20}}}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "ix-auth.json"));
            for (var i = 0; i < 13; i++) {
                store.SaveAsync(new AuthBundle("openai-codex", "access-" + i, "refresh-" + i,
                    DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "account-" + i }).GetAwaiter().GetResult();
            }
            var options = new OpenAINativeOptions { AuthStore = store, CodexHome = directory,
                ChatGptApiBaseUrl = server.BaseUri.ToString() };
            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = ProviderLimitSnapshotService.FetchCodexAsync("codex", options, CancellationToken.None,
                TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult();
            AssertEqual(13, result.Accounts.Count, "queued accounts retain explicit unavailable rows at deadline");
            AssertEqual(true, started.Elapsed < TimeSpan.FromSeconds(3), "scan deadline covers queued accounts");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
#endif
