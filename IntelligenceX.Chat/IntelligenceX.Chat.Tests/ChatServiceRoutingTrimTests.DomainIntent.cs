using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo ShouldRequestDomainIntentClarificationMethod =
        typeof(ChatServiceSession).GetMethod("ShouldRequestDomainIntentClarification", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldRequestDomainIntentClarification not found.");

    private static readonly MethodInfo BuildDomainIntentClarificationTextMethod =
        typeof(ChatServiceSession).GetMethod(
            "BuildDomainIntentClarificationText",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
        ?? throw new InvalidOperationException("BuildDomainIntentClarificationText not found.");

    private static readonly MethodInfo BuildDomainIntentClarificationVisibleTextMethod =
        typeof(ChatServiceSession).GetMethod(
            "BuildDomainIntentClarificationVisibleText",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
        ?? throw new InvalidOperationException("BuildDomainIntentClarificationVisibleText not found.");

    [Fact]
    public void ShouldRequestDomainIntentClarification_TrueForMixedAdAndPublicDomainSubset() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("ad_domain_controllers", description: "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" }),
            new("eventlog_live_query", description: "Event log", tags: new[] { "domain_family:ad_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                24,
                selectedTools,
                selectedTools
            });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_TrueForMixedAliasPrefixedAdAndPublicDomainSubset() {
        var selectedTools = new List<ToolDefinition> {
            new("active_directory_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("adplayground_domain_controllers", description: "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new("dns_client_x_query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domain_detective_domain_summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" }),
            new("eventlog_live_query", description: "Event log", tags: new[] { "domain_family:ad_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                24,
                selectedTools,
                selectedTools
            });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_TrueForMixedHyphenatedAliasPrefixedAdAndPublicDomainSubset() {
        var selectedTools = new List<ToolDefinition> {
            new("active-directory-scope-discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("adplayground-domain-controllers", description: "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new("dns-client-x-query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domain-detective-domain-summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" }),
            new("eventlog-live-query", description: "Event log", tags: new[] { "domain_family:ad_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                24,
                selectedTools,
                selectedTools
            });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenOneFamilyDominates() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("ad_domain_controllers", description: "AD DCs", tags: new[] { "domain_family:ad_domain" }),
            new("ad_forest_discover", description: "AD forest", tags: new[] { "domain_family:ad_domain" }),
            new("ad_ldap_query", description: "AD LDAP", tags: new[] { "domain_family:ad_domain" }),
            new("ad_directory_discovery_diagnostics", description: "AD diagnostics", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", description: "DNS query", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                30,
                selectedTools,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenExecutionContractApplies() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                true,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                20,
                selectedTools,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenFullToolsetIsSelected() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", description: "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", description: "Domain summary", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                selectedTools.Count,
                selectedTools.Count,
                selectedTools,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenSelectedToolsIsNull() {
        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                "check evotec.xyz",
                3,
                20,
                null,
                null
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildDomainIntentClarificationText_UsesLanguageNeutralStructuredScopeContract() {
        var text = BuildDomainIntentClarificationTextMethod.Invoke(null, Array.Empty<object?>());
        var clarification = Assert.IsType<string>(text);

        Assert.Contains("Unicode digits supported", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accepted_input", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selection_map", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("examples", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("２", clarification, StringComparison.Ordinal);
        Assert.Contains("١", clarification, StringComparison.Ordinal);
        Assert.Contains("ad_domain", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public_domain", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:domain-intent:v1", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:domain-intent-choice:v1", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Active Directory domain scope", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Public DNS/domain scope", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Accepted quick replies", clarification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDomainIntentClarificationVisibleText_DoesNotExposeProtocolMarkers() {
        var text = BuildDomainIntentClarificationVisibleTextMethod.Invoke(null, Array.Empty<object?>());
        var clarification = Assert.IsType<string>(text);

        Assert.Contains("1.", clarification, StringComparison.Ordinal);
        Assert.Contains("2.", clarification, StringComparison.Ordinal);
        Assert.Contains("AD", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DNS", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[DomainIntent]", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ix:domain-intent", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Action]", clarification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDomainIntentClarificationVisibleTextForTesting_IncludesAmbiguousTargetAndNaturalReplyExamples() {
        var clarification = ChatServiceSession.BuildDomainIntentClarificationVisibleTextForTesting(
            "Can you check evotec.xyz?",
            hasAdFamily: true,
            hasPublicFamily: true);

        Assert.Contains("evotec.xyz", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"AD\"", clarification, StringComparison.Ordinal);
        Assert.Contains("\"public DNS\"", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/act", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ad_domain", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public_domain", clarification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDomainIntentClarificationTextForTesting_ReturnsEmptyWhenOnlyOneFamilyIsAvailable() {
        var clarification = ChatServiceSession.BuildDomainIntentClarificationTextForTesting(
            hasAdFamily: true,
            hasPublicFamily: false);

        Assert.Equal(string.Empty, clarification);
    }

    [Fact]
    public void BuildDomainIntentClarificationVisibleTextForTesting_ReturnsEmptyWhenOnlyOneFamilyIsAvailable() {
        var clarification = ChatServiceSession.BuildDomainIntentClarificationVisibleTextForTesting(
            hasAdFamily: false,
            hasPublicFamily: true);

        Assert.Equal(string.Empty, clarification);
    }

    [Fact]
    public void BuildDomainIntentClarificationTextForTesting_ReturnsEmptyWhenFamilyActionMappingIsIncomplete() {
        var familyActionIds = new Dictionary<string, string>(StringComparer.Ordinal) {
            [ToolSelectionMetadata.DomainIntentFamilyAd] = "act_domain_scope_ad_custom"
        };
        var clarification = ChatServiceSession.BuildDomainIntentClarificationTextForTesting(
            families: new[] { ToolSelectionMetadata.DomainIntentFamilyAd, ToolSelectionMetadata.DomainIntentFamilyPublic },
            familyActionIds: familyActionIds);

        Assert.Equal(string.Empty, clarification);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesDomainIntentClarificationOrdinalSelection() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var clarificationText = Assert.IsType<string>(BuildDomainIntentClarificationTextMethod.Invoke(null, Array.Empty<object?>()));

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-domain-intent", clarificationText });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-domain-intent", "2" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("act_domain_scope_public", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public_domain", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_action_selection", expanded, StringComparison.OrdinalIgnoreCase);
    }
}
