using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers host-side visual popout request validation and callback contract inputs.
/// </summary>
public sealed class MainWindowVisualPopoutContractTests {
    /// <summary>
    /// Ensures only PNG/SVG popout mime types are accepted and normalized.
    /// </summary>
    [Theory]
    [InlineData("image/png", true, "image/png", "png")]
    [InlineData("IMAGE/SVG+XML ", true, "image/svg+xml", "svg")]
    [InlineData("text/plain", false, "text/plain", "")]
    [InlineData("", false, "", "")]
    public void TryNormalizeVisualPopoutMimeType_ValidatesSupportedMimeTypes(
        string mimeType,
        bool expectedOk,
        string expectedNormalizedMimeType,
        string expectedFormat) {
        var ok = MainWindow.TryNormalizeVisualPopoutMimeType(
            mimeType,
            out var normalizedMimeType,
            out var normalizedFormat);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedNormalizedMimeType, normalizedMimeType);
        Assert.Equal(expectedFormat, normalizedFormat);
    }

    /// <summary>
    /// Ensures popout titles are trimmed and capped to the configured max length.
    /// </summary>
    [Fact]
    public void NormalizeVisualPopoutTitle_TrimsAndCapsLength() {
        var title = "  " + new string('x', 300) + "  ";

        var normalized = MainWindow.NormalizeVisualPopoutTitle(title);

        Assert.Equal(160, normalized.Length);
        Assert.DoesNotContain(' ', normalized);
    }

    /// <summary>
    /// Ensures invalid base64 payloads are rejected with a decode error.
    /// </summary>
    [Fact]
    public void TryDecodeVisualPopoutPayload_ReturnsFalseForInvalidBase64() {
        var ok = MainWindow.TryDecodeVisualPopoutPayload("not-base64", out var payloadBytes, out var errorMessage);

        Assert.False(ok);
        Assert.Empty(payloadBytes);
        Assert.StartsWith("Invalid popout payload:", errorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures valid base64 payloads decode into non-empty bytes.
    /// </summary>
    [Fact]
    public void TryDecodeVisualPopoutPayload_ReturnsBytesForValidBase64() {
        var ok = MainWindow.TryDecodeVisualPopoutPayload("AQID", out var payloadBytes, out var errorMessage);

        Assert.True(ok);
        Assert.Equal(new byte[] { 1, 2, 3 }, payloadBytes);
        Assert.Equal(string.Empty, errorMessage);
    }

    /// <summary>
    /// Ensures unsupported mime types fail request preparation before decode work.
    /// </summary>
    [Fact]
    public void TryPrepareVisualPopoutRequest_ReturnsFalseForUnsupportedMime() {
        var ok = MainWindow.TryPrepareVisualPopoutRequest(
            "chart",
            "text/plain",
            "AQID",
            out _,
            out _,
            out var payloadBytes,
            out var errorMessage);

        Assert.False(ok);
        Assert.Empty(payloadBytes);
        Assert.Equal("Unsupported popout mime type.", errorMessage);
    }

    /// <summary>
    /// Ensures oversized payloads fail request preparation on char-length guard.
    /// </summary>
    [Fact]
    public void TryPrepareVisualPopoutRequest_ReturnsFalseForOversizedPayloadBeforeDecode() {
        var maxBase64Chars = ((12 * 1024 * 1024 + 2) / 3) * 4;
        var oversizedPayload = new string('A', maxBase64Chars + 1);

        var ok = MainWindow.TryPrepareVisualPopoutRequest(
            "chart",
            "image/png",
            oversizedPayload,
            out _,
            out _,
            out var payloadBytes,
            out var errorMessage);

        Assert.False(ok);
        Assert.Empty(payloadBytes);
        Assert.Equal("Popout payload exceeds maximum allowed size.", errorMessage);
    }

    /// <summary>
    /// Ensures malformed payloads fail request preparation with decode details.
    /// </summary>
    [Fact]
    public void TryPrepareVisualPopoutRequest_ReturnsFalseForInvalidPayload() {
        var ok = MainWindow.TryPrepareVisualPopoutRequest(
            "chart",
            "image/png",
            "not-base64",
            out _,
            out _,
            out var payloadBytes,
            out var errorMessage);

        Assert.False(ok);
        Assert.Empty(payloadBytes);
        Assert.StartsWith("Invalid popout payload:", errorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures valid request inputs are normalized and returned for popout dispatch.
    /// </summary>
    [Fact]
    public void TryPrepareVisualPopoutRequest_ReturnsPreparedInputsForValidRequest() {
        var ok = MainWindow.TryPrepareVisualPopoutRequest(
            "  Visual Title  ",
            "image/png",
            "AQID",
            out var normalizedTitle,
            out var normalizedFormat,
            out var payloadBytes,
            out var errorMessage);

        Assert.True(ok);
        Assert.Equal("Visual Title", normalizedTitle);
        Assert.Equal("png", normalizedFormat);
        Assert.Equal(new byte[] { 1, 2, 3 }, payloadBytes);
        Assert.Equal(string.Empty, errorMessage);
    }
}
