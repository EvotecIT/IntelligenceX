using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ActiveDirectoryToolBaseErrorMappingTests {
    [Fact]
    public void ErrorFromException_ShouldMapInvalidOperationToInvalidArgument() {
        var tool = new HarnessTool();

        var json = tool.Map(new InvalidOperationException("Bad input\r\nline2"), "Fallback.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal("Bad input line2", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldMapTimeoutToTransientTimeoutError() {
        var tool = new HarnessTool();

        var json = tool.Map(new TimeoutException("Timed out talking to DC"), "Fallback.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("timeout", root.GetProperty("error_code").GetString());
        Assert.True(root.GetProperty("is_transient").GetBoolean());
    }

    [Fact]
    public void ErrorFromException_ShouldAllowInvalidOperationOverride() {
        var tool = new HarnessTool();

        var json = tool.Map(new InvalidOperationException("Service unavailable"), "Fallback.", invalidOperationErrorCode: "query_failed");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Service unavailable", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldHideUnhandledExceptionDetails() {
        var tool = new HarnessTool();

        var json = tool.Map(new Exception("secret-token=abc123"), "GPO list query failed.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("GPO list query failed.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void IsConfiguredAttributionValue_ShouldTreatOffAsUnconfiguredWhenConfiguredAliasProvided() {
        var tool = new HarnessTool();

        Assert.False(tool.IsConfigured("Off", new[] { "off" }));
        Assert.False(tool.IsConfigured("  OFF  ", new[] { "Off" }));
        Assert.True(tool.IsConfigured("Off", additionalUnconfiguredValues: null));
        Assert.True(tool.IsConfigured("Enabled", new[] { "Off" }));
    }

    [Fact]
    public void ToCollectorErrorMessage_ShouldSanitizeAndFallback() {
        var tool = new HarnessTool();

        var sanitized = tool.CollectorError(new Exception("server:\r\nline2\tfailure"), "Domain query failed.");
        Assert.Equal("server: line2 failure", sanitized);

        var fallback = tool.CollectorError(new Exception("   "), "Domain query failed.");
        Assert.Equal("Domain query failed.", fallback);
    }

    [Fact]
    public void TryReadPolicyAttributionToolRequest_ShouldRequireDomainName() {
        var tool = new HarnessTool();

        var ok = tool.TryReadPolicyRequest(
            arguments: new JsonObject(),
            out _,
            out _,
            out _,
            out _,
            out var errorResponse);

        Assert.False(ok);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task ExecutePolicyAttributionTool_ShouldFilterRowsAndAddStandardMeta() {
        var tool = new HarnessTool();
        var arguments = new JsonObject()
            .Add("domain_name", "contoso.local")
            .Add("configured_attribution_only", true)
            .Add("max_results", 10);

        var json = await tool.ExecutePolicyToolAsync(
            arguments,
            _ => new HarnessTool.MockPolicyView(new[] {
                new PolicyAttribution("s1", "k", "v", "Off", null, Array.Empty<GpoRef>()),
                new PolicyAttribution("s2", "k", "v", "Enabled", null, Array.Empty<GpoRef>()),
                new PolicyAttribution("s3", "k", "v", "Not configured", null, Array.Empty<GpoRef>())
            }, "ok"),
            additionalUnconfiguredValues: new[] { "Off" });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("attribution_view").GetArrayLength());
        Assert.Equal(1, root.GetProperty("scanned").GetInt32());
        Assert.Equal("contoso.local", root.GetProperty("meta").GetProperty("domain_name").GetString());
        Assert.True(root.GetProperty("meta").GetProperty("configured_attribution_only").GetBoolean());
        Assert.Equal(1, root.GetProperty("meta").GetProperty("scanned").GetInt32());
    }

    [Fact]
    public async Task ExecutePolicyAttributionTool_ShouldMapInvalidOperationToQueryFailed() {
        var tool = new HarnessTool();
        var arguments = new JsonObject().Add("domain_name", "contoso.local");

        var json = await tool.ExecutePolicyToolAsync(
            arguments,
            _ => throw new InvalidOperationException("Policy service unavailable"));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Policy service unavailable", root.GetProperty("error").GetString());
    }

    private sealed class HarnessTool : ActiveDirectoryToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "ad_test_harness",
            "Test harness tool.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool() : base(new ActiveDirectoryToolOptions()) { }

        public override ToolDefinition Definition => DefinitionValue;

        public string Map(
            Exception ex,
            string defaultMessage,
            string fallbackErrorCode = "query_failed",
            string invalidOperationErrorCode = "invalid_argument") {
            return ErrorFromException(
                ex,
                defaultMessage,
                fallbackErrorCode,
                invalidOperationErrorCode);
        }

        public bool IsConfigured(string? effectiveValue, IReadOnlyList<string>? additionalUnconfiguredValues = null) {
            return IsConfiguredAttributionValue(effectiveValue, additionalUnconfiguredValues);
        }

        public string CollectorError(Exception? exception, string fallback) {
            return ToCollectorErrorMessage(exception, fallback);
        }

        public bool TryReadPolicyRequest(
            JsonObject? arguments,
            out string? domainName,
            out bool includeAttribution,
            out bool configuredAttributionOnly,
            out int maxResults,
            out string? errorResponse) {
            var ok = TryReadPolicyAttributionToolRequest(arguments, out var request, out errorResponse);
            domainName = ok ? request.DomainName : null;
            includeAttribution = ok && request.IncludeAttribution;
            configuredAttributionOnly = ok && request.ConfiguredAttributionOnly;
            maxResults = ok ? request.MaxResults : 0;
            return ok;
        }

        public Task<string> ExecutePolicyToolAsync(
            JsonObject? arguments,
            Func<string, MockPolicyView> query,
            IReadOnlyList<string>? additionalUnconfiguredValues = null) {
            return ExecutePolicyAttributionTool<MockPolicyView, MockPolicyResult>(
                arguments: arguments,
                cancellationToken: CancellationToken.None,
                title: "Active Directory: Test Policy (preview)",
                defaultErrorMessage: "Policy query failed.",
                query: query,
                attributionSelector: static view => view.Attribution,
                additionalUnconfiguredValues: additionalUnconfiguredValues,
                resultFactory: static (request, view, scanned, truncated, rows) => new MockPolicyResult(
                    request.DomainName,
                    request.IncludeAttribution,
                    request.ConfiguredAttributionOnly,
                    scanned,
                    truncated,
                    view.Marker,
                    rows));
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }

        internal sealed record MockPolicyView(IReadOnlyList<PolicyAttribution> Attribution, string Marker);

        private sealed record MockPolicyResult(
            string DomainName,
            bool IncludeAttribution,
            bool ConfiguredAttributionOnly,
            int Scanned,
            bool Truncated,
            string Marker,
            IReadOnlyList<PolicyAttribution> Attribution);
    }
}
