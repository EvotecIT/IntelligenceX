using System;
using System.Reflection;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DomainDetectiveDomainSummaryToolSummaryMarkdownTests {
    private static readonly Type ResultModelType =
        typeof(DomainDetectiveDomainSummaryTool).GetNestedType(
            "DomainDetectiveDomainSummaryResultModel",
            BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DomainDetectiveDomainSummaryResultModel type was not found.");

    private static readonly Type SummaryModelType =
        typeof(DomainDetectiveDomainSummaryTool).GetNestedType(
            "DomainDetectiveSummaryModel",
            BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DomainDetectiveSummaryModel type was not found.");

    private static readonly Type AnalysisOverviewModelType =
        typeof(DomainDetectiveDomainSummaryTool).GetNestedType(
            "DomainDetectiveAnalysisOverviewModel",
            BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DomainDetectiveAnalysisOverviewModel type was not found.");

    private static readonly MethodInfo BuildSummaryMarkdownMethod =
        typeof(DomainDetectiveDomainSummaryTool).GetMethod(
            "BuildSummaryMarkdown",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSummaryMarkdown method was not found.");

    [Fact]
    public void BuildSummaryMarkdown_UsesNoSelectorEvidenceWhenDkimWasNotObserved() {
        var result = CreateResultModel(
            checksRequested: new[] { "NS", "MX", "SPF", "DMARC", "DKIM" },
            spfValid: true,
            dmarcValid: true,
            hasDkimRecord: false,
            dkimValid: false,
            dnsSecValid: false,
            expiresSoon: false);
        var markdown = InvokeBuildSummaryMarkdown(result);

        Assert.Contains("- DKIM status: `no selector evidence`", markdown, StringComparison.Ordinal);
        Assert.Equal("no selector evidence", ReadSummaryDkimStatus(result));
    }

    [Fact]
    public void BuildSummaryMarkdown_UsesInvalidWhenDkimRecordExistsButDoesNotValidate() {
        var result = CreateResultModel(
            checksRequested: new[] { "DKIM" },
            spfValid: false,
            dmarcValid: false,
            hasDkimRecord: true,
            dkimValid: false,
            dnsSecValid: false,
            expiresSoon: false);
        var markdown = InvokeBuildSummaryMarkdown(result);

        Assert.Contains("- DKIM status: `invalid`", markdown, StringComparison.Ordinal);
        Assert.Equal("invalid", ReadSummaryDkimStatus(result));
    }

    [Fact]
    public void BuildSummaryMarkdown_UsesValidWhenDkimRecordValidates() {
        var result = CreateResultModel(
            checksRequested: new[] { "DKIM" },
            spfValid: false,
            dmarcValid: false,
            hasDkimRecord: true,
            dkimValid: true,
            dnsSecValid: false,
            expiresSoon: false);
        var markdown = InvokeBuildSummaryMarkdown(result);

        Assert.Contains("- DKIM status: `valid`", markdown, StringComparison.Ordinal);
        Assert.Equal("valid", ReadSummaryDkimStatus(result));
    }

    private static string InvokeBuildSummaryMarkdown(object result) {
        var value = BuildSummaryMarkdownMethod.Invoke(null, new object?[] { result });
        return Assert.IsType<string>(value);
    }

    private static object CreateResultModel(
        string[] checksRequested,
        bool spfValid,
        bool dmarcValid,
        bool hasDkimRecord,
        bool dkimValid,
        bool dnsSecValid,
        bool expiresSoon) {
        var summary = Activator.CreateInstance(SummaryModelType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create summary model.");
        SetProperty(summary, "SpfValid", spfValid);
        SetProperty(summary, "DmarcValid", dmarcValid);
        SetProperty(summary, "HasDkimRecord", hasDkimRecord);
        SetProperty(summary, "DkimValid", dkimValid);
        SetProperty(summary, "DkimStatus", ResolveExpectedDkimStatus(hasDkimRecord, dkimValid));
        SetProperty(summary, "DnsSecValid", dnsSecValid);
        SetProperty(summary, "ExpiresSoon", expiresSoon);
        SetProperty(summary, "Hints", Array.Empty<string>());

        var result = Activator.CreateInstance(ResultModelType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create result model.");
        SetProperty(result, "Domain", "contoso.com");
        SetProperty(result, "ChecksRequested", checksRequested);
        SetProperty(result, "Summary", summary);
        SetProperty(result, "AnalysisOverview", Array.CreateInstance(AnalysisOverviewModelType, 0));
        SetProperty(result, "Warnings", Array.Empty<string>());
        return result;
    }

    private static string ReadSummaryDkimStatus(object result) {
        var summary = GetProperty(result, "Summary")
            ?? throw new InvalidOperationException("Summary should not be null.");
        return Assert.IsType<string>(GetProperty(summary, "DkimStatus"));
    }

    private static void SetProperty(object target, string propertyName, object? value) {
        var property = GetPropertyInfo(target, propertyName);
        property.SetValue(target, value);
    }

    private static object? GetProperty(object target, string propertyName) {
        var property = GetPropertyInfo(target, propertyName);
        return property.GetValue(target);
    }

    private static PropertyInfo GetPropertyInfo(object target, string propertyName) {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on {target.GetType().FullName}.");
        return property;
    }

    private static string ResolveExpectedDkimStatus(bool hasDkimRecord, bool dkimValid) {
        if (hasDkimRecord) {
            return dkimValid ? "valid" : "invalid";
        }

        return "no selector evidence";
    }
}
