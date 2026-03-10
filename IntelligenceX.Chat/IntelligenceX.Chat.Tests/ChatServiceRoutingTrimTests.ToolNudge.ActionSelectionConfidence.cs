using System.Text.Json;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void TryFindNormalizedConfidenceValue_SkipsNextActionsWhenFallingBackToEnvelopeMetadata() {
        using var document = JsonDocument.Parse("""
            {
              "ok": true,
              "next_actions": [
                { "tool": "ad_ldap_diagnostics", "confidence": 0.91 }
              ]
            }
            """);

        var found = ChatServiceSession.TryFindNormalizedConfidenceValueForTesting(document.RootElement, maxDepth: 3, out var confidence);

        Assert.False(found);
        Assert.Equal(0d, confidence);
    }

    [Fact]
    public void TryFindNormalizedConfidenceValue_UsesEnvelopeConfidenceOutsideNextActions() {
        using var document = JsonDocument.Parse("""
            {
              "ok": true,
              "meta": {
                "chain_confidence": 0.42
              },
              "next_actions": [
                { "tool": "ad_ldap_diagnostics", "confidence": 0.91 }
              ]
            }
            """);

        var found = ChatServiceSession.TryFindNormalizedConfidenceValueForTesting(document.RootElement, maxDepth: 3, out var confidence);

        Assert.True(found);
        Assert.Equal(0.42d, confidence, 3);
    }

    [Fact]
    public void TryReadNormalizedConfidenceValue_ParsesDecimalCommaStrings() {
        using var document = JsonDocument.Parse("""
            {
              "confidence": "0,88"
            }
            """);

        var found = ChatServiceSession.TryReadNormalizedConfidenceValueForTesting(document.RootElement, "confidence", out var confidence);

        Assert.True(found);
        Assert.Equal(0.88d, confidence, 3);
    }
}
