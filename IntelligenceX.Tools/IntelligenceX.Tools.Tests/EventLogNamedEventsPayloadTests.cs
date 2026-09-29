using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EventViewerX;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogNamedEventsPayloadTests {
    [Fact]
    public void RuleIdentityUsesCanonicalEventTypeWhenLegacyTypeNameDiffers() {
        var rule = (AliasStatusRule)RuntimeHelpers.GetUninitializedObject(typeof(AliasStatusRule));
        rule.SetTypeName("ADUsersStatus");

        Assert.Equal("ad_user_status", EventLogNamedEventsPayload.ResolveNamedEventName(rule));
        Assert.Equal("AliasStatusRule", EventLogNamedEventsQueryTool.ResolveRuleType(rule));
    }

    private sealed class AliasStatusRule : EventRuleBase {
        private AliasStatusRule(EventObject source) : base(source) { }
        public override List<int> EventIds => new() { 4722 };
        public override string LogName => "Security";
        public override EventType Type => EventType.ADUserStatus;
        public override bool CanHandle(EventObject source) => true;
        public void SetTypeName(string name) => TypeName = name;
    }

    [Fact]
    public void TryParseUtcValue_ShouldTreatUnspecifiedTimestampAsUtc() {
        var parsed = EventLogNamedEventsPayload.TryParseUtcValue("2026-02-20T12:34:56", out var utc);

        Assert.True(parsed);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 2, 20, 12, 34, 56, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void TryParseUtcValue_ShouldConvertOffsetTimestampToUtc() {
        var parsed = EventLogNamedEventsPayload.TryParseUtcValue("2026-02-20T12:34:56+02:00", out var utc);

        Assert.True(parsed);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 2, 20, 10, 34, 56, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void TryParseUtcValue_ShouldReturnFalseForInvalidInput() {
        var parsed = EventLogNamedEventsPayload.TryParseUtcValue("not-a-timestamp", out _);

        Assert.False(parsed);
    }
}
