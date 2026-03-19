using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolEvidenceCacheTests {
    [Fact]
    public void ToolEvidenceCache_BuildsFallbackFromRecentReadOnlyEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "domaindetective_domain_summary",
                ArgumentsJson = "{\"domain\":\"contoso.com\"}"
            },
            new ToolCallDto {
                CallId = "call-2",
                Name = "dnsclientx_query",
                ArgumentsJson = "{\"query\":\"contoso.com\",\"type\":\"MX\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"domain\":\"contoso.com\",\"risk\":\"medium\"}",
                SummaryMarkdown = "Domain posture: medium risk."
            },
            new ToolOutputDto {
                CallId = "call-2",
                Ok = true,
                Output = "{\"answers\":[\"mail.contoso.com\"]}",
                SummaryMarkdown = "MX points to mail.contoso.com."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-evidence",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-evidence",
            "show latest contoso domain checks",
            out var text);

        Assert.True(built);
        Assert.Contains("ix:cached-tool-evidence:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domaindetective_domain_summary", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dnsclientx_query", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#### domaindetective_domain_summary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("- domaindetective_domain_summary:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolEvidenceCache_PreservesMultiLineMarkdownBlocksInFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_environment_discover",
                ArgumentsJson = "{\"forest\":\"ad.evotec.xyz\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"ok\":true}",
                SummaryMarkdown = "### Active Directory: Environment Discovery\n\n```json\n{\"ok\":true}\n```"
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-multiline",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-multiline",
            "show latest ad environment discovery",
            out var text);

        Assert.True(built);
        Assert.Contains("#### ad_environment_discover", text, StringComparison.Ordinal);
        Assert.Contains("### Active Directory: Environment Discovery", text, StringComparison.Ordinal);
        Assert.Contains("```json", text, StringComparison.Ordinal);
        Assert.Contains("{\"ok\":true}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolEvidenceCache_ExplainsBlockedBackgroundPrerequisitesInFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "eventlog_channels_list",
                ArgumentsJson = """{"machine_name":"srv-cached.contoso.com"}"""
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = """{"ok":true,"channels":["System"]}""",
                SummaryMarkdown = "Event log channels are reachable on srv-cached.contoso.com."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-evidence-background-blocked",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "seed_eventlog_live_followup",
                "seed live follow-up",
                ToolSchema.Object().NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "query live event log",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "list event log channels",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        }));
        session.RememberToolHandoffBackgroundWorkForTesting(
            "thread-evidence-background-blocked",
            new[] {
                new ToolDefinition(
                    "seed_eventlog_live_followup",
                    "seed live follow-up",
                    ToolSchema.Object().NoAdditionalProperties(),
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "eventlog",
                                TargetToolName = "eventlog_live_query",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "computer_name",
                                        TargetArgument = "machine_name"
                                    }
                                }
                            }
                        }
                    }),
                new ToolDefinition(
                    "eventlog_live_query",
                    "query live event log",
                    ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                    authentication: new ToolAuthenticationContract {
                        IsAuthenticationAware = true,
                        RequiresAuthentication = true,
                        AuthenticationContractId = "ix.auth.runtime.v1",
                        Mode = ToolAuthenticationMode.ProfileReference,
                        ProfileIdArgumentName = "profile_id",
                        SupportsConnectivityProbe = true,
                        ProbeToolName = "eventlog_channels_list"
                    }),
                new ToolDefinition(
                    "eventlog_channels_list",
                    "list event log channels",
                    ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
            },
            new[] {
                new ToolCallDto {
                    CallId = "call-seed",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-seed",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            "thread-evidence-background-blocked",
            "continue",
            new[] {
                new ToolDefinition(
                    "seed_eventlog_live_followup",
                    "seed live follow-up",
                    ToolSchema.Object().NoAdditionalProperties(),
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "eventlog",
                                TargetToolName = "eventlog_live_query",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "computer_name",
                                        TargetArgument = "machine_name"
                                    }
                                }
                            }
                        }
                    }),
                new ToolDefinition(
                    "eventlog_live_query",
                    "query live event log",
                    ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                    authentication: new ToolAuthenticationContract {
                        IsAuthenticationAware = true,
                        RequiresAuthentication = true,
                        AuthenticationContractId = "ix.auth.runtime.v1",
                        Mode = ToolAuthenticationMode.ProfileReference,
                        ProfileIdArgumentName = "profile_id",
                        SupportsConnectivityProbe = true,
                        ProbeToolName = "eventlog_channels_list"
                    }),
                new ToolDefinition(
                    "eventlog_channels_list",
                    "list event log channels",
                    ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var helperItemId,
            out _,
            out _,
            out _));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            "thread-evidence-background-blocked",
            helperItemId,
            "host_background_work_eventlog_channels_list_010",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_channels_list_010",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Output = """{"ok":false}"""
                }
            });

        var built = session.TryBuildToolEvidenceFallbackTextIgnoringLiveExecutionBypassForTesting(
            "thread-evidence-background-blocked",
            "continue eventlog review",
            out var text);

        Assert.True(built);
        Assert.Contains("Prepared follow-up work is waiting on prerequisite helpers: eventlog_channels_list.", text, StringComparison.Ordinal);
        Assert.Contains("ix:cached-tool-evidence:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotStoreMutatingToolEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_write_user_update",
                ArgumentsJson = "{\"id\":\"u1\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"updated\":true}",
                SummaryMarkdown = "Updated user."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-mutating",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                ["ad_write_user_update"] = true
            });

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-mutating",
            "retry update",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_RehydratesFallbackAfterServiceRestart() {
        var (_, pendingActionsStorePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        try {
            var writerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var calls = new[] {
                new ToolCallDto {
                    CallId = "call-1",
                    Name = "domaindetective_domain_summary",
                    ArgumentsJson = "{\"domain\":\"contoso.com\"}"
                }
            };
            var outputs = new[] {
                new ToolOutputDto {
                    CallId = "call-1",
                    Ok = true,
                    Output = "{\"domain\":\"contoso.com\",\"risk\":\"medium\"}",
                    SummaryMarkdown = "Domain posture: medium risk."
                }
            };

            writerSession.RememberThreadToolEvidenceForTesting(
                threadId: "thread-restart",
                toolCalls: calls,
                toolOutputs: outputs,
                mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            var readerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var built = readerSession.TryBuildToolEvidenceFallbackTextForTesting(
                "thread-restart",
                "repeat latest contoso checks",
                out var text);

            Assert.True(built);
            Assert.Contains("ix:cached-tool-evidence:v1", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("domaindetective_domain_summary", text, StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                if (Directory.Exists(persistenceDirectory)) {
                    Directory.Delete(persistenceDirectory, recursive: true);
                }
            } catch {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public void ToolEvidenceCache_RehydratesExecutionBackendAfterServiceRestart() {
        var (_, pendingActionsStorePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        try {
            var writerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var calls = new[] {
                new ToolCallDto {
                    CallId = "call-1",
                    Name = "system_service_list",
                    ArgumentsJson = "{\"engine\":\"cim\"}"
                }
            };
            var outputs = new[] {
                new ToolOutputDto {
                    CallId = "call-1",
                    Ok = true,
                    Output = "{\"services\":[{\"name\":\"wuauserv\"}]}",
                    SummaryMarkdown = "Listed Windows services.",
                    MetaJson = "{\"engine_preference\":\"cim\"}"
                }
            };

            writerSession.RememberThreadToolEvidenceForTesting(
                threadId: "thread-restart-backend",
                toolCalls: calls,
                toolOutputs: outputs,
                mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            var readerSession = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var built = readerSession.TryBuildToolEvidenceFallbackTextForTesting(
                "thread-restart-backend",
                "repeat latest services check",
                out var text);

            Assert.True(built);
            Assert.Contains("#### system_service_list (backend: cim)", text, StringComparison.Ordinal);
        } finally {
            try {
                if (Directory.Exists(persistenceDirectory)) {
                    Directory.Delete(persistenceDirectory, recursive: true);
                }
            } catch {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseUnrelatedEvidenceWhenRequestHasNoTokenMatches() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "dnsclientx_query",
                ArgumentsJson = "{\"name\":\"ad.evotec.xyz\",\"type\":\"A\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"answers\":[\"192.168.0.10\"]}",
                SummaryMarkdown = "Resolved ad.evotec.xyz."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-unrelated",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-unrelated",
            "run eventlog_evtx_query on ad0",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_PrefersTokenMatchedEvidenceOverRecentUnmatchedEntries() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "dnsclientx_query",
                ArgumentsJson = "{\"name\":\"evotec.xyz\",\"type\":\"A\"}"
            },
            new ToolCallDto {
                CallId = "call-2",
                Name = "eventlog_evtx_query",
                ArgumentsJson = "{\"evtx_path\":\"C:\\\\logs\\\\sys.evtx\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"answers\":[\"1.1.1.1\"]}",
                SummaryMarkdown = "DNS answer for evotec.xyz."
            },
            new ToolOutputDto {
                CallId = "call-2",
                Ok = true,
                Output = "{\"events\":42}",
                SummaryMarkdown = "Parsed EVTX events for restart timeline."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-token-match",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-token-match",
            "show latest eventlog_evtx_query for this host",
            out var text);

        Assert.True(built);
        Assert.Contains("eventlog_evtx_query", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dnsclientx_query", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolEvidenceCache_UsesPreferredDomainFamilyWhenRequestIsCompact() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "dnsclientx_query",
                ArgumentsJson = "{\"name\":\"evotec.xyz\",\"type\":\"A\"}"
            },
            new ToolCallDto {
                CallId = "call-2",
                Name = "eventlog_live_query",
                ArgumentsJson = "{\"machine_name\":\"AD0\",\"log_name\":\"System\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"answers\":[\"1.1.1.1\"]}",
                SummaryMarkdown = "DNS evidence."
            },
            new ToolOutputDto {
                CallId = "call-2",
                Ok = true,
                Output = "{\"events\":5}",
                SummaryMarkdown = "System restart-related events."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-family-context",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetPreferredDomainIntentFamilyForTesting("thread-family-context", "ad_domain");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-family-context",
            "ok continue",
            out var text);

        Assert.True(built);
        Assert.Contains("eventlog_live_query", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dnsclientx_query", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReusePreferredFamilyForPassiveCompactAckWithSymbolCue() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "eventlog_live_query",
                ArgumentsJson = "{\"machine_name\":\"AD0\",\"log_name\":\"System\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"events\":5}",
                SummaryMarkdown = "System restart-related events."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-family-passive-ack",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetPreferredDomainIntentFamilyForTesting("thread-family-passive-ack", "ad_domain");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-family-passive-ack",
            "ok to dziala ;)",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseFamilyMatchedEvidenceForExplicitOtherToolReference() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"health\":\"healthy\"}",
                SummaryMarkdown = "Forest replication health is healthy."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-explicit-other-tool",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetPreferredDomainIntentFamilyForTesting("thread-explicit-other-tool", "ad_domain");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-explicit-other-tool",
            "what does eventlog_evtx_query do?",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseFamilyMatchedEvidenceForEscapedExplicitToolReference() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"health\":\"healthy\"}",
                SummaryMarkdown = "Forest replication health is healthy."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-explicit-escaped-tool",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetPreferredDomainIntentFamilyForTesting("thread-explicit-escaped-tool", "ad_domain");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-explicit-escaped-tool",
            "co to `eventlog\\_evtx\\_query · Event Log (EventViewerX)`",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseFamilyMatchedEvidenceWhenSpecificRequestTokensDoNotMatch() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_environment_discover",
                ArgumentsJson = "{\"forest\":\"ad.evotec.xyz\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"ok\":true,\"domain_controllers\":[\"AD0\",\"AD1\",\"AD2\"]}",
                SummaryMarkdown = "Active Directory environment discovery returned AD0, AD1, and AD2."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-family-token-mismatch",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.SetPreferredDomainIntentFamilyForTesting("thread-family-token-mismatch", "ad_domain");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-family-token-mismatch",
            "where is ADRODC in the full replication table?",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseCachedEvidenceWhenContinuationFollowUpIntroducesNewUnresolvedAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"scope\":\"forest\",\"servers\":[\"AD0\",\"AD1\",\"AD2\"]}",
                SummaryMarkdown = "Full forest replication table currently shows AD0, AD1, and AD2 with healthy replication."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-unresolved-ask-shift",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-unresolved-ask-shift",
            intentAnchor: "Go ahead and check full AD replication forest.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: Full forest replication table currently shows AD0, AD1, and AD2." });

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-unresolved-ask-shift",
            "where is ADRODC in the full replication table?",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_ReusesCachedEvidenceWhenContinuationFollowUpStillMatchesRememberedAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"scope\":\"forest\",\"servers\":[\"AD0\",\"AD1\",\"AD2\"]}",
                SummaryMarkdown = "Forest replication status for AD0, AD1, and AD2 is healthy."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-covered-follow-up",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-covered-follow-up",
            intentAnchor: "Check forest replication across AD0, AD1, and AD2.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: Forest replication status for AD0, AD1, and AD2 is healthy." },
            priorAnswerPlanUserGoal: "Continue from the same replication snapshot.",
            priorAnswerPlanAllowCachedEvidenceReuse: true,
            priorAnswerPlanPreferCachedEvidenceReuse: true,
            priorAnswerPlanCachedEvidenceReuseReason: "latest replication snapshot still answers this compact continuation.");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-covered-follow-up",
            "continue forest replication on AD2",
            out var text);

        Assert.True(built);
        Assert.Contains("ad_replication_summary", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseCachedEvidenceForPriorAnswerPlanUnresolvedAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"scope\":\"forest\",\"servers\":[\"AD0\",\"AD1\",\"AD2\"],\"missing\":[\"ADRODC\"]}",
                SummaryMarkdown = "Full forest replication table currently shows AD0, AD1, AD2, and notes that ADRODC is missing from the returned rows."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-prior-unresolved-answer-plan",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-prior-unresolved-answer-plan",
            intentAnchor: "Go ahead and check full AD replication forest.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: ADRODC is missing from the returned rows." },
            priorAnswerPlanUserGoal: "Return the full forest replication table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the full replication table.",
            priorAnswerPlanPrimaryArtifact: "table");

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-prior-unresolved-answer-plan",
            "where is ADRODC in the full replication table?",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_SelectCachedEvidenceAskCoverageTokens_PreservesShortNonLatinTokens() {
        var selected = ChatServiceSession.SelectCachedEvidenceAskCoverageTokensForTesting("лес", "表", "ok");

        Assert.Contains("лес", selected);
        Assert.Contains("表", selected);
        Assert.DoesNotContain("ok", selected);
    }

    [Fact]
    public void ToolEvidenceCache_ExtractExplicitRequestedToolNames_NormalizesEscapedAndHyphenatedForms() {
        var extracted = ChatServiceSession.ExtractExplicitRequestedToolNamesForTesting(
            "sprawdz `eventlog\\_evtx\\_query` and dnsclientx-query");

        Assert.Contains("eventlogevtxquery", extracted);
        Assert.Contains("dnsclientxquery", extracted);
    }

    [Fact]
    public void ToolEvidenceCache_ExtractExplicitRequestedToolNames_HandlesLargeInputWithoutThrowing() {
        var input = new string('a', 4096) + "_" + new string('b', 4096);
        var extracted = ChatServiceSession.ExtractExplicitRequestedToolNamesForTesting(input);
        Assert.NotNull(extracted);
    }

    [Fact]
    public void ToolEvidenceCache_ExtractExplicitRequestedToolNames_StripsFormatCharactersFromToolId() {
        var extracted = ChatServiceSession.ExtractExplicitRequestedToolNamesForTesting(
            "co to `eventlog_\u200bevtx_query · Event Log (EventViewerX)`");

        Assert.Contains("eventlogevtxquery", extracted);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseCachedEvidence_ForCompactRecheckQuestion() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "ad_replication_summary",
                ArgumentsJson = "{\"scope\":\"forest\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"health\":\"healthy\"}",
                SummaryMarkdown = "Forest replication health is healthy."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-live-recheck",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-live-recheck",
            "can't you recheck?",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_DoesNotReuseCachedEvidence_ForStructuredLiveExecutionFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "eventlog_evtx_query",
                ArgumentsJson = "{\"computer\":\"srv-01\",\"log_name\":\"System\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"event_count\":3}",
                SummaryMarkdown = "Recent system events found."
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-rerun",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-rerun",
            intentAnchor: "Continue the same event log diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "eventlog_evtx_query" },
            recentEvidenceSnippets: new[] { "eventlog_evtx_query: Recent system events found." },
            priorAnswerPlanUserGoal: "Refresh the same host event log query.",
            priorAnswerPlanRequiresLiveExecution: true,
            priorAnswerPlanMissingLiveEvidence: "fresh event log output for this host",
            priorAnswerPlanPreferredToolNames: new[] { "eventlog_evtx_query" });

        var built = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-rerun",
            "eventlog_evtx_query for this host",
            out _);

        Assert.False(built);
    }

    [Fact]
    public void ToolEvidenceCache_PrefersResolvedContinuationReuseFromCurrentAnswerPlanEvenBeforeCheckpointRefresh() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var calls = new[] {
            new ToolCallDto {
                CallId = "call-1",
                Name = "mock_round_tool",
                ArgumentsJson = "{\"step\":\"forest_cache_safe\"}"
            }
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call-1",
                Ok = true,
                Output = "{\"ok\":true,\"summary_markdown\":\"Full forest replication table shows AD0, AD1, and AD2 with healthy replication.\"}"
            }
        };

        session.RememberThreadToolEvidenceForTesting(
            threadId: "thread-resolved-current-plan",
            toolCalls: calls,
            toolOutputs: outputs,
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-resolved-current-plan",
            intentAnchor: "go ahead and check full ad replication forest",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "mock_round_tool" },
            recentEvidenceSnippets: new[] { "mock_round_tool: Full forest replication table shows AD0, AD1, and AD2 with healthy replication." },
            priorAnswerPlanUserGoal: "summarize the forest replication state in a table",
            priorAnswerPlanUnresolvedNow: string.Empty,
            priorAnswerPlanAllowCachedEvidenceReuse: false,
            priorAnswerPlanPreferCachedEvidenceReuse: false,
            priorAnswerPlanPrimaryArtifact: "table");

        var currentAnswerPlan = ChatServiceSession.ResolveReviewedAssistantDraft("""
            [Answer progression plan]
            ix:answer-plan:v1
            user_goal: continue from the same forest replication evidence
            resolved_so_far: the forest replication table is already available above
            unresolved_now: none
            carry_forward_unresolved_focus: false
            carry_forward_reason: this continuation reuses the already-resolved evidence snapshot
            prefer_cached_evidence_reuse: true
            cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot
            primary_artifact: prose
            requested_artifact_already_visible_above: true
            requested_artifact_visibility_reason: the forest replication table is already visible above
            advances_current_ask: true
            advance_reason: confirms that the next step should reuse the same forest replication evidence without a rerun

            Reusing the latest forest replication evidence for AD0, AD1, and AD2.
            """).AnswerPlan;

        Assert.True(currentAnswerPlan.HasPlan);
        Assert.True(currentAnswerPlan.PreferCachedEvidenceReuse);
        Assert.True(currentAnswerPlan.AllowCachedEvidenceReuse);

        var builtWithoutCurrentPlanOverride = session.TryBuildToolEvidenceFallbackTextForTesting(
            "thread-resolved-current-plan",
            "continue replication AD2",
            out _);

        Assert.False(builtWithoutCurrentPlanOverride);

        var builtIgnoringLiveExecutionBypass = session.TryBuildToolEvidenceFallbackTextIgnoringLiveExecutionBypassForTesting(
            "thread-resolved-current-plan",
            "continue replication AD2",
            out var directText);

        Assert.True(builtIgnoringLiveExecutionBypass);
        Assert.Contains("mock_round_tool", directText, StringComparison.OrdinalIgnoreCase);

        var built = session.TryPreferCachedEvidenceForResolvedCompactContinuationForTesting(
            "thread-resolved-current-plan",
            "continue replication AD2",
            currentAnswerPlan,
            toolActivityDetected: false,
            out var text);

        Assert.True(built);
        Assert.Contains("[Cached evidence fallback]", text, StringComparison.Ordinal);
        Assert.Contains("mock_round_tool", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD2", text, StringComparison.OrdinalIgnoreCase);
    }
}
