using System;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolResponseShapingTests {
    [Fact]
    public void BuildSessionResponseShapingInstructions_ReturnsNull_WhenNoConstraintsEnabled() {
        var shaping = ToolResponseShaping.BuildSessionResponseShapingInstructions(
            maxTableRows: 0,
            maxSample: 0,
            redact: false);

        Assert.Null(shaping);
    }

    [Fact]
    public void BuildSessionResponseShapingInstructions_IncludesConfiguredConstraints() {
        var shaping = ToolResponseShaping.BuildSessionResponseShapingInstructions(
            maxTableRows: 25,
            maxSample: 10,
            redact: true);

        Assert.NotNull(shaping);
        Assert.Contains("## Session Response Shaping", shaping);
        Assert.Contains("Max table rows: 25", shaping);
        Assert.Contains("Max sample items: 10", shaping);
        Assert.Contains("Redaction: redact emails/UPNs", shaping);
    }

    [Fact]
    public void AppendSessionResponseShapingInstructions_AppendsToExistingInstructions() {
        var appended = ToolResponseShaping.AppendSessionResponseShapingInstructions(
            instructions: "Base instructions",
            maxTableRows: 5,
            maxSample: 0,
            redact: false);

        Assert.NotNull(appended);
        Assert.StartsWith("Base instructions" + Environment.NewLine + Environment.NewLine, appended);
        Assert.Contains("Max table rows: 5", appended);
    }

    [Fact]
    public void RedactEmailLikeTokens_RedactsEmailAndHandlesEmptyInput() {
        var redacted = ToolResponseShaping.RedactEmailLikeTokens("Contact admin@contoso.local now.");
        var empty = ToolResponseShaping.RedactEmailLikeTokens(string.Empty);
        var nullText = ToolResponseShaping.RedactEmailLikeTokens(null);

        Assert.Equal("Contact [redacted_email] now.", redacted);
        Assert.Equal(string.Empty, empty);
        Assert.Equal(string.Empty, nullText);
    }
}
