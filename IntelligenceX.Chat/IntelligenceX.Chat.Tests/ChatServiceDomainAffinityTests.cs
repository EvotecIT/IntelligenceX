using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceDomainAffinityTests {
    [Fact]
    public void TryApplyDomainIntentAffinity_FiltersConflictingDomainFamilyTools() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain", "ad_domain");

        var tools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("ad_domain_controllers", description: "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" }),
            new("eventlog_live_query", description: "Event log", tags: new[] { "domain_family:ad_domain" })
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
    public void TryApplyDomainIntentAffinity_UsesExplicitDomainFamilyMetadata_WhenNamesDoNotUseLegacyPrefixes() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain", "ad_domain");

        var tools = new List<ToolDefinition> {
            new("directory_context_discover", description: "AD scope", category: "active_directory", tags: new[] { "domain_family:ad_domain" }),
            new("resolver_domain_overview", description: "DNS summary", category: "dns", tags: new[] { "domain_family:public_domain" }),
            new("eventlog_live_query", description: "Event log", tags: new[] { "domain_family:ad_domain" })
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
    public void TryApplyDomainIntentAffinity_UsesExplicitDomainFamilyTags_WhenNamesAreCustom() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain-tagged", "ad_domain");

        var tools = new List<ToolDefinition> {
            new("custom_directory_probe", description: "Custom AD probe", tags: new[] { "domain_family:ad_domain" }),
            new("custom_dns_probe", description: "Custom DNS probe", tags: new[] { "domain_family:public_domain" }),
            new("eventlog_live_query", description: "Event log")
        };

        var applied = session.TryApplyDomainIntentAffinityForTesting(
            "thread-domain-tagged",
            tools,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("ad_domain", family);
        Assert.Equal(1, removedCount);
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "custom_directory_probe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "custom_dns_probe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryApplyDomainIntentAffinity_DoesNotApplyWhenAffinityIsExpired() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
                new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
                new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify",
            "2",
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotResolveUnavailableFamilyChoice() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-unavailable");
        var availableDefinitions = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_domain_controllers", "AD DCs", tags: new[] { "domain_family:ad_domain" })
        };

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-unavailable",
            "2",
            availableDefinitions,
            out var family);

        Assert.False(resolved);
        Assert.Equal(string.Empty, family);
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-unavailable"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ResolvesAvailableFamilyChoiceWhenSingleFamilyIsPresent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-single-family");
        var availableDefinitions = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_domain_controllers", "AD DCs", tags: new[] { "domain_family:ad_domain" })
        };

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-single-family",
            "1",
            availableDefinitions,
            out var family);

        Assert.True(resolved);
        Assert.Equal("ad_domain", family);
        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-single-family"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_MapsOrdinalToCustomFamilyWhenAvailable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-custom-family");
        var availableDefinitions = new[] {
            new ToolDefinition(
                "ad_pack_info",
                "AD pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }),
            new ToolDefinition(
                "corp_pack_info",
                "Corp pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "corp_pack",
                    DomainIntentFamily = "corp_internal",
                    DomainIntentActionId = "act_domain_scope_corp_internal_custom"
                })
        };

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-custom-family",
            "2",
            availableDefinitions,
            out var family);

        Assert.True(resolved);
        Assert.Equal("corp_internal", family);
        Assert.Equal("corp_internal", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-custom-family"));
    }

    [Theory]
    [InlineData("２", "public_domain")]
    [InlineData("２）", "public_domain")]
    [InlineData("٢", "public_domain")]
    [InlineData("②", "public_domain")]
    [InlineData("❷", "public_domain")]
    [InlineData("١", "ad_domain")]
    [InlineData("١：", "ad_domain")]
    [InlineData("①", "ad_domain")]
    [InlineData("❶", "ad_domain")]
    public void TryResolvePendingDomainIntentClarificationSelection_MapsUnicodeNumericChoiceToDomainFamily(
        string input,
        string expectedFamily) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesDomainIntentFamilyMarkerPayload() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-family-marker");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-family-marker",
            """
            [DomainIntent]
            ix:domain-intent:v1
            family: public_domain
            """,
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-family-marker"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesActionSelectionPayload() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesActionSelectionPayloadWithObjectRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-object-request");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-object-request",
            """
            {"ix_action_selection":{"id":"act_domain_scope_ad","title":"ad_domain","request":{"ix_domain_scope":{"family":"ad_domain"}},"mutating":false}}
            """,
            out var family);

        Assert.True(resolved);
        Assert.Equal("ad_domain", family);
        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action-object-request"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesCustomActionIdFromAvailableRoutingContracts() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-custom");

        var availableDefinitions = new List<ToolDefinition> {
            new(
                name: "ad_pack_info",
                description: "AD pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_custom"
                }),
            new(
                name: "domaindetective_pack_info",
                description: "Domain pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "domaindetective",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = "act_domain_scope_public_custom"
                })
        };

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-custom",
            """
            {"ix_action_selection":{"id":"act_domain_scope_public_custom","title":"public_domain","request":{"ix_domain_scope":{"family":"public_domain"}},"mutating":false}}
            """,
            availableDefinitions,
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action-custom"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotAcceptDefaultActionIdWhenRoutingDeclaresCustomIds() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-custom-default");

        var availableDefinitions = new List<ToolDefinition> {
            new(
                name: "ad_pack_info",
                description: "AD pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_custom"
                }),
            new(
                name: "domaindetective_pack_info",
                description: "Domain pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "domaindetective",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = "act_domain_scope_public_custom"
                })
        };

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-custom-default",
            "/act act_domain_scope_public",
            availableDefinitions,
            out var family);

        Assert.False(resolved);
        Assert.Equal(string.Empty, family);
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action-custom-default"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_AcceptsAllDeclaredFamilyActionIds_RegardlessOfDefinitionOrder() {
        var availableDefinitionsPrimaryOrder = new List<ToolDefinition> {
            new(
                name: "ad_pack_info_primary",
                description: "AD pack primary mapping",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_primary"
                }),
            new(
                name: "ad_pack_info_secondary",
                description: "AD pack conflicting mapping",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_secondary"
                }),
            new(
                name: "domaindetective_pack_info",
                description: "Domain pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "domaindetective",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = "act_domain_scope_public_custom"
                })
        };
        var availableDefinitionsReversedOrder = availableDefinitionsPrimaryOrder.AsEnumerable().Reverse().ToList();

        var sessionPrimaryOrder = ChatServiceTestSessionFactory.CreateIsolatedSession();
        sessionPrimaryOrder.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-conflict-a");
        var secondaryActionPrimaryOrderResolved = sessionPrimaryOrder.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-conflict-a",
            "/act act_domain_scope_ad_secondary",
            availableDefinitionsPrimaryOrder,
            out var secondaryFamilyPrimaryOrder);
        Assert.True(secondaryActionPrimaryOrderResolved);
        Assert.Equal("ad_domain", secondaryFamilyPrimaryOrder);

        var sessionReversedOrder = ChatServiceTestSessionFactory.CreateIsolatedSession();
        sessionReversedOrder.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-conflict-b");
        var secondaryActionReversedOrderResolved = sessionReversedOrder.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-conflict-b",
            "/act act_domain_scope_ad_secondary",
            availableDefinitionsReversedOrder,
            out var secondaryFamilyReversedOrder);
        Assert.True(secondaryActionReversedOrderResolved);
        Assert.Equal("ad_domain", secondaryFamilyReversedOrder);

        sessionReversedOrder.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-conflict-b");
        var primaryActionResolved = sessionReversedOrder.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-conflict-b",
            "/act act_domain_scope_ad_primary",
            availableDefinitionsReversedOrder,
            out var primaryFamily);

        Assert.True(primaryActionResolved);
        Assert.Equal("ad_domain", primaryFamily);
        Assert.Equal("ad_domain", sessionReversedOrder.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action-conflict-b"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ResolvesAmbiguousCrossFamilyActionIdDeterministicallyAcrossDefinitionOrder() {
        var availableDefinitionsPrimaryOrder = new List<ToolDefinition> {
            new(
                name: "ad_pack_info",
                description: "AD pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_shared"
                }),
            new(
                name: "domaindetective_pack_info",
                description: "Domain pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "domaindetective",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = "act_domain_scope_shared"
                })
        };
        var availableDefinitionsReversedOrder = availableDefinitionsPrimaryOrder.AsEnumerable().Reverse().ToList();

        var sessionPrimaryOrder = ChatServiceTestSessionFactory.CreateIsolatedSession();
        sessionPrimaryOrder.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-ambiguous-a");
        var resolvedPrimaryOrder = sessionPrimaryOrder.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-ambiguous-a",
            "/act act_domain_scope_shared",
            availableDefinitionsPrimaryOrder,
            out var familyPrimaryOrder);

        var sessionReversedOrder = ChatServiceTestSessionFactory.CreateIsolatedSession();
        sessionReversedOrder.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-ambiguous-b");
        var resolvedReversedOrder = sessionReversedOrder.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-ambiguous-b",
            "/act act_domain_scope_shared",
            availableDefinitionsReversedOrder,
            out var familyReversedOrder);

        Assert.Equal(resolvedPrimaryOrder, resolvedReversedOrder);
        Assert.Equal(familyPrimaryOrder, familyReversedOrder);
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotInventDefaultActionWhenCatalogMappingIsMissing() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-action-default-fallback");

        var availableDefinitions = new List<ToolDefinition> {
            new(
                name: "ad_pack_info",
                description: "AD pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }),
            new(
                name: "domaindetective_pack_info",
                description: "Domain pack",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "domaindetective",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = "act_domain_scope_public_custom"
                })
        };

        // Simulate partial in-memory contract drift after validation.
        availableDefinitions[0].Routing!.DomainIntentActionId = string.Empty;

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-action-default-fallback",
            "/act act_domain_scope_ad",
            availableDefinitions,
            out var family);

        Assert.False(resolved);
        Assert.Equal(string.Empty, family);
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-action-default-fallback"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesExplicitActSelectionCommand() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-explicit-act");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-explicit-act",
            "/act act_domain_scope_public",
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-explicit-act"));
    }

    [Theory]
    [InlineData("AD", "ad_domain")]
    [InlineData("LDAP", "ad_domain")]
    [InlineData("DC", "ad_domain")]
    [InlineData("DNS", "public_domain")]
    [InlineData("MX", "public_domain")]
    [InlineData("SPF", "public_domain")]
    [InlineData("DMARC", "public_domain")]
    [InlineData("نتيجة DNS", "public_domain")]
    [InlineData("Necesito revisar LDAP y Kerberos en este dominio", "ad_domain")]
    [InlineData("Verifier LDAP du domaine", "ad_domain")]
    [InlineData("Por favor revisar DNS publico", "public_domain")]
    [InlineData("Verifier DNS public du domaine", "public_domain")]
    [InlineData("Use adplayground for this domain", "ad_domain")]
    [InlineData("Use ad_playground for this domain", "ad_domain")]
    [InlineData("Use ad-playground for this domain", "ad_domain")]
    [InlineData("active_directory diagnostics for this domain", "ad_domain")]
    [InlineData("Run domaindetective checks for this zone", "public_domain")]
    [InlineData("Run domain_detective checks for this zone", "public_domain")]
    [InlineData("dnsclientx resolver baseline", "public_domain")]
    [InlineData("dns_client_x resolver baseline", "public_domain")]
    [InlineData("Run SYSVOL replication health checks", "ad_domain")]
    [InlineData("Check forest trust posture for this domain", "ad_domain")]
    [InlineData("Narysuj diagram cross-forest trusted paths", "ad_domain")]
    [InlineData("sprawdz trusty cross-forest", "ad_domain")]
    [InlineData("Run DNSSEC and CAA checks", "public_domain")]
    [InlineData("Validate WHOIS and BIMI posture", "public_domain")]
    [InlineData("Check mta-sts and dkim records", "public_domain")]
    [InlineData("act_domain_scope_public", "public_domain")]
    [InlineData("ad_domain", "ad_domain")]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesLanguageNeutralTechnicalSignals(
        string input,
        string expectedFamily) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-signal");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-signal",
            input,
            out var family);

        Assert.True(resolved);
        Assert.Equal(expectedFamily, family);
        Assert.Equal(expectedFamily, session.GetPreferredDomainIntentFamilyForTesting("thread-clarify-signal"));
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotResolveWhenTechnicalSignalsConflict() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-conflict");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-conflict",
            "AD and DNS",
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotTreatLowercaseAdAsStandaloneAdSignal() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-lowercase-ad");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-lowercase-ad",
            "ad and dns",
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotResolveLowercaseAdAloneAsDomainSignal() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-lowercase-ad-alone");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-lowercase-ad-alone",
            "ad",
            out var family);

        Assert.False(resolved);
        Assert.Equal(string.Empty, family);
    }

    [Fact]
    public void TryApplyDomainIntentSignalRoutingHint_FiltersMixedToolsAndRemembersAdPreference() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_domain_controllers", "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "Domain summary", tags: new[] { "domain_family:public_domain" })
        };

        var applied = session.TryApplyDomainIntentSignalRoutingHintForTesting(
            "thread-domain-signal-ad",
            "Run LDAP and GPO checks for this domain.",
            tools,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("ad_domain", family);
        Assert.Equal(2, removedCount);
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-domain-signal-ad"));
    }

    [Fact]
    public void TryApplyDomainIntentSignalRoutingHint_FiltersMixedToolsAndRemembersPublicPreference() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "Domain summary", tags: new[] { "domain_family:public_domain" })
        };

        var applied = session.TryApplyDomainIntentSignalRoutingHintForTesting(
            "thread-domain-signal-public",
            "Necesito revisar MX y SPF del dominio público.",
            tools,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("public_domain", family);
        Assert.Equal(1, removedCount);
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("public_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-domain-signal-public"));
    }

    [Fact]
    public void TryApplyDomainIntentSignalRoutingHint_DoesNotApplyWhenSignalsConflict() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var applied = session.TryApplyDomainIntentSignalRoutingHintForTesting(
            "thread-domain-signal-conflict",
            "LDAP and DNS both please.",
            tools,
            out _,
            out _,
            out _);

        Assert.False(applied);
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting("thread-domain-signal-conflict"));
    }

    [Fact]
    public void TryApplyDomainIntentSignalRoutingHint_ReexpandsFromFullCatalogWhenSubsetHasConflictingFamily() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var selectedSubset = new[] {
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "Domain summary", tags: new[] { "domain_family:public_domain" })
        };
        var fullCandidates = new[] {
            new ToolDefinition("ad_trust", "AD trust", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("eventlog_live_query", "Event log", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var applied = session.TryApplyDomainIntentSignalRoutingHintForTesting(
            "thread-domain-signal-reexpand",
            "sprawdz trusty cross-forest i narysuj diagram",
            selectedSubset,
            fullCandidates,
            out var filtered,
            out var family,
            out var removedCount);

        Assert.True(applied);
        Assert.Equal("ad_domain", family);
        Assert.Equal(1, removedCount);
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "ad_trust", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filtered, tool => string.Equals(tool.Name, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ad_domain", session.GetPreferredDomainIntentFamilyForTesting("thread-domain-signal-reexpand"));
    }

    [Theory]
    [InlineData("AD and DNS")]
    [InlineData("Need LDAP + MX checks")]
    [InlineData("kerberos DNS MX")]
    [InlineData("act_domain_scope_ad with dns checks")]
    [InlineData("replication with DNSSEC checks")]
    public void HasConflictingDomainIntentSignalsForTesting_ReturnsTrueForMixedSignals(string input) {
        Assert.True(ChatServiceSession.HasConflictingDomainIntentSignalsForTesting(input));
    }

    [Theory]
    [InlineData("AD LDAP GPO")]
    [InlineData("DNS MX SPF")]
    [InlineData("domain summary")]
    [InlineData("ad and dns")]
    public void HasConflictingDomainIntentSignalsForTesting_ReturnsFalseWhenSignalsDoNotConflict(string input) {
        Assert.False(ChatServiceSession.HasConflictingDomainIntentSignalsForTesting(input));
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsTrueWhenSignalsConflictAndFamiliesAvailable() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Please do AD LDAP + DNS MX together now.",
            tools);

        Assert.True(shouldForce);
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsFalseWhenExplicitFamilyMarkerIsPresent() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            """
            [DomainIntent]
            ix:domain-intent:v1
            family: public_domain
            AD LDAP + DNS MX
            """,
            tools);

        Assert.False(shouldForce);
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsFalseWhenOnlyOneFamilyIsAvailable() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_domain_controllers", "AD DCs", tags: new[] { "domain_family:ad_domain" })
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Please do AD LDAP + DNS MX together now.",
            tools);

        Assert.False(shouldForce);
    }

    [Fact]
    public void ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting_ReturnsTrueForCompactFollowUpWithPreferredFamily() {
        var suppressed = ChatServiceSession.ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
            compactFollowUpTurn: true,
            hasPreferredDomainIntentFamily: true,
            hasFreshPendingActionContext: false,
            conflictingDomainSignals: false);

        Assert.True(suppressed);
    }

    [Fact]
    public void ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting_ReturnsTrueForCompactFollowUpWithPendingActionContext() {
        var suppressed = ChatServiceSession.ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
            compactFollowUpTurn: true,
            hasPreferredDomainIntentFamily: false,
            hasFreshPendingActionContext: true,
            conflictingDomainSignals: false);

        Assert.True(suppressed);
    }

    [Fact]
    public void ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting_ReturnsFalseWhenSignalsConflict() {
        var suppressed = ChatServiceSession.ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
            compactFollowUpTurn: true,
            hasPreferredDomainIntentFamily: true,
            hasFreshPendingActionContext: true,
            conflictingDomainSignals: true);

        Assert.False(suppressed);
    }

    [Fact]
    public void CompactFollowUp_WithUnresolvedFallbackChoice_KeepsPendingActionContextAndSuppressesDomainIntentClarification() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-compact-follow-up";
        const string assistantChoices = """
                                      Chcesz, zebym od razu zrobil:
                                      - szybki drill-down z detalami polaczen
                                      - od razu pelny health-check DC
                                      """;

        session.RememberPendingActionsForTesting(threadId, assistantChoices);
        Assert.True(session.HasFreshPendingActionsContextForTesting(threadId));

        var expanded = session.ExpandContinuationUserRequestForTesting(threadId, "tak poprosze to drugie");
        Assert.Equal("tak poprosze to drugie", expanded);
        Assert.True(session.HasFreshPendingActionsContextForTesting(threadId));

        var suppressed = ChatServiceSession.ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
            compactFollowUpTurn: true,
            hasPreferredDomainIntentFamily: false,
            hasFreshPendingActionContext: session.HasFreshPendingActionsContextForTesting(threadId),
            conflictingDomainSignals: false);

        Assert.True(suppressed);
    }

    [Theory]
    [InlineData("Check domain health for corp.contoso.com and contoso.com.")]
    [InlineData("Necesito revisar corp.contoso.com y contoso.com.")]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsTrueForParentChildDomainPairWithoutLexicalSignals(
        string input) {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            input,
            tools);

        Assert.True(shouldForce);
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsFalseForUnrelatedDomainPairWithoutLexicalSignals() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Check domain health for contoso.com and fabrikam.net.",
            tools);

        Assert.False(shouldForce);
    }

    [Fact]
    public void DomainIntentHostGuardrail_BlocksAdScopeHostCallWhenTargetMatchesPublicDomainEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail",
            new[] {
                new ToolCallDto {
                    CallId = "public-1",
                    Name = "domaindetective_network_probe",
                    ArgumentsJson = """{"host":"contoso-com.mail.protection.outlook.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "public-1",
                    Output = """{"ok":true,"host":"contoso-com.mail.protection.outlook.com"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-1",
            name: "eventlog_live_query",
            input: """{"machine_name":"contoso-com.mail.protection.outlook.com"}""",
            arguments: new JsonObject().Add("machine_name", "contoso-com.mail.protection.outlook.com"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail",
            userRequest: "Continue replication checks for AD scope.",
            call: call,
            output: out var output);

        Assert.True(blocked);
        Assert.Equal("domain_scope_host_guardrail", output.ErrorCode);
        Assert.False(output.IsTransient);
    }

    [Fact]
    public void DomainIntentHostGuardrail_BlocksCompactScopeShiftSingleHostReplayWhenThreadHasMultiHostEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail-scope-shift", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail-scope-shift",
            new[] {
                new ToolCallDto {
                    CallId = "ad-evx-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System"}"""
                },
                new ToolCallDto {
                    CallId = "ad-evx-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD2.ad.evotec.xyz","log_name":"System"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "ad-evx-1",
                    Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""",
                    Ok = true
                },
                new ToolOutputDto {
                    CallId = "ad-evx-2",
                    Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-evx-replay",
            name: "eventlog_live_query",
            input: """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System"}""",
            arguments: new JsonObject()
                .Add("machine_name", "AD0.ad.evotec.xyz")
                .Add("log_name", "System"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail-scope-shift",
            userRequest: "i mean other dcs",
            call: call,
            output: out var output);

        Assert.True(blocked);
        Assert.Equal("domain_scope_host_guardrail", output.ErrorCode);
        Assert.Contains("multi-host coverage", output.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DomainIntentHostGuardrail_AllowsCompactScopeShiftSingleHostWhenHostIsPinnedExplicitly() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail-scope-shift-explicit", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail-scope-shift-explicit",
            new[] {
                new ToolCallDto {
                    CallId = "ad-evx-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System"}"""
                },
                new ToolCallDto {
                    CallId = "ad-evx-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD2.ad.evotec.xyz","log_name":"System"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "ad-evx-1",
                    Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""",
                    Ok = true
                },
                new ToolOutputDto {
                    CallId = "ad-evx-2",
                    Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-evx-replay-explicit",
            name: "eventlog_live_query",
            input: """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System"}""",
            arguments: new JsonObject()
                .Add("machine_name", "AD0.ad.evotec.xyz")
                .Add("log_name", "System"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail-scope-shift-explicit",
            userRequest: "i mean other dcs, but AD0.ad.evotec.xyz first",
            call: call,
            output: out _);

        Assert.False(blocked);
    }

    [Fact]
    public void DomainIntentHostGuardrail_DoesNotBlockShortAcknowledgementQuestionSingleHostReplay() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail-scope-shift-short-question", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail-scope-shift-short-question",
            new[] {
                new ToolCallDto {
                    CallId = "ad-evx-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System"}"""
                },
                new ToolCallDto {
                    CallId = "ad-evx-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"machine_name":"AD2.ad.evotec.xyz","log_name":"System"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "ad-evx-1",
                    Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""",
                    Ok = true
                },
                new ToolOutputDto {
                    CallId = "ad-evx-2",
                    Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-evx-replay-short-question",
            name: "eventlog_live_query",
            input: """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System"}""",
            arguments: new JsonObject()
                .Add("machine_name", "AD0.ad.evotec.xyz")
                .Add("log_name", "System"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail-scope-shift-short-question",
            userRequest: "go ahead?",
            call: call,
            output: out _);

        Assert.False(blocked);
    }

    [Fact]
    public void IsDomainIntentHostGuardrailCandidateToolForTesting_UsesMetadataForCustomNames() {
        var adTagged = ChatServiceSession.IsDomainIntentHostGuardrailCandidateToolForTesting(
            "custom_directory_probe",
            new ToolDefinition(
                name: "custom_directory_probe",
                description: "Custom AD probe",
                parameters: null,
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "custom_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }));
        var adHostTagged = ChatServiceSession.IsDomainIntentHostGuardrailCandidateToolForTesting(
            "custom_host_timeline",
            new ToolDefinition(
                name: "custom_host_timeline",
                description: "Custom host timeline",
                parameters: null,
                category: "custom",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "custom_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }));
        var dnsTagged = ChatServiceSession.IsDomainIntentHostGuardrailCandidateToolForTesting(
            "custom_dns_probe",
            new ToolDefinition(
                name: "custom_dns_probe",
                description: "Custom DNS probe",
                parameters: null,
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "dnsclientx",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
                }));
        var eventLogCategory = ChatServiceSession.IsDomainIntentHostGuardrailCandidateToolForTesting(
            "eventlog_live_query",
            new ToolDefinition(
                name: "eventlog_live_query",
                description: "Event log query",
                parameters: null,
                category: "eventlog",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    PackId = "eventlog",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }));

        Assert.True(adTagged);
        Assert.True(adHostTagged);
        Assert.False(dnsTagged);
        Assert.True(eventLogCategory);
    }

    [Fact]
    public void IsDomainIntentHostGuardrailCandidateToolForTesting_DoesNotInferFamilyFromToolNameWithoutRoutingContract() {
        var inferredByName = ChatServiceSession.IsDomainIntentHostGuardrailCandidateToolForTesting(
            "ad_replication_health",
            new ToolDefinition(
                name: "ad_replication_health",
                description: "Legacy-style AD tool name without routing contract"));

        Assert.False(inferredByName);
    }

    [Fact]
    public void DomainIntentHostGuardrail_AllowsExplicitHostWhenUserProvidesTargetInTurnRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail-explicit", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail-explicit",
            new[] {
                new ToolCallDto {
                    CallId = "public-1",
                    Name = "domaindetective_network_probe",
                    ArgumentsJson = """{"host":"contoso-com.mail.protection.outlook.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "public-1",
                    Output = """{"ok":true,"host":"contoso-com.mail.protection.outlook.com"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-1",
            name: "eventlog_live_query",
            input: """{"machine_name":"contoso-com.mail.protection.outlook.com"}""",
            arguments: new JsonObject().Add("machine_name", "contoso-com.mail.protection.outlook.com"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail-explicit",
            userRequest: "Run AD checks on contoso-com.mail.protection.outlook.com in this turn.",
            call: call,
            output: out _);

        Assert.False(blocked);
    }

    [Fact]
    public void DomainIntentHostGuardrail_AllowsAdScopeHostCallWhenTargetDoesNotMatchPublicDomainEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetPreferredDomainIntentFamilyForTesting("thread-guardrail-miss", "ad_domain");
        session.RememberThreadToolEvidenceForTesting(
            "thread-guardrail-miss",
            new[] {
                new ToolCallDto {
                    CallId = "public-1",
                    Name = "domaindetective_network_probe",
                    ArgumentsJson = """{"host":"contoso-com.mail.protection.outlook.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "public-1",
                    Output = """{"ok":true,"host":"contoso-com.mail.protection.outlook.com"}""",
                    Ok = true
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "ad-1",
            name: "ad_replication_summary",
            input: """{"domain_controller":"ad1.corp.contoso.com"}""",
            arguments: new JsonObject().Add("domain_controller", "ad1.corp.contoso.com"),
            raw: new JsonObject().Add("type", "tool_call").Add("name", "ad_replication_summary"));

        var blocked = session.TryBuildDomainIntentHostScopeGuardrailOutputForTesting(
            threadId: "thread-guardrail-miss",
            userRequest: "Continue AD replication checks on discovered DCs.",
            call: call,
            output: out _);

        Assert.False(blocked);
    }
}
