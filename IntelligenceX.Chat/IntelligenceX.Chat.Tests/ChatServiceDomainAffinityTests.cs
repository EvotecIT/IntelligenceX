using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceDomainAffinityTests {
    [Fact]
    public void TryApplyDomainIntentAffinity_FiltersConflictingDomainFamilyTools() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain", "ad_domain");

        var tools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope"),
            new("ad_domain_controllers", description: "AD DCs"),
            new("dnsclientx_query", description: "DNS query"),
            new("domaindetective_domain_summary", description: "Domain summary"),
            new("eventlog_live_query", description: "Event log")
        };

        var applied = session.TryApplyDomainIntentAffinityForTesting(
            "thread-domain",
            tools,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("ad_domain", family);
        Assert.Equal(2, removedCount);
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryApplyDomainIntentAffinity_UsesToolMetadataCategory_WhenNamesDoNotUseLegacyPrefixes() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain", "ad_domain");

        var tools = new List<ToolDefinition> {
            new("directory_context_discover", description: "AD scope", category: "active_directory"),
            new("resolver_domain_overview", description: "DNS summary", category: "dns"),
            new("eventlog_live_query", description: "Event log")
        };

        var applied = session.TryApplyDomainIntentAffinityForTesting(
            "thread-domain",
            tools,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("ad_domain", family);
        Assert.Equal(1, removedCount);
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "directory_context_discover", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "resolver_domain_overview", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryApplyDomainIntentAffinity_DoesNotApplyWhenAffinityIsExpired() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetPreferredDomainIntentFamilyForTesting(
            threadId: "thread-expired",
            family: "ad_domain",
            seenUtcTicks: DateTime.UtcNow.AddHours(-12).Ticks);

        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
        };

        var applied = session.TryApplyDomainIntentAffinityForTesting(
            "thread-expired",
            tools,
            out _,
            out _,
            out _);

        Assert.False(applied);
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-expired"));
    }

    [Fact]
    public void RememberPreferredDomainIntentFamily_PrefersDominantSuccessfulReadOnlyFamily() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var calls = new[] {
            new ToolCallDto { CallId = "1", Name = "ad_scope_discovery", ArgumentsJson = "{}" },
            new ToolCallDto { CallId = "2", Name = "ad_domain_controllers", ArgumentsJson = "{}" },
            new ToolCallDto { CallId = "3", Name = "dnsclientx_query", ArgumentsJson = "{}" }
        };
        var outputs = new[] {
            new ToolOutputDto { CallId = "1", Output = "{\"ok\":true}", Ok = true },
            new ToolOutputDto { CallId = "2", Output = "{\"ok\":true}", Ok = true },
            new ToolOutputDto { CallId = "3", Output = "{\"ok\":false}", Ok = false, ErrorCode = "tool_error" }
        };

        session.RememberPreferredDomainIntentFamilyForTesting(
            threadId: "thread-votes",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-votes"));
    }

    [Fact]
    public void RememberPreferredDomainIntentFamily_ClearsAffinityWhenVotesAreAmbiguous() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetPreferredDomainIntentFamilyForTesting("thread-ambiguous", "ad_domain");
        var calls = new[] {
            new ToolCallDto { CallId = "1", Name = "ad_scope_discovery", ArgumentsJson = "{}" },
            new ToolCallDto { CallId = "2", Name = "dnsclientx_query", ArgumentsJson = "{}" }
        };
        var outputs = new[] {
            new ToolOutputDto { CallId = "1", Output = "{\"ok\":true}", Ok = true },
            new ToolOutputDto { CallId = "2", Output = "{\"ok\":true}", Ok = true }
        };

        session.RememberPreferredDomainIntentFamilyForTesting(
            threadId: "thread-ambiguous",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-ambiguous"));
    }

    [Fact]
    public void TryApplyDomainIntentAffinity_RehydratesPersistedAffinityAcrossSessionRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-domain-affinity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");

        try {
            var writerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var calls = new[] {
                new ToolCallDto { CallId = "1", Name = "ad_scope_discovery", ArgumentsJson = "{}" },
                new ToolCallDto { CallId = "2", Name = "ad_domain_controllers", ArgumentsJson = "{}" }
            };
            var outputs = new[] {
                new ToolOutputDto { CallId = "1", Output = "{\"ok\":true}", Ok = true },
                new ToolOutputDto { CallId = "2", Output = "{\"ok\":true}", Ok = true }
            };

            writerSession.RememberPreferredDomainIntentFamilyForTesting(
                threadId: "thread-persisted",
                toolCalls: calls,
                toolOutputs: outputs,
                mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            var readerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var tools = new[] {
                new ToolDefinition("ad_scope_discovery", "AD scope"),
                new ToolDefinition("dnsclientx_query", "DNS query")
            };

            var applied = readerSession.TryApplyDomainIntentAffinityForTesting(
                "thread-persisted",
                tools,
                out var filtered,
                out var family,
                out var removedCount);

            Assert.True(applied);
            Assert.Equal("ad_domain", family);
            Assert.Equal(1, removedCount);
            Assert.Contains(filtered, tool => string.Equals(tool.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_MapsNumericChoiceToDomainFamily() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify",
            "2",
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify"));
    }

    [Theory]
    [InlineData("２", "public_domain")]
    [InlineData("２）", "public_domain")]
    [InlineData("٢", "public_domain")]
    [InlineData("١", "ad_domain")]
    [InlineData("١：", "ad_domain")]
    public void TryResolvePendingDomainIntentClarificationSelection_MapsUnicodeNumericChoiceToDomainFamily(
        string input,
        string expectedFamily) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-unicode");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-unicode",
            input,
            out var family);

        Assert.True(resolved);
        Assert.Equal(expectedFamily, family);
        Assert.Equal(expectedFamily, session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-unicode"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_RehydratesPersistedClarificationContextAcrossSessionRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-domain-clarify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");

        try {
            var writerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            writerSession.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-restart");

            var readerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var resolved = readerSession.TryResolvePendingDomainIntentClarificationSelectionForTesting(
                "thread-clarify-restart",
                "1",
                out var family);

            Assert.True(resolved);
            Assert.Equal("ad_domain", family);
            Assert.Equal("ad_domain", readerSession.GetPreferredDomainIntentFamilyForTesting("thread-clarify-restart"));
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesStructuredPayload() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-structured");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-structured",
            """
            {"ix_domain_scope":{"family":"ad_domain"}}
            """,
            out var family);

        Assert.True(resolved);
        Assert.Equal("ad_domain", family);
        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-structured"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesDomainIntentChoiceMarkerPayload() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-marker");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-marker",
            """
            [DomainIntent]
            ix:domain-intent-choice:v1
            choice: 2
            """,
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-marker"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesActionSelectionPayload() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action",
            """
            {"ix_action_selection":{"id":"act_domain_scope_public","title":"Public DNS/domain scope","request":"{\"ix_domain_scope\":{\"family\":\"public_domain\"}}","mutating":false}}
            """,
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action"));
    }
}
