using System;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class LoginStatusMessageSerializationTests {
    [Fact]
    public void LoginStatusMessage_RoundTripsNativeUsageSnapshot() {
        var message = new LoginStatusMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_login_status",
            IsAuthenticated = true,
            AccountId = "acct-demo",
            NativeUsage = new NativeUsageSnapshotDto {
                AccountId = "acct-demo",
                Email = "demo@example.test",
                PlanType = "pro",
                RetrievedAtUtc = new DateTime(2026, 2, 19, 18, 30, 0, DateTimeKind.Utc),
                Source = "live",
                RateLimit = new NativeRateLimitStatusDto {
                    Allowed = true,
                    LimitReached = false,
                    Primary = new NativeRateLimitWindowDto {
                        UsedPercent = 72.5d,
                        LimitWindowSeconds = 10800,
                        ResetAfterSeconds = 1800,
                        ResetAtUnixSeconds = 1771527600
                    }
                },
                Credits = new NativeCreditsSnapshotDto {
                    HasCredits = true,
                    Unlimited = false,
                    Balance = 123.45d,
                    ApproxLocalMessages = new[] { 10, 20 },
                    ApproxCloudMessages = new[] { 30, 40 }
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(message, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);

        var typed = Assert.IsType<LoginStatusMessage>(parsed);
        Assert.True(typed.IsAuthenticated);
        Assert.Equal("acct-demo", typed.AccountId);
        Assert.NotNull(typed.NativeUsage);
        Assert.Equal("pro", typed.NativeUsage!.PlanType);
        Assert.Equal("live", typed.NativeUsage.Source);
        Assert.NotNull(typed.NativeUsage.RateLimit);
        Assert.Equal(72.5d, typed.NativeUsage.RateLimit!.Primary!.UsedPercent);
        Assert.NotNull(typed.NativeUsage.Credits);
        Assert.Equal(123.45d, typed.NativeUsage.Credits!.Balance);
        Assert.Equal(new[] { 10, 20 }, typed.NativeUsage.Credits.ApproxLocalMessages);
    }
}
