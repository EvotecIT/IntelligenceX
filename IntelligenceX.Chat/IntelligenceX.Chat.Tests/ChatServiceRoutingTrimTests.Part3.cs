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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        session.RememberUserIntentForTesting("thread-001", "Please run forest-wide replication and LDAP diagnostics.");
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-001", "run now");

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow-up:", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run now", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequestForTesting_ThrowsWhenThreadIdIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var ex = Assert.Throws<ArgumentNullException>(() => session.ExpandContinuationUserRequestForTesting(null!, "run now"));
        Assert.Equal("threadId", ex.ParamName);
    }

    [Fact]
    public void RememberUserIntentForTesting_ThrowsWhenUserRequestIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var ex = Assert.Throws<ArgumentNullException>(() => session.RememberUserIntentForTesting("thread-001", null!));
        Assert.Equal("userRequest", ex.ParamName);
    }

    [Fact]
    public void RememberPendingActionsForTesting_ThrowsWhenAssistantReplyIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var ex = Assert.Throws<ArgumentNullException>(() => session.RememberPendingActionsForTesting("thread-001", null!));
        Assert.Equal("assistantReply", ex.ParamName);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesActCommandToPendingActionRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var assistantDraft = """
            Pick one:

            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            """;

        session.RememberPendingActionsForTesting("thread-001", assistantDraft);
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-001", "/act act_001");

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures loose pending-action blocks (without ix:action marker envelope) can still be selected via /act.
    /// </summary>
    [Fact]
    public void ExpandContinuationUserRequest_ResolvesActCommandFromLoosePendingActionBlock() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        session.RememberPendingActionsForTesting("thread-001", assistantDraft);
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-001", "/act act_repl_now");

        Assert.Contains("ad_replication_summary", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_action_selection", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesMultiLinePendingActionRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            Also capture the top 5 errors from System log.
            reply: /act act_001
            """;

        session.RememberPendingActionsForTesting("thread-001", assistantDraft);
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-001", "/act act_001");

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("top 5 errors", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesOrdinalSelectionToSecondPendingAction() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        session.RememberPendingActionsForTesting("thread-002", assistantDraft);
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-002", "2");

        Assert.Contains("second thing", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveOrdinalWhenMessageIsNotASelection() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        session.RememberPendingActionsForTesting("thread-002", assistantDraft);
        var input = "2 servers are down";
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-002", input);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotTreatIdentityAsIdField() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var assistantDraft = """
            [Action]
            ix:action:v1
            identity: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            """;

        session.RememberPendingActionsForTesting("thread-001", assistantDraft);
        var expanded = session.ExpandContinuationUserRequestForTesting("thread-001", "/act act_001");

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

    [Theory]
    [InlineData(0, "maxToolRounds must be between 1 and 256.")]
    [InlineData(257, "maxToolRounds must be between 1 and 256.")]
    public void TryValidateChatRequestOptions_RejectsOutOfRangeMaxToolRounds(int maxToolRounds, string expectedError) {
        var invokeArgs = new object?[] {
            new ChatRequestOptions { MaxToolRounds = maxToolRounds },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal(expectedError, Assert.IsType<string>(invokeArgs[1]));
    }

    [Theory]
    [InlineData(-1, "turnTimeoutSeconds must be between 0 and 3600.")]
    [InlineData(3601, "turnTimeoutSeconds must be between 0 and 3600.")]
    public void TryValidateChatRequestOptions_RejectsOutOfRangeTurnTimeout(int turnTimeoutSeconds, string expectedError) {
        var invokeArgs = new object?[] {
            new ChatRequestOptions { TurnTimeoutSeconds = turnTimeoutSeconds },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal(expectedError, Assert.IsType<string>(invokeArgs[1]));
    }

    [Theory]
    [InlineData(-1, "toolTimeoutSeconds must be between 0 and 3600.")]
    [InlineData(3601, "toolTimeoutSeconds must be between 0 and 3600.")]
    public void TryValidateChatRequestOptions_RejectsOutOfRangeToolTimeout(int toolTimeoutSeconds, string expectedError) {
        var invokeArgs = new object?[] {
            new ChatRequestOptions { ToolTimeoutSeconds = toolTimeoutSeconds },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal(expectedError, Assert.IsType<string>(invokeArgs[1]));
    }

    [Fact]
    public void TryValidateChatRequestOptions_AcceptsBoundaryRoundsAndTimeouts() {
        var invokeArgs = new object?[] {
            new ChatRequestOptions {
                MaxToolRounds = 256,
                TurnTimeoutSeconds = 0,
                ToolTimeoutSeconds = 3600
            },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.True(result);
        Assert.Null(invokeArgs[1]);
    }

    [Fact]
    public void TryValidateChatRequestOptions_RejectsInvalidParallelToolMode() {
        var invokeArgs = new object?[] {
            new ChatRequestOptions {
                ParallelToolMode = "parallelize_everything"
            },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal(
            "parallelToolMode must be one of: auto, force_serial, allow_parallel.",
            Assert.IsType<string>(invokeArgs[1]));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("force_serial")]
    [InlineData("serial")]
    [InlineData("allow_parallel")]
    [InlineData("allow-parallel")]
    [InlineData("on")]
    [InlineData("off")]
    public void TryValidateChatRequestOptions_AcceptsCanonicalAndAliasParallelToolModeValues(string parallelToolMode) {
        var invokeArgs = new object?[] {
            new ChatRequestOptions {
                ParallelToolMode = parallelToolMode
            },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.True(result);
        Assert.Null(invokeArgs[1]);
    }

    [Theory]
    [InlineData(-1, "maxCandidateTools must be between 0 and 256.")]
    [InlineData(257, "maxCandidateTools must be between 0 and 256.")]
    public void TryValidateChatRequestOptions_RejectsOutOfRangeMaxCandidateTools(int maxCandidateTools, string expectedError) {
        var invokeArgs = new object?[] {
            new ChatRequestOptions {
                MaxCandidateTools = maxCandidateTools
            },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.False(result);
        Assert.Equal(expectedError, Assert.IsType<string>(invokeArgs[1]));
    }

    [Fact]
    public void TryValidateChatRequestOptions_AcceptsBoundaryMaxCandidateTools() {
        var invokeArgs = new object?[] {
            new ChatRequestOptions {
                MaxCandidateTools = 256
            },
            null
        };

        var result = Assert.IsType<bool>(TryValidateChatRequestOptionsMethod.Invoke(null, invokeArgs));
        Assert.True(result);
        Assert.Null(invokeArgs[1]);
    }
}
