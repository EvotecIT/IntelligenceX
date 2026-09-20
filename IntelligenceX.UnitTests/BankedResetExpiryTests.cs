using IntelligenceX.Telemetry.Limits;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class BankedResetExpiryTests {
    private static TimeZoneInfo MidnightZone() => TimeZoneInfo.CreateCustomTimeZone("fixture-midnight", TimeSpan.Zero,
        "fixture", "standard", "daylight", new[] { TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 3, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 10, 1)) });

    [Theory]
    [InlineData(2, 28, 0)] // Next midnight is invalid; this date still ends on standard time.
    [InlineData(9, 30, 0)] // Final hour repeats; expiry is its last occurrence.
    [InlineData(7, 15, 1)]
    public void ResolvesTheSelectedDateRatherThanNextMidnightsOffset(int month, int day, int offsetHours) {
        var date = new DateTime(2026, month, day);
        var result = BankedResetExpiry.ResolveLocalDateEnd(date, MidnightZone());
        Assert.Equal(date.AddDays(1).AddTicks(-1), result.DateTime);
        Assert.Equal(TimeSpan.FromHours(offsetHours), result.Offset);
        Assert.Equal(date, TimeZoneInfo.ConvertTime(result, MidnightZone()).Date);
    }

    [Fact]
    public void InvalidEndOfDayUsesTheLastValidTick() {
        var transition = new DateTime(1, 1, 1, 23, 30, 12, 345);
        var zone = TimeZoneInfo.CreateCustomTimeZone("late-gap", TimeSpan.Zero, "fixture", "standard", "daylight",
            new[] { TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31),
                TimeSpan.FromHours(1), TimeZoneInfo.TransitionTime.CreateFixedDateRule(transition, 3, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 10, 1)) });
        var result = BankedResetExpiry.ResolveLocalDateEnd(new DateTime(2026, 3, 1), zone);
        Assert.Equal(new DateTime(2026, 3, 1, 23, 30, 12, 345).AddTicks(-1), result.DateTime);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }
}
