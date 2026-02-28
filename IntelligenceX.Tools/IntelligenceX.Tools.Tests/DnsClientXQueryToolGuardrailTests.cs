using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.DnsClientX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DnsClientXQueryToolGuardrailTests {
    private static readonly MethodInfo IsSuspiciousEmptyNoErrorResponseMethod =
        typeof(DnsClientXQueryTool).GetMethod(
            "IsSuspiciousEmptyNoErrorResponse",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsSuspiciousEmptyNoErrorResponse method was not found.");
    private static readonly MethodInfo BuildRenderHintsMethod =
        typeof(DnsClientXQueryTool).GetMethod(
            "BuildRenderHints",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRenderHints method was not found.");

    [Fact]
    public void IsSuspiciousEmptyNoErrorResponse_ReturnsTrueWhenAllSectionsAreEmptyAndNotTruncated() {
        var suspicious = InvokeIsSuspiciousEmptyNoErrorResponse(
            questionCount: 0,
            answerCount: 0,
            authorityCount: 0,
            additionalCount: 0,
            isTruncated: false);

        Assert.True(suspicious);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(0, 1, 0, 0, false)]
    [InlineData(0, 0, 1, 0, false)]
    [InlineData(0, 0, 0, 1, false)]
    [InlineData(0, 0, 0, 0, true)]
    public void IsSuspiciousEmptyNoErrorResponse_ReturnsFalseWhenAnySignalOfARealResponseExists(
        int questionCount,
        int answerCount,
        int authorityCount,
        int additionalCount,
        bool isTruncated) {
        var suspicious = InvokeIsSuspiciousEmptyNoErrorResponse(
            questionCount,
            answerCount,
            authorityCount,
            additionalCount,
            isTruncated);

        Assert.False(suspicious);
    }

    [Fact]
    public void BuildRenderHints_ReturnsNullWhenAllSectionsAreEmpty() {
        var hints = InvokeBuildRenderHints(
            answerCount: 0,
            authorityCount: 0,
            additionalCount: 0,
            questionCount: 0);

        Assert.Null(hints);
    }

    [Fact]
    public void BuildRenderHints_EmitsPriorityOrderedSectionTables() {
        var hints = InvokeBuildRenderHints(
            answerCount: 2,
            authorityCount: 1,
            additionalCount: 1,
            questionCount: 1);
        Assert.NotNull(hints);

        using var document = JsonDocument.Parse(JsonLite.Serialize(hints!));
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(4, renderHints.Length);

        Assert.Equal("answers", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(400, renderHints[0].GetProperty("priority").GetInt32());

        Assert.Equal("authorities", renderHints[1].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[1].GetProperty("priority").GetInt32());

        Assert.Equal("additional", renderHints[2].GetProperty("rows_path").GetString());
        Assert.Equal(200, renderHints[2].GetProperty("priority").GetInt32());

        Assert.Equal("questions", renderHints[3].GetProperty("rows_path").GetString());
        Assert.Equal(100, renderHints[3].GetProperty("priority").GetInt32());
    }

    private static bool InvokeIsSuspiciousEmptyNoErrorResponse(
        int questionCount,
        int answerCount,
        int authorityCount,
        int additionalCount,
        bool isTruncated) {
        var value = IsSuspiciousEmptyNoErrorResponseMethod.Invoke(
            null,
            new object?[] { questionCount, answerCount, authorityCount, additionalCount, isTruncated });
        return Assert.IsType<bool>(value);
    }

    private static JsonValue? InvokeBuildRenderHints(
        int answerCount,
        int authorityCount,
        int additionalCount,
        int questionCount) {
        var value = BuildRenderHintsMethod.Invoke(
            null,
            new object?[] { answerCount, authorityCount, additionalCount, questionCount });
        return value as JsonValue;
    }
}
