using System;
using System.Text.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolExceptionMapperTests {
    [Fact]
    public void ErrorFromException_ShouldMapInvalidOperation_WithOverrideCode() {
        var json = ToolExceptionMapper.ErrorFromException(
            new InvalidOperationException("Bad input\r\nline2"),
            defaultMessage: "Fallback.",
            unauthorizedMessage: "Access denied.",
            timeoutMessage: "Timed out.",
            invalidOperationErrorCode: "query_failed");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Bad input line2", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldMapTimeout_AsTransient() {
        var json = ToolExceptionMapper.ErrorFromException(
            new TimeoutException("timed out"),
            defaultMessage: "Fallback.",
            unauthorizedMessage: "Access denied.",
            timeoutMessage: "Timed out.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("timeout", root.GetProperty("error_code").GetString());
        Assert.True(root.GetProperty("is_transient").GetBoolean());
    }

    [Fact]
    public void ErrorFromException_ShouldHideUnhandledExceptionDetails() {
        var json = ToolExceptionMapper.ErrorFromException(
            new Exception("secret-token=abc123"),
            defaultMessage: "Patch details query failed.",
            unauthorizedMessage: "Access denied.",
            timeoutMessage: "Timed out.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Patch details query failed.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void SanitizeErrorMessage_ShouldCompactWhitespace_AndFallbackWhenEmpty() {
        var compact = ToolExceptionMapper.SanitizeErrorMessage("server:\r\nline2\tfailure", "fallback");
        Assert.Equal("server: line2 failure", compact);

        var fallback = ToolExceptionMapper.SanitizeErrorMessage("   ", "fallback");
        Assert.Equal("fallback", fallback);
    }
}
