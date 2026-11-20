using System.Text.Json;
using IntelligenceX.Ledger.Hosting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class AccountLimitsTests {
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CodexLimitsKeepTheWindowCreditsAndFreeResetsTheAppServerReports() {
        using var json = JsonDocument.Parse("""
            {"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","primary":{"usedPercent":29,"windowDurationMins":10080,"resetsAt":1795303643},
             "secondary":null,"credits":{"hasCredits":true,"unlimited":false,"balance":"32955.94"},"spendControlReached":false,"planType":"pro","rateLimitReachedType":null},
             "rateLimitResetCredits":{"availableCount":3,"credits":[]},"accountId":"private-account"}
            """);
        var limit = AccountLimits.Codex(json.RootElement, Now);

        Assert.Equal(("codex", "pro", 29.0, 10080), (limit.Provider, limit.Plan, limit.UsedPercent, limit.WindowMinutes));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795303643), limit.ResetsAtUtc);
        Assert.Equal((true, "32955.94", 3, false), (limit.HasCredits, limit.CreditBalance, limit.FreeResets, limit.LimitReached));
        Assert.Null(limit.ShortUsedPercent);
        Assert.DoesNotContain("private-account", JsonSerializer.Serialize(limit));
    }

    [Fact]
    public void ACodexAccountOutOfItsWindowSaysTheLimitIsReached() {
        using var json = JsonDocument.Parse("""{"ordinaryUsageAllowed":false,"rateLimits":{"primary":{"usedPercent":100},"rateLimitReachedType":"primary"}}""");
        Assert.True(AccountLimits.Codex(json.RootElement, Now).LimitReached);
    }

    [Fact]
    public void CodexWorkspaceIdentityUsesTheSameAccountNamespaceAsTheLimitsResponse() {
        using var withWorkspace = JsonDocument.Parse("""{"userId":"person","workspaceRouting":{"chatgptAccountId":"workspace"}}""");
        using var limits = JsonDocument.Parse("""{"accountId":"workspace","rateLimits":{}}""");
        using var noWorkspace = JsonDocument.Parse("""{"userId":"person","workspaceRouting":null}""");

        Assert.Equal("codex:workspace", AccountLimits.CodexWorkspace(withWorkspace.RootElement));
        Assert.Equal(AccountLimits.Codex(limits.RootElement, Now).AccountId, AccountLimits.CodexWorkspace(withWorkspace.RootElement));
        Assert.Null(AccountLimits.CodexWorkspace(noWorkspace.RootElement));
    }

    [Fact]
    public void CodexWindowsAreSortedByDurationAndAnUnreportedStateStaysUnknown() {
        using var json = JsonDocument.Parse("""
            {"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":1795300000},
             "secondary":{"usedPercent":41,"windowDurationMins":10080,"resetsAt":1795900000}}}
            """);
        var limit = AccountLimits.Codex(json.RootElement, Now);
        Assert.Equal((41.0, 10080, 12.0), (limit.UsedPercent, limit.WindowMinutes, limit.ShortUsedPercent));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795300000), limit.ShortResetsAtUtc);
        Assert.Null(limit.LimitReached);
    }

    [Fact]
    public void ClaudeLimitsComeFromTheLastStatusLineAndAgeIsNamed() {
        var path = Path.Combine(Path.GetTempPath(), "ix-claude-limits-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            File.WriteAllText(path, """
                {"observedAtUtc":"2026-11-15T09:00:00Z","rate_limits":{"five_hour":{"used_percentage":12,"resets_at":1795400000},"seven_day":{"used_percentage":41.5,"resets_at":1795900000}}}
                """);
            var limit = AccountLimits.Claude(path, Now)!;
            Assert.Equal(("claude", 41.5, 12.0, 10080), (limit.Provider, limit.UsedPercent, limit.ShortUsedPercent, limit.WindowMinutes));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795900000), limit.ResetsAtUtc);
            Assert.Equal("Last seen by a status line 49 h ago.", limit.Note);

            Assert.Null(AccountLimits.Claude(path + ".missing", Now));
            foreach (var unreadable in new[] { "not json", "[]", """{"observedAtUtc":null}""" }) {
                File.WriteAllText(path, unreadable);
                Assert.Equal(unreadable == """{"observedAtUtc":null}""" ? null : "The saved status line snapshot couldn't be read.",
                    AccountLimits.Claude(path, Now)!.Note);
            }
        } finally {
            File.Delete(path);
        }
    }
}
