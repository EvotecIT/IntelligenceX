using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
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
            "please rerun eventlog_evtx_query for this host",
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
}
