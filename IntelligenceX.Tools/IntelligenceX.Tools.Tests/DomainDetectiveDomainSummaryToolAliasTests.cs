using System;
using System.Reflection;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DomainDetectiveDomainSummaryToolAliasTests {
    private static readonly MethodInfo NormalizeDomainDetectiveCheckNameMethod =
        typeof(DomainDetectiveDomainSummaryTool).GetMethod(
            "NormalizeDomainDetectiveCheckName",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizeDomainDetectiveCheckName method was not found.");

    [Theory]
    [InlineData("NameServers", "NS")]
    [InlineData("name servers", "NS")]
    [InlineData("name_server_records", "NS")]
    [InlineData("mx-records", "MX")]
    [InlineData("dmarc records", "DMARC")]
    [InlineData("dns health", "DNSHEALTH")]
    [InlineData(" ttl ", "TTL")]
    public void NormalizeDomainDetectiveCheckName_MapsAliasesAndTokenShapes(string input, string expected) {
        var actual = NormalizeCheckName(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeDomainDetectiveCheckName_ReturnsEmptyForBlankInput() {
        var actual = NormalizeCheckName("   ");

        Assert.Equal(string.Empty, actual);
    }

    private static string NormalizeCheckName(string value) {
        var result = NormalizeDomainDetectiveCheckNameMethod.Invoke(null, new object?[] { value });
        return Assert.IsType<string>(result);
    }
}

