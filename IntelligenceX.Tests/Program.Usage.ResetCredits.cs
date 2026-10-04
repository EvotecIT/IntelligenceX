using System;
using System.IO;
using System.Linq;
using System.Threading;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestResetCreditsPreserveExactTimesAndUnknownEvidence() {
        var raw = JsonLite.Parse("""
            {"rate_limit_reset_credits":{"available_count":3,"applicable_available_count":0},
             "rate_limit_reset_credit_details":{"credits":[
               {"id":"first","status":"available","reset_type":"codex_rate_limits","is_supported_by_plan":true,
                "granted_at":"2026-09-05T04:18:41.901758Z","expires_at":"2026-10-05T04:18:41.901758Z"},
               {"id":"late","status":"future-status","expires_at":"2026-10-29T18:48:50.770699Z"},
               {"id":"unknown","expires_at":9223372036854775807},
               {"id":"numeric","expires_at":1760000000.901758,"granted_at":-0.125}]},
             "rate_limit_reset_credit_history":{"events":[{"id":"event","kind":"used","occurred_at":"2026-10-03T22:18:02.243412Z"},
                {"id":"numeric-event","kind":"granted","occurred_at":1760000000.901758}],
                "window_start":"2026-09-04T10:38:49Z","as_of":"2026-10-04T10:38:49Z","next_cursor":"next-page"},"custom":"retained"}
            """).AsObject()!;
        var snapshot = ChatGptUsageSnapshot.FromJson(raw);
        var reset = snapshot.ResetCredits!;
        AssertEqual(3L, reset.AvailableCount, "available count distinct from applicability");
        AssertEqual(0L, reset.ApplicableAvailableCount, "zero applicability preserved");
        AssertEqual(9017580L, reset.Credits[0].ExpiresAt!.Value.Ticks % TimeSpan.TicksPerSecond, "expiry microseconds retained");
        var numericExpiry = DateTimeOffset.FromUnixTimeSeconds(1760000000).AddTicks(9017580);
        AssertEqual(numericExpiry, reset.Credits[3].ExpiresAt, "numeric expiry retains fractional Unix seconds");
        AssertEqual(DateTimeOffset.FromUnixTimeSeconds(0).AddTicks(-1250000), reset.Credits[3].GrantedAt, "negative fractional Unix instant retained");
        AssertEqual(numericExpiry, reset.History[1].OccurredAt, "numeric event time retains fractional Unix seconds");
        AssertEqual(null, reset.Credits[2].ExpiresAt, "invalid expiry stays unknown");
        AssertEqual("future-status", reset.Credits[1].Status, "unknown status preserved");
        AssertEqual("used", reset.History[0].Kind, "provider history parsed");
        AssertEqual("next-page", reset.HistoryNextCursor, "partial history remains explicit");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "Central European Standard Time" : "Europe/Warsaw");
        AssertEqual(true, ChatGptResetCreditsFormatter.FormatTimestamp(reset.Credits[0].ExpiresAt, zone).Contains("06:18:41.901758 +02:00"), "summer expiry offset");
        AssertEqual(true, ChatGptResetCreditsFormatter.FormatTimestamp(reset.Credits[1].ExpiresAt, zone).Contains("19:48:50.770699 +01:00"), "winter expiry offset");
        var roundTrip = ChatGptUsageSnapshot.FromJson(JsonLite.Parse(JsonLite.Serialize(JsonValue.From(snapshot.ToJson()))).AsObject()!);
        AssertEqual(reset.Credits[0].ExpiresAt, roundTrip.ResetCredits!.Credits[0].ExpiresAt, "cache/JSON retains exact grant expiry");
        AssertEqual(numericExpiry, roundTrip.ResetCredits.Credits[3].ExpiresAt, "cache/JSON retains fractional numeric expiry");
        AssertEqual("retained", roundTrip.Raw.GetString("custom"), "unrecognized provider evidence retained");
        var countsOnly = ChatGptUsageSnapshot.FromJson(JsonLite.Parse("{\"rate_limit_reset_credits\":{\"available_count\":0}}").AsObject()!);
        AssertEqual(false, countsOnly.ResetCredits!.DetailsAvailable, "count-only response does not claim a verified empty grant list");
        AssertEqual(null, ChatGptUsageSnapshot.FromJson(new JsonObject()).ResetCredits, "missing reset data is unknown");
    }

#if !NET472
    private static void TestResetCreditsFetchIndependentlyForFiveAccounts() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-reset-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var server = new LocalHttpServer(request => {
                AssertEqual("GET", request.Method, "reset monitoring is read-only");
                var account = request.Headers["ChatGPT-Account-Id"];
                AssertEqual("Bearer access-" + account, request.Headers["Authorization"], "each endpoint uses the same account credential");
                if (request.Path.EndsWith("/history", StringComparison.Ordinal))
                    return new HttpResponse("{\"events\":[{\"id\":\"" + account + "-event\",\"kind\":\"granted\",\"occurred_at\":\"2026-09-05T04:18:41Z\"}]}");
                if (request.Path.EndsWith("/rate-limit-reset-credits", StringComparison.Ordinal)) {
                    if (account == "fifth") return new HttpResponse("private-response", StatusCode: 403, StatusText: "Forbidden");
                    if (account == "third") return new HttpResponse("invalid-json");
                    return new HttpResponse("{\"available_count\":1,\"credits\":[{\"id\":\"" + account + "-reset\",\"status\":\"available\",\"expires_at\":\"2026-10-05T04:18:41.901758Z\"}]}");
                }
                var credits = account == "fourth" ? "" : ",\"credits\":{\"has_credits\":true,\"balance\":\"125.75\"}";
                var window = account == "first" ? ",\"rate_limit\":{\"primary_window\":{\"used_percent\":25,\"limit_window_seconds\":604800}}" : "";
                return new HttpResponse("{\"account_id\":\"" + account + "\"" + credits + window + ",\"rate_limit_reset_credits\":{\"available_count\":1}}");
            });
            var store = new FileAuthBundleStore(Path.Combine(directory, "auth-store.json"));
            foreach (var id in new[] { "first", "second", "third", "fourth", "fifth" })
                store.SaveAsync(new AuthBundle("openai-codex", "access-" + id, "refresh", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = id }).GetAwaiter().GetResult();
            var options = new OpenAINativeOptions { AuthStore = store, CodexHome = directory, PersistCodexAuthJson = false,
                ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/') + "/backend-api" };
            var snapshot = new ProviderLimitSnapshotService().FetchOpenAiAccountsAsync(options).GetAwaiter().GetResult();
            AssertEqual(5, snapshot.Accounts.Count, "all five accounts retained");
            foreach (var account in snapshot.Accounts) {
                AssertEqual(true, account.IsAvailable, "optional reset failure does not hide usage");
                AssertEqual(account.AccountId == "fourth" ? (double?)null : 125.75, account.CreditBalance, "numeric credit balance remains available per account");
                AssertEqual(account.AccountId == "first" ? 1 : 0, account.Windows.Count, "successful readings can omit limit windows");
                AssertEqual(1L, account.ResetCredits!.AvailableCount, "reported reset count retained");
                AssertEqual(account.AccountId + "-event", account.ResetCredits.History[0].Id, "history never crosses accounts");
                if (account.AccountId is "third" or "fifth") {
                    AssertEqual(false, account.ResetCredits.DetailsAvailable, "failure is not an empty live balance");
                    AssertEqual(false, account.ResetCreditsError!.Contains("private-response"), "raw endpoint errors never exposed");
                } else AssertEqual(account.AccountId + "-reset", account.ResetCredits.Credits[0].Id, "grants never cross accounts");
            }
            var unselected = ProviderLimitSnapshotService.WithoutCurrentCodexAccount(snapshot);
            AssertEqual(5, unselected.Accounts.Count(a => a.ResetCredits is not null), "account switch preserves reset evidence");
            AssertEqual(4, unselected.Accounts.Count(a => a.CreditBalance == 125.75), "account switch preserves numeric credit balances");
            var resetOnly = unselected.Accounts.Single(a => a.AccountId == "fourth");
            var resetProvider = new ProviderLimitSnapshot("codex", "Codex", "test", null, null,
                Array.Empty<ProviderLimitWindow>(), null, null, DateTimeOffset.UtcNow, new[] { resetOnly });
            AssertEqual(true, resetProvider.IsAvailable, "provider with reset-only evidence remains available");
            AssertEqual(false, ProviderLimitForecasting.BuildAccountAdvisories(resetProvider).Single().IsRecommended,
                "reset availability does not invent usable window capacity");
            var zeroBalance = new ProviderLimitAccountSnapshot("zero", null, null, Array.Empty<ProviderLimitWindow>(), null, null, DateTimeOffset.UtcNow) { CreditBalance = 0 };
            AssertEqual(true, zeroBalance.IsAvailable, "a reported zero credit balance is valid evidence");
            var unknown = new ProviderLimitAccountSnapshot("unknown", null, null, Array.Empty<ProviderLimitWindow>(), null, null, DateTimeOffset.UtcNow) {
                ResetCredits = ChatGptResetCredits.FromJson(new JsonObject().Add("rate_limit_reset_credits", new JsonObject()))
            };
            AssertEqual(false, unknown.IsAvailable, "empty reset metadata is not an available reading");
        } finally { Directory.Delete(directory, recursive: true); }
    }
#endif
}
