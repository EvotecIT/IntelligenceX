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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
}
