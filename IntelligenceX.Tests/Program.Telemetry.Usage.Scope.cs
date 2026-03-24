using System;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryScopeSummaryBuilderExplainsClaudeLocalVsOnlineGap() {
        var roots = new[] {
            new SourceRootRecord("src-claude-local", "claude", UsageSourceKind.LocalLogs, @"C:\Users\dev\.claude\projects"),
            new SourceRootRecord("src-claude-wsl", "claude", UsageSourceKind.LocalLogs, @"\\wsl$\Ubuntu\home\dev\.claude\projects") {
                PlatformHint = "wsl",
                MachineLabel = "Ubuntu"
            }
        };
        var snapshot = new ProviderLimitSnapshot(
            "claude",
            "Claude",
            "Claude OAuth usage API",
            "Claude Max",
            "user@example.com",
            new[] {
                new ProviderLimitWindow(
                    "session",
                    "Session",
                    92d,
                    DateTimeOffset.UtcNow.AddHours(1),
                    detail: "Account-wide online usage",
                    windowDuration: TimeSpan.FromHours(5))
            },
            summary: "Extra 10.00 / 50.00 USD",
            detailMessage: null,
            retrievedAtUtc: DateTimeOffset.UtcNow);

        var summary = UsageTelemetryScopeSummaryBuilder.Build("claude", roots, snapshot);

        AssertContainsText(summary.LocalScopeText ?? string.Empty, "2 discovered roots", "claude scope local root count");
        AssertContainsText(summary.LocalScopeText ?? string.Empty, "WSL", "claude scope local WSL");
        AssertContainsText(summary.LocalScopeText ?? string.Empty, "Ubuntu", "claude scope machine label");
        AssertContainsText(summary.OnlineScopeText ?? string.Empty, "Claude OAuth usage API", "claude scope online source");
        AssertContainsText(summary.OnlineScopeText ?? string.Empty, "account-wide online usage", "claude scope online breadth");
        AssertContainsText(summary.DifferenceText ?? string.Empty, "claude.ai web/app usage", "claude scope difference explanation");
    }

    private static void TestProviderLimitForecastingHandlesClaudeWindowDurations() {
        var now = new DateTimeOffset(2026, 03, 23, 12, 00, 00, TimeSpan.Zero);
        var window = new ProviderLimitWindow(
            "session",
            "Session",
            60d,
            now.AddHours(2),
            detail: "Account-wide online usage",
            windowDuration: TimeSpan.FromHours(5));

        var forecast = ProviderLimitForecasting.BuildForecast(window, now);

        AssertEqual(true, forecast is not null, "claude forecast available when duration supplied");
        AssertContainsText(forecast?.Summary ?? string.Empty, "On pace", "claude forecast summary");
        AssertEqual(false, forecast?.ExhaustsBeforeReset ?? true, "claude forecast on pace does not exhaust before reset");
    }
}
