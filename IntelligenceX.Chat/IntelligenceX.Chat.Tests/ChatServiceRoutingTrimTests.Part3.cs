using System;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ExtractPrimaryUserRequest_ReturnsEmptyForAllCodeMessages() {
        var input = """
            ```powershell
            Get-EventLog -LogName System
            ```
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ExtractIntentUserText_RemovesFenceMarkersButKeepsContent() {
        var input = """
            ```powershell
            Get-EventLog -LogName System
            ```
            """;

        var result = ExtractIntentUserTextMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Get-EventLog -LogName System", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_IncludesLastIntent() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        RememberUserIntentMethod.Invoke(session, new object?[] { "thread-001", "Please run forest-wide replication and LDAP diagnostics." });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow-up:", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run now", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesActCommandToPendingActionRequest() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pick one:

            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures loose pending-action blocks (without ix:action marker envelope) can still be selected via /act.
    /// </summary>
    [Fact]
    public void ExpandContinuationUserRequest_ResolvesActCommandFromLoosePendingActionBlock() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            id
            act_repl_now
            title
            Run fresh AD replication summary now
            request
            Execute ad_replication_summary for current forest/domain scope and return current health, failed edges, stale links, and top replication errors.
            reply
            /act act_repl_now
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_repl_now" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("ad_replication_summary", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_action_selection", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesMultiLinePendingActionRequest() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            Also capture the top 5 errors from System log.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("top 5 errors", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesOrdinalSelectionToSecondPendingAction() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001

            [Action]
            ix:action:v1
            id: act_002
            title: Second
            request: Do second thing.
            reply: /act act_002
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-002", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-002", "2" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("second thing", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveOrdinalWhenMessageIsNotASelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001

            [Action]
            ix:action:v1
            id: act_002
            title: Second
            request: Do second thing.
            reply: /act act_002
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-002", assistantDraft });
        var input = "2 servers are down";
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-002", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotTreatIdentityAsIdField() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            identity: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        Assert.DoesNotContain("ix:action-selection:v1", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/act act_001", expanded);
    }

    [Fact]
    public void TryValidateChatRequestOptions_RejectsOutOfRangeTemperature() {
        var invokeArgs = new object?[] {
            new ChatRequestOptions { Temperature = 2.01d },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal("temperature must be between 0 and 2.", Assert.IsType<string>(invokeArgs[1]));
    }

    [Fact]
    public void TryValidateChatRequestOptions_AcceptsValidTemperature() {
        var invokeArgs = new object?[] {
            new ChatRequestOptions { Temperature = 1.5d },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.True(result);
        Assert.Null(invokeArgs[1]);
    }
}
