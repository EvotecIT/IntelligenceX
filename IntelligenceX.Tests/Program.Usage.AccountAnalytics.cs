using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestAccountAnalyticsPrecisionUnitsAndCache() {
        var payload = JsonLite.Parse("""
            {"retrieved_at":"2026-10-07T12:00:00.1234567Z",
             "profile":{"stats":{"lifetime_tokens":9007199254740993,"peak_daily_tokens":3983458877,
               "daily_usage_buckets":[{"start_date":"2026-10-07","tokens":9007199254740993}],
               "fast_mode_usage_percentage":3.69885229540918},
               "metadata":{"stats_as_of":"2026-10-07","generated_at":"2026-10-07T11:57:01.833331Z"}},
             "daily_usage":{"units":"percent","group_by":"day","future_field":{"preserved":true},
               "data":[{"date":"2026-10-07","product_surface_usage_values":{"desktop_app":25},
                "attribution":[{"thread_source":"subagent","turn_trigger":"goal","model":"model-a","surface":"desktop_app","value":12.90515794335061}],
                "models":[{"model":"model-a","speed":"standard","credits":25}]}]},
             "plan_history":{"data_as_of":"2026-10-06T00:00:00Z","coverage_complete":false,"approximate":true,"boundary_tolerance_seconds":60,
               "periods":[{"starts_at":"2026-10-03T22:18:28.571001Z","ends_at":"2026-10-04T20:05:00.986Z",
                "window_minutes":10080,"accounting_complete":false,"used_basis_points":5660.097467,
                "breakdowns":[{"dimension":"turn_trigger","rows":[{"key":"goal","basis_points":214.5398511145752}]}]}]},
             "errors":[{"endpoint":"optional","status_code":403,"message":"Unavailable"}]}
            """)!.AsObject()!;
        var analytics = ChatGptAccountAnalytics.FromJson(payload);
        AssertEqual(9007199254740993L, analytics.Profile!.LifetimeTokens, "profile integer precision beyond double mantissa");
        AssertEqual(9007199254740993L, analytics.Profile.DailyUsage[0].Tokens, "bucket integer precision");
        AssertEqual("percent", analytics.DailyUsage!.Units, "provider units survive typed parsing");
        AssertEqual(25d, analytics.DailyUsage.Data[0].Models[0].Value, "model credits field stays in enclosing percent units");
        AssertEqual("goal", analytics.DailyUsage.Data[0].Attribution[0].TurnTrigger, "structured turn attribution");
        AssertEqual(false, analytics.PlanHistory!.CoverageComplete, "partial history is retained");
        AssertEqual(true, analytics.PlanHistory.Approximate, "approximation is retained");
        AssertEqual(56.60097467, analytics.PlanHistory.Periods[0].UsedPercent, "basis points converted to percentage points");
        AssertEqual(5710010L, analytics.PlanHistory.Periods[0].StartsAt!.Value.Ticks % TimeSpan.TicksPerSecond, "fractional timestamps preserved");
        AssertEqual(403L, analytics.Errors[0].StatusCode, "endpoint availability independent of measurements");
        var serialized = JsonSerializer.Serialize(analytics);
        var restored = JsonSerializer.Deserialize<ChatGptAccountAnalytics>(serialized)!;
        AssertEqual(9007199254740993L, restored.Profile!.LifetimeTokens, "typed JSON roundtrip preserves precision");
        AssertEqual(true, restored.DailyUsage!.Raw.GetObject("future_field")!.GetBoolean("preserved"), "unrecognized provider data survives export");
        var snapshot = ChatGptUsageSnapshot.FromJson(new JsonObject().Add("account_id", "synthetic")
            .Add("account_analytics", analytics.ToJson()));
        var cached = ChatGptUsageCacheEntry.FromJson(new ChatGptUsageCacheEntry(snapshot, DateTimeOffset.UtcNow).ToJson())!;
        AssertEqual(9007199254740993L, cached.Snapshot.AccountAnalytics!.Profile!.LifetimeTokens, "usage cache preserves provider measurements");
        var text = ChatGptAccountAnalyticsFormatter.Format(analytics);
        AssertContainsText(text, "units percent", "formatted units are explicit");
        AssertContainsText(text, "incomplete accounting", "display retains partial accounting warning");
        AssertEqual(null, ChatGptAccountAnalytics.FromJson(new JsonObject()).Profile, "missing statistics remain unknown");
    }

#if !NET472
    private static void TestAccountAnalyticsIndependentFiveAccountReads() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-analytics-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var server = new LocalHttpServer(request => {
                AssertEqual("GET", request.Method, "account analytics are read-only");
                var account = request.Headers["ChatGPT-Account-Id"];
                AssertEqual("Bearer access-" + account, request.Headers["Authorization"], "all analytics endpoints use the requested credential");
                if (request.Path.Contains("/profiles/me")) {
                    if (account == "3") return new HttpResponse("private-provider-response", StatusCode: 403, StatusText: "Forbidden");
                    return new HttpResponse("{\"stats\":{\"lifetime_tokens\":" + account + "000},\"metadata\":{\"stats_as_of\":\"2026-10-07\"}}");
                }
                if (request.Path.Contains("daily-token-usage-breakdown"))
                    return new HttpResponse("{\"units\":\"percent\",\"data\":[{\"date\":\"2026-10-07\",\"product_surface_usage_values\":{\"desktop_app\":" + account + "}}]}");
                if (request.Path.Contains("plan_limit_history")) {
                    if (account == "4") return new HttpResponse("invalid-json");
                    return new HttpResponse("{\"coverage_complete\":false,\"periods\":[{\"used_basis_points\":" + account + "00}]}");
                }
                return new HttpResponse("{\"account_id\":\"" + account + "\",\"credits\":{\"balance\":0}}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "auth-store.json"));
            foreach (var id in new[] { "1", "2", "3", "4", "5" })
                store.SaveAsync(new AuthBundle("openai-codex", "access-" + id, "refresh", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = id })
                    .GetAwaiter().GetResult();
            var options = new OpenAINativeOptions { AuthStore = store, CodexHome = directory, PersistCodexAuthJson = false,
                ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/') + "/backend-api" };
            var snapshot = new ProviderLimitSnapshotService().FetchOpenAiAccountsAsync(options).GetAwaiter().GetResult();
            AssertEqual(5, snapshot.Accounts.Count, "all accounts retain independent results");
            foreach (var account in snapshot.Accounts) {
                var analytics = account.AccountAnalytics!;
                AssertEqual(true, account.IsAvailable, "optional failure does not hide successful account reading");
                AssertEqual(double.Parse(account.AccountId!, System.Globalization.CultureInfo.InvariantCulture),
                    analytics.DailyUsage!.Data[0].Total, "daily values never cross account boundaries");
                AssertEqual(account.AccountId == "3" ? (long?)null : long.Parse(account.AccountId!) * 1000,
                    analytics.Profile?.LifetimeTokens, "profile identity isolated");
                AssertEqual(account.AccountId == "4", analytics.PlanHistory is null, "invalid endpoint does not hide other measurements");
                AssertEqual(false, JsonLite.Serialize(JsonValue.From(analytics.ToJson())).Contains("private-provider-response"), "raw failure bodies excluded");
            }
            var deselected = ProviderLimitSnapshotService.WithoutCurrentCodexAccount(snapshot);
            AssertEqual(5, deselected.Accounts.Count(a => a.AccountAnalytics?.DailyUsage is not null), "account switch preserves provider data");
            var serialized = JsonSerializer.Serialize(snapshot);
            AssertEqual(true, serialized.Contains("\"lifetime_tokens\":1000"), "all-accounts export contains native measurement values");
            var analyticsOnly = new ProviderLimitAccountSnapshot("synthetic", null, null, Array.Empty<ProviderLimitWindow>(), null, null, DateTimeOffset.UtcNow) {
                AccountAnalytics = snapshot.Accounts[0].AccountAnalytics
            };
            AssertEqual(true, analyticsOnly.IsAvailable, "analytics-only evidence remains usable");
            AssertEqual(false, File.Exists(Path.Combine(directory, "auth.json")), "reads do not export a Codex login");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try {
                using var usage = new ChatGptUsageService(options);
                usage.GetAccountAnalyticsAsync(canceled.Token).GetAwaiter().GetResult();
                AssertEqual(true, false, "caller cancellation must propagate");
            } catch (OperationCanceledException) { }
        } finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestProviderThreadUsageQueryContract() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-thread-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var server = new LocalHttpServer(request => {
                AssertEqual("POST", request.Method, "provider thread query uses read-only POST");
                AssertEqual(true, request.Path.EndsWith("/thread_usage/query_v2", StringComparison.Ordinal), "v2 query route");
                AssertEqual("Bearer synthetic-access", request.Headers["Authorization"], "query retains selected auth");
                var query = JsonLite.Parse(request.Body)!.AsObject()!.GetArray("threads")!;
                AssertEqual(2, query.Count, "query groups retained");
                AssertEqual("child", query[0].AsObject()!.GetArray("descendant_thread_ids")![0].AsString(), "explicit descendants sent");
                return new HttpResponse("""
                    {"data_as_of":"2026-10-07T08:41:43.073936Z","threads":[
                    {"thread_id":"root","data_status":"partial","weekly_limit_percent":47.34203320342946,
                     "five_hour_limit_percent":null,"balance_usage_credits":"0E-10",
                     "groups":[{"model":"model-a","reasoning_effort":"high","weekly_limit_percent":47.34203320342946}]},
                    {"thread_id":"other","data_status":"unavailable","weekly_limit_percent":null,"groups":[]}]}
                    """);
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "auth-store.json"));
            store.SaveAsync(new AuthBundle("openai-codex", "synthetic-access", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "synthetic-account"
            }).GetAwaiter().GetResult();
            using var service = new ChatGptUsageService(new OpenAINativeOptions { AuthStore = store, CodexHome = directory,
                PersistCodexAuthJson = false, ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/') + "/backend-api" });
            var usage = service.QueryThreadUsageAsync(new[] {
                new ChatGptThreadUsageRequest("root", descendantThreadIds: new[] { "child" }), new ChatGptThreadUsageRequest("other")
            }).GetAwaiter().GetResult();
            AssertEqual(47.34203320342946, usage.Threads[0].WeeklyLimitPercent, "provider percentages retain precision");
            AssertEqual(0d, usage.Threads[0].BalanceUsageCredits, "scientific credit strings parsed");
            AssertEqual(null, usage.Threads[0].FiveHourLimitPercent, "missing five-hour metric stays unknown");
            AssertEqual("partial", usage.Threads[0].DataStatus, "partial results retained");
            AssertEqual(null, usage.Threads[1].WeeklyLimitPercent, "unavailable is not zero");
            AssertEqual("high", usage.Threads[0].Groups[0].ReasoningEffort, "model groups exposed");
            try {
                service.QueryThreadUsageAsync(new[] {
                    new ChatGptThreadUsageRequest("root", descendantThreadIds: new[] { "child" }), new ChatGptThreadUsageRequest("child")
                }).GetAwaiter().GetResult();
                AssertEqual(true, false, "overlapping roots must be rejected before HTTP");
            } catch (ArgumentException) { }
        } finally { Directory.Delete(directory, recursive: true); }
    }
#endif
}
