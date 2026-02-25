using System;
using System.Reflection;
using IntelligenceX.Tools.DnsClientX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DnsClientXQueryToolGuardrailTests {
    private static readonly MethodInfo IsSuspiciousEmptyNoErrorResponseMethod =
        typeof(DnsClientXQueryTool).GetMethod(
            "IsSuspiciousEmptyNoErrorResponse",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsSuspiciousEmptyNoErrorResponse method was not found.");

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
}

