using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void TryGetContinuationToolSubset_ReusesSubsetForGenericContinuationFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-generic";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "continue";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Equal(2, subset.Count);
        Assert.Equal("dnsclientx_query", subset[0].Name);
        Assert.Equal("dnsclientx_ping", subset[1].Name);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetWhenFollowUpMentionsToolOutsideSubset() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-explicit-tool";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "continue with eventlog_live_query";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetWhenFollowUpMentionsEscapedMarkdownToolOutsideSubset() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-explicit-tool-escaped";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = @"continue with `eventlog\_live\_query`";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Empty(subset);
    }

    private static List<ToolDefinition> BuildContinuationSubsetTestToolDefinitions() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        return new List<ToolDefinition> {
            new("dnsclientx_query", "dns query", schema),
            new("dnsclientx_ping", "dns ping", schema),
            new("eventlog_live_query", "eventlog query", schema),
            new("eventlog_top_events", "eventlog top", schema)
        };
    }
}
