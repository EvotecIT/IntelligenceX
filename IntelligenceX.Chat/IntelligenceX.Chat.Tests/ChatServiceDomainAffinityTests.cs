using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
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
    [InlineData("②", "public_domain")]
    [InlineData("❷", "public_domain")]
    [InlineData("١", "ad_domain")]
    [InlineData("١：", "ad_domain")]
    [InlineData("①", "ad_domain")]
    [InlineData("❶", "ad_domain")]
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
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesDomainIntentFamilyMarkerPayload() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesActionSelectionPayloadWithObjectRequest() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesExplicitActSelectionCommand() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
    [InlineData("ad", "ad_domain")]
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
    [InlineData("Run domaindetective checks for this zone", "public_domain")]
    [InlineData("dnsclientx resolver baseline", "public_domain")]
    [InlineData("act_domain_scope_public", "public_domain")]
    [InlineData("ad_domain", "ad_domain")]
    public void TryResolvePendingDomainIntentClarificationSelection_ParsesLanguageNeutralTechnicalSignals(
        string input,
        string expectedFamily) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-conflict");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-conflict",
            "AD and DNS",
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolvePendingDomainIntentClarificationSelection_DoesNotTreatLowercaseAdAsStandaloneAdSignal() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberPendingDomainIntentClarificationRequestForTesting("thread-clarify-lowercase-ad");

        var resolved = session.TryResolvePendingDomainIntentClarificationSelectionForTesting(
            "thread-clarify-lowercase-ad",
            "ad and dns",
            out var family);

        Assert.True(resolved);
        Assert.Equal("public_domain", family);
    }

    [Fact]
    public void TryApplyDomainIntentSignalRoutingHint_FiltersMixedToolsAndRemembersAdPreference() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("ad_domain_controllers", "AD DCs"),
            new ToolDefinition("dnsclientx_query", "DNS query"),
            new ToolDefinition("domaindetective_domain_summary", "Domain summary")
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query"),
            new ToolDefinition("domaindetective_domain_summary", "Domain summary")
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
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

    [Theory]
    [InlineData("AD and DNS")]
    [InlineData("Need LDAP + MX checks")]
    [InlineData("kerberos DNS MX")]
    [InlineData("act_domain_scope_ad with dns checks")]
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
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Please do AD LDAP + DNS MX together now.",
            tools);

        Assert.True(shouldForce);
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsFalseWhenExplicitFamilyMarkerIsPresent() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
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
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("ad_domain_controllers", "AD DCs")
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Please do AD LDAP + DNS MX together now.",
            tools);

        Assert.False(shouldForce);
    }

    [Theory]
    [InlineData("Check domain health for corp.contoso.com and contoso.com.")]
    [InlineData("Necesito revisar corp.contoso.com y contoso.com.")]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsTrueForParentChildDomainPairWithoutLexicalSignals(
        string input) {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            input,
            tools);

        Assert.True(shouldForce);
    }

    [Fact]
    public void ShouldForceDomainIntentClarificationForConflictingSignalsForTesting_ReturnsFalseForUnrelatedDomainPairWithoutLexicalSignals() {
        var tools = new[] {
            new ToolDefinition("ad_scope_discovery", "AD scope"),
            new ToolDefinition("dnsclientx_query", "DNS query")
        };

        var shouldForce = ChatServiceSession.ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
            "Check domain health for contoso.com and fabrikam.net.",
            tools);

        Assert.False(shouldForce);
    }

    [Fact]
    public void DomainIntentHostGuardrail_BlocksAdScopeHostCallWhenTargetMatchesPublicDomainEvidence() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
    public void DomainIntentHostGuardrail_AllowsExplicitHostWhenUserProvidesTargetInTurnRequest() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
