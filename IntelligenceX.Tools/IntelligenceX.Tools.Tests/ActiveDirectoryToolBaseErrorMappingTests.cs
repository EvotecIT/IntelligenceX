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
        Assert.Equal("domain_name is required.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void TryReadRequiredDomainName_ShouldReturnDomainWhenPresent() {
        var tool = new HarnessTool();

        var ok = tool.TryReadRequiredDomainNameArgument(
            arguments: new JsonObject().Add("domain_name", "contoso.local"),
            out var domainName,
            out var errorResponse);

        Assert.True(ok);
        Assert.Equal("contoso.local", domainName);
        Assert.Null(errorResponse);
    }

    [Fact]
    public void TryReadRequiredDomainName_ShouldReturnInvalidArgumentWhenMissing() {
        var tool = new HarnessTool();

        var ok = tool.TryReadRequiredDomainNameArgument(
            arguments: new JsonObject(),
            out var domainName,
            out var errorResponse);

        Assert.False(ok);
        Assert.Equal(string.Empty, domainName);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal("domain_name is required.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void TryReadRequiredDomainQueryRequest_ShouldReturnDomainAndCappedMaxResults() {
        var tool = new HarnessTool();

        var ok = tool.TryReadRequiredDomainQuery(
            arguments: new JsonObject()
                .Add("domain_name", "contoso.local")
                .Add("max_results", 0),
            useOptionCapDefaultForNonPositive: false,
            out var domainName,
            out var maxResults,
            out var errorResponse);

        Assert.True(ok);
        Assert.Equal("contoso.local", domainName);
        Assert.Equal(1, maxResults);
        Assert.Null(errorResponse);
    }

    [Fact]
    public void TryReadRequiredDomainQueryRequest_ShouldDefaultToOptionCap_WhenConfigured() {
        var tool = new HarnessTool();

        var ok = tool.TryReadRequiredDomainQuery(
            arguments: new JsonObject()
                .Add("domain_name", "contoso.local")
                .Add("max_results", 0),
            useOptionCapDefaultForNonPositive: true,
            out var domainName,
            out var maxResults,
            out var errorResponse);

        Assert.True(ok);
        Assert.Equal("contoso.local", domainName);
        Assert.Equal(1000, maxResults);
        Assert.Null(errorResponse);
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

    [Fact]
    public async Task ExecutePolicyAttributionTool_ShouldAllowAdditionalMetaMutation() {
        var tool = new HarnessTool();
        var arguments = new JsonObject().Add("domain_name", "contoso.local");

        var json = await tool.ExecutePolicyToolAsync(
            arguments,
            _ => new HarnessTool.MockPolicyView(Array.Empty<PolicyAttribution>(), "marker"),
            additionalMetaMutate: static (meta, _, result) => {
                meta.Add("marker", result.Marker);
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("marker", root.GetProperty("meta").GetProperty("marker").GetString());
    }

    [Fact]
    public void ResolveMaxResults_ShouldDefaultOnNonPositiveAndCapHighValues() {
        var tool = new HarnessTool();

        Assert.Equal(1000, tool.ResolveDefaultingMaxResults(new JsonObject().Add("max_results", 0)));
        Assert.Equal(1000, tool.ResolveDefaultingMaxResults(new JsonObject().Add("max_results", -5)));
        Assert.Equal(1000, tool.ResolveDefaultingMaxResults(new JsonObject().Add("max_results", 5000)));
        Assert.Equal(50, tool.ResolveDefaultingMaxResults(new JsonObject().Add("max_results", 50)));
    }

    [Fact]
    public void ResolveMaxResults_ShouldClampToOneAndCapHighValues_ByDefault() {
        var tool = new HarnessTool();

        Assert.Equal(1, tool.ResolveClampedMaxResults(new JsonObject().Add("max_results", 0)));
        Assert.Equal(1, tool.ResolveClampedMaxResults(new JsonObject().Add("max_results", -5)));
        Assert.Equal(1000, tool.ResolveClampedMaxResults(new JsonObject().Add("max_results", 5000)));
        Assert.Equal(50, tool.ResolveClampedMaxResults(new JsonObject().Add("max_results", 50)));
    }

    [Fact]
    public void ResolveDomainAndForestScopeWithMaxResults_ShouldReturnScopeAndClampMax() {
        var tool = new HarnessTool();

        var resolved = tool.ResolveDomainAndForestScopeWithMax(
            new JsonObject()
                .Add("domain_name", "contoso.local")
                .Add("forest_name", "contoso.local")
                .Add("max_results", 0));

        Assert.Equal("contoso.local", resolved.DomainName);
        Assert.Equal("contoso.local", resolved.ForestName);
        Assert.Equal(1, resolved.MaxResults);
    }

    [Fact]
    public void ResolveDomainAndForestScopeWithMaxResults_ShouldUseOptionCapWhenRequested() {
        var tool = new HarnessTool();

        var resolved = tool.ResolveDomainAndForestScopeWithMax(
            new JsonObject().Add("max_results", 0),
            useOptionCapDefaultForNonPositive: true);

        Assert.Null(resolved.DomainName);
        Assert.Null(resolved.ForestName);
        Assert.Equal(1000, resolved.MaxResults);
    }

    [Fact]
    public void AddDomainAndForestMeta_ShouldAddOnlyNonEmptyValues() {
        var tool = new HarnessTool();

        var withBoth = tool.CreateScopeMeta("contoso.local", "corp.contoso.local");
        Assert.Equal("contoso.local", withBoth.GetString("domain_name"));
        Assert.Equal("corp.contoso.local", withBoth.GetString("forest_name"));

        var withDomainOnly = tool.CreateScopeMeta("contoso.local", " ");
        Assert.Equal("contoso.local", withDomainOnly.GetString("domain_name"));
        Assert.Null(withDomainOnly.GetString("forest_name"));

        var withNone = tool.CreateScopeMeta(" ", null);
        Assert.Null(withNone.GetString("domain_name"));
        Assert.Null(withNone.GetString("forest_name"));
    }

    [Fact]
    public void AddDomainAndMaxResultsMeta_ShouldAddBothKeys() {
        var tool = new HarnessTool();

        var meta = tool.CreateDomainAndMaxResultsMeta("contoso.local", 123);

        Assert.Equal("contoso.local", meta.GetString("domain_name"));
        Assert.Equal(123, meta.GetInt64("max_results"));
    }

    [Fact]
    public void AddDomainAndForestAndMaxResultsMeta_ShouldAddAllKeys() {
        var tool = new HarnessTool();

        var withAll = tool.CreateDomainForestAndMaxResultsMeta("contoso.local", "corp.contoso.local", 321);
        Assert.Equal("contoso.local", withAll.GetString("domain_name"));
        Assert.Equal("corp.contoso.local", withAll.GetString("forest_name"));
        Assert.Equal(321, withAll.GetInt64("max_results"));

        var withDomainOnly = tool.CreateDomainForestAndMaxResultsMeta("contoso.local", " ", 22);
        Assert.Equal("contoso.local", withDomainOnly.GetString("domain_name"));
        Assert.Null(withDomainOnly.GetString("forest_name"));
        Assert.Equal(22, withDomainOnly.GetInt64("max_results"));
    }

    [Fact]
    public void TryMapCollectionFailure_ShouldReturnFalseAndDefaultMessageWhenCollectionFailsWithoutError() {
        var tool = new HarnessTool();

        var ok = tool.MapCollectionFailure(
            collectionSucceeded: false,
            collectionError: null,
            defaultErrorMessage: "Policy query failed.",
            out var errorResponse);

        Assert.False(ok);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Policy query failed.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void TryMapCollectionFailure_ShouldReturnTrueWhenCollectionSucceeds() {
        var tool = new HarnessTool();

        var ok = tool.MapCollectionFailure(
            collectionSucceeded: true,
            collectionError: "ignored",
            defaultErrorMessage: "Policy query failed.",
            out var errorResponse);

        Assert.True(ok);
        Assert.Null(errorResponse);
    }

    [Fact]
    public void TryExecuteCollectionQuery_ShouldReturnResultWhenCollectionSucceeds() {
        var tool = new HarnessTool();

        var ok = tool.ExecuteCollectionQuerySuccess(out var marker, out var errorResponse);

        Assert.True(ok);
        Assert.Null(errorResponse);
        Assert.Equal("ok", marker);
    }

    [Fact]
    public void TryExecuteCollectionQuery_ShouldMapCollectionFailureToQueryFailed() {
        var tool = new HarnessTool();

        var ok = tool.ExecuteCollectionQueryFailure(out var errorResponse);

        Assert.False(ok);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Collection error.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void TryExecuteCollectionQuery_ShouldReturnContractErrorWhenCollectionShapeIsMissing() {
        var tool = new HarnessTool();

        var ok = tool.ExecuteCollectionQueryInvalidContract(out var errorResponse);

        Assert.False(ok);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Collection view contract is invalid.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void TryExecuteCollectionQuery_WithTypedSelectors_ShouldReturnResultWhenCollectionSucceeds() {
        var tool = new HarnessTool();

        var ok = tool.ExecuteTypedCollectionQuerySuccess(out var marker, out var errorResponse);

        Assert.True(ok);
        Assert.Null(errorResponse);
        Assert.Equal("ok-typed", marker);
    }

    [Fact]
    public void TryExecuteCollectionQuery_WithTypedSelectors_ShouldMapCollectionFailureToQueryFailed() {
        var tool = new HarnessTool();

        var ok = tool.ExecuteTypedCollectionQueryFailure(out var errorResponse);

        Assert.False(ok);
        Assert.NotNull(errorResponse);
        using var doc = JsonDocument.Parse(errorResponse!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Typed collection error.", root.GetProperty("error").GetString());
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

        public bool TryReadRequiredDomainNameArgument(
            JsonObject? arguments,
            out string domainName,
            out string? errorResponse) {
            return TryReadRequiredDomainName(arguments, out domainName, out errorResponse);
        }

        public bool TryReadRequiredDomainQuery(
            JsonObject? arguments,
            bool useOptionCapDefaultForNonPositive,
            out string? domainName,
            out int maxResults,
            out string? errorResponse) {
            var ok = TryReadRequiredDomainQueryRequest(
                arguments: arguments,
                request: out var request,
                errorResponse: out errorResponse,
                nonPositiveBehavior: useOptionCapDefaultForNonPositive
                    ? MaxResultsNonPositiveBehavior.DefaultToOptionCap
                    : MaxResultsNonPositiveBehavior.ClampToOne);
            domainName = ok ? request.DomainName : null;
            maxResults = ok ? request.MaxResults : 0;
            return ok;
        }

        public Task<string> ExecutePolicyToolAsync(
            JsonObject? arguments,
            Func<string, MockPolicyView> query,
            Action<JsonObject, MockPolicyView, MockPolicyResult>? additionalMetaMutate = null,
            IReadOnlyList<string>? additionalUnconfiguredValues = null) {
            return ExecutePolicyAttributionTool<MockPolicyView, MockPolicyResult>(
                arguments: arguments,
                cancellationToken: CancellationToken.None,
                title: "Active Directory: Test Policy (preview)",
                defaultErrorMessage: "Policy query failed.",
                query: query,
                attributionSelector: static view => view.Attribution,
                additionalMetaMutate: additionalMetaMutate is null
                    ? null
                    : (meta, _, view, result) => additionalMetaMutate(meta, view, result),
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

        public int ResolveDefaultingMaxResults(JsonObject? arguments) {
            return ResolveMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        }

        public int ResolveClampedMaxResults(JsonObject? arguments) {
            return ResolveMaxResults(arguments);
        }

        public (string? DomainName, string? ForestName, int MaxResults) ResolveDomainAndForestScopeWithMax(
            JsonObject? arguments,
            bool useOptionCapDefaultForNonPositive = false) {
            return ResolveDomainAndForestScopeWithMaxResults(
                arguments,
                nonPositiveBehavior: useOptionCapDefaultForNonPositive
                    ? MaxResultsNonPositiveBehavior.DefaultToOptionCap
                    : MaxResultsNonPositiveBehavior.ClampToOne);
        }

        public JsonObject CreateScopeMeta(string? domainName, string? forestName) {
            var meta = new JsonObject();
            AddDomainAndForestMeta(meta, domainName, forestName);
            return meta;
        }

        public JsonObject CreateDomainAndMaxResultsMeta(string domainName, int maxResults) {
            var meta = new JsonObject();
            AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
            return meta;
        }

        public JsonObject CreateDomainForestAndMaxResultsMeta(string? domainName, string? forestName, int maxResults) {
            var meta = new JsonObject();
            AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            return meta;
        }

        public bool MapCollectionFailure(
            bool collectionSucceeded,
            string? collectionError,
            string defaultErrorMessage,
            out string? errorResponse) {
            return TryMapCollectionFailure(
                collectionSucceeded,
                collectionError,
                defaultErrorMessage,
                out errorResponse);
        }

        public bool ExecuteCollectionQuerySuccess(out string? marker, out string? errorResponse) {
            var ok = TryExecuteCollectionQuery(
                query: static () => new MockCollectionContractView(
                    CollectionSucceeded: true,
                    CollectionError: null,
                    Marker: "ok"),
                result: out var view,
                errorResponse: out errorResponse,
                defaultErrorMessage: "Collection query failed.");
            marker = ok ? view.Marker : null;
            return ok;
        }

        public bool ExecuteCollectionQueryFailure(out string? errorResponse) {
            return TryExecuteCollectionQuery(
                query: static () => new MockCollectionContractView(
                    CollectionSucceeded: false,
                    CollectionError: "Collection error.",
                    Marker: "failed"),
                result: out _,
                errorResponse: out errorResponse,
                defaultErrorMessage: "Collection query failed.");
        }

        public bool ExecuteCollectionQueryInvalidContract(out string? errorResponse) {
            return TryExecuteCollectionQuery(
                query: static () => new MockInvalidCollectionContractView("invalid"),
                result: out _,
                errorResponse: out errorResponse,
                defaultErrorMessage: "Collection query failed.");
        }

        public bool ExecuteTypedCollectionQuerySuccess(out string? marker, out string? errorResponse) {
            var ok = TryExecuteCollectionQuery(
                query: static () => new MockCollectionContractView(
                    CollectionSucceeded: true,
                    CollectionError: null,
                    Marker: "ok-typed"),
                collectionSucceededSelector: static view => view!.CollectionSucceeded,
                collectionErrorSelector: static view => view!.CollectionError,
                result: out var view,
                errorResponse: out errorResponse,
                defaultErrorMessage: "Collection query failed.");
            marker = ok ? view.Marker : null;
            return ok;
        }

        public bool ExecuteTypedCollectionQueryFailure(out string? errorResponse) {
            return TryExecuteCollectionQuery(
                query: static () => new MockCollectionContractView(
                    CollectionSucceeded: false,
                    CollectionError: "Typed collection error.",
                    Marker: "typed-failed"),
                collectionSucceededSelector: static view => view!.CollectionSucceeded,
                collectionErrorSelector: static view => view!.CollectionError,
                result: out _,
                errorResponse: out errorResponse,
                defaultErrorMessage: "Collection query failed.");
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }

        internal sealed record MockPolicyView(IReadOnlyList<PolicyAttribution> Attribution, string Marker);

        internal sealed record MockCollectionContractView(
            bool CollectionSucceeded,
            string? CollectionError,
            string Marker);

        internal sealed record MockInvalidCollectionContractView(string Marker);

        internal sealed record MockPolicyResult(
            string DomainName,
            bool IncludeAttribution,
            bool ConfiguredAttributionOnly,
            int Scanned,
            bool Truncated,
            string Marker,
            IReadOnlyList<PolicyAttribution> Attribution);
    }
}
