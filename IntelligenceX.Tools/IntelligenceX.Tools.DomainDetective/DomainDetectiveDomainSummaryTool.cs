using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

#if DOMAINDETECTIVE_ENABLED
using DnsClientX;
using DomainDetective;
#endif

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Runs selected DomainDetective checks and returns a condensed domain posture summary.
/// </summary>
public sealed class DomainDetectiveDomainSummaryTool : DomainDetectiveToolBase, ITool {
    private sealed record DomainSummaryRequest(
        string Domain,
        IReadOnlyList<string> RequestedChecks,
        int TimeoutMs,
        int MaxHints,
        bool IncludeAnalysisOverview,
        string EndpointText);

    private static readonly ToolDefinition DefinitionValue = new(
        "domaindetective_domain_summary",
        "Run selected DomainDetective checks for a domain and return a condensed DNS/email/security posture summary.",
        ToolSchema.Object(
                ("domain", ToolSchema.String("Domain name to analyze (for example: contoso.com).")),
                ("checks", ToolSchema.Array(ToolSchema.String(), "Optional DomainDetective check names (enum strings). Defaults to a bounded baseline set.")),
                ("dns_endpoint", ToolSchema.String("Optional DnsClientX endpoint used by DomainDetective (default: System).")),
                ("timeout_ms", ToolSchema.Integer("Verification timeout in milliseconds (capped by pack options).")),
                ("max_hints", ToolSchema.Integer("Maximum remediation hints returned (capped by pack options).")),
                ("include_analysis_overview", ToolSchema.Boolean("Include analysis map availability summary (default: true).")))
            .Required("domain")
            .NoAdditionalProperties(),
        category: "dns",
        tags: new[] {
            "domain_posture",
            "dns"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectiveDomainSummaryTool"/> class.
    /// </summary>
    public DomainDetectiveDomainSummaryTool(DomainDetectiveToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<DomainSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var domain = reader.OptionalString("domain");
            if (string.IsNullOrWhiteSpace(domain)) {
                return ToolRequestBindingResult<DomainSummaryRequest>.Failure("domain is required.");
            }

            var requestedChecks = reader.DistinctStringArray("checks");
            if (requestedChecks.Count == 0) {
                requestedChecks = DomainDetectiveCheckNameCatalog.DefaultChecks.ToArray();
            }

            return ToolRequestBindingResult<DomainSummaryRequest>.Success(new DomainSummaryRequest(
                Domain: domain,
                RequestedChecks: requestedChecks,
                TimeoutMs: reader.CappedInt32("timeout_ms", Options.DefaultTimeoutMs, 1000, Options.MaxTimeoutMs),
                MaxHints: reader.CappedInt32("max_hints", Options.MaxHints, 1, Options.MaxHints),
                IncludeAnalysisOverview: reader.Boolean("include_analysis_overview", defaultValue: true),
                EndpointText: reader.OptionalString("dns_endpoint") ?? "System"));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<DomainSummaryRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;

#if !DOMAINDETECTIVE_ENABLED
        return ToolResultV2.Error(
            errorCode: "dependency_unavailable",
            error: "DomainDetective dependency is not available in this build.",
            hints: new[] {
                "Provide DomainDetective as a sibling source checkout or package reference.",
                "Disable the domaindetective pack when running in builds without the dependency."
            },
            isTransient: false);
#else
        if (!Enum.TryParse<DnsEndpoint>(request.EndpointText, ignoreCase: true, out var endpoint)) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported dns_endpoint '{request.EndpointText}'.",
                hints: new[] { "Use a known DnsClientX endpoint such as System, Cloudflare, Google, or Quad9." },
                isTransient: false);
        }

        if (!TryResolveChecks(request.RequestedChecks, out var checks, out var invalidChecks)) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported check names: {string.Join(", ", invalidChecks)}.",
                hints: new[] { "Use DomainDetective.HealthCheckType enum names (for example DNSHEALTH, SOA, NS, MX, SPF, DMARC, DNSSEC, TTL)." },
                isTransient: false);
        }

        var warnings = new List<string>();
        if (checks.Length > Options.MaxChecks) {
            checks = checks.Take(Options.MaxChecks).ToArray();
            warnings.Add($"Requested checks were capped to {Options.MaxChecks}.");
        }

        var healthCheck = new DomainHealthCheck(endpoint);
        try {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(request.TimeoutMs);

            await healthCheck.Verify(
                domainName: request.Domain,
                healthCheckTypes: checks,
                cancellationToken: linkedCts.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ToolResultV2.Error(
                errorCode: "timeout",
                error: $"DomainDetective run timed out after timeout_ms={request.TimeoutMs}.",
                hints: new[] {
                    "Lower the number of checks in checks[] for faster runs.",
                    "Increase timeout_ms for slower networks/domains."
                },
                isTransient: true);
        } catch (ArgumentException ex) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: ex.Message,
                hints: new[] { "Verify domain format and requested check names." },
                isTransient: false);
        } catch (Exception ex) {
            return ToolResultV2.Error(
                errorCode: "query_failed",
                error: $"DomainDetective execution failed: {ex.Message}",
                hints: new[] {
                    "Retry with a narrower checks[] selection.",
                    "Confirm DNS/network connectivity for the target domain."
                },
                isTransient: true);
        }

        var summary = healthCheck.BuildSummary();
        var mappedSummary = MapSummary(summary, request.MaxHints, out var hintsTruncated);
        if (hintsTruncated) {
            warnings.Add($"Summary hints were capped to {request.MaxHints}.");
        }

        var analysisOverview = request.IncludeAnalysisOverview
            ? BuildAnalysisOverview(healthCheck)
            : Array.Empty<DomainDetectiveAnalysisOverviewModel>();

        var result = new DomainDetectiveDomainSummaryResultModel {
            Domain = request.Domain,
            DnsEndpoint = endpoint.ToString(),
            TimeoutMs = request.TimeoutMs,
            ChecksRequested = checks.Select(static check => check.ToString()).ToArray(),
            ChecksTruncated = request.RequestedChecks.Count > checks.Length,
            Summary = mappedSummary,
            AnalysisOverview = analysisOverview,
            Warnings = warnings
        };

        var summaryMarkdown = BuildSummaryMarkdown(result);

        var meta = ToolOutputHints.Meta(count: 1, truncated: result.ChecksTruncated || hintsTruncated)
            .Add("domain", result.Domain)
            .Add("checks", result.ChecksRequested.Count)
            .Add("dns_endpoint", result.DnsEndpoint)
            .Add("hints_count", result.Summary.Hints.Count);

        return ToolOutputEnvelope.OkFlatWithRenderValue(
            root: ToolJson.ToJsonObjectSnakeCase(result),
            meta: meta,
            summaryMarkdown: summaryMarkdown,
            render: BuildRenderHints(
                analysisOverviewCount: result.AnalysisOverview.Count,
                checksRequestedCount: result.ChecksRequested.Count,
                hintCount: result.Summary.Hints.Count));
#endif
    }

#if DOMAINDETECTIVE_ENABLED
    private static bool TryResolveChecks(
        IReadOnlyList<string> requestedChecks,
        out HealthCheckType[] checks,
        out IReadOnlyList<string> invalidChecks) {
        var resolved = new List<HealthCheckType>();
        var invalid = new List<string>();

        for (var i = 0; i < requestedChecks.Count; i++) {
            var check = requestedChecks[i];
            if (string.IsNullOrWhiteSpace(check)) {
                continue;
            }

            if (TryResolveHealthCheckType(check, out var parsed)) {
                if (!resolved.Contains(parsed)) {
                    resolved.Add(parsed);
                }
                continue;
            }

            invalid.Add(check);
        }

        checks = resolved.ToArray();
        invalidChecks = invalid;
        return invalid.Count == 0 && checks.Length > 0;
    }

    private static bool TryResolveHealthCheckType(string check, out HealthCheckType parsed) {
        return DomainDetectiveCheckNameCatalog.TryResolveHealthCheckType(check, out parsed);
    }
#endif

    private static string NormalizeDomainDetectiveCheckName(string check) {
        return DomainDetectiveCheckNameCatalog.NormalizeDomainDetectiveCheckName(check);
    }

    private static string NormalizeCheckLookupToken(string value) {
        return DomainDetectiveCheckNameCatalog.NormalizeCheckLookupToken(value);
    }

    private static string BuildSummaryMarkdown(DomainDetectiveDomainSummaryResultModel result) {
        return ToolMarkdown.SummaryFacts(
            title: "DomainDetective domain summary",
            facts: new[] {
                ("Domain", result.Domain),
                ("Checks", result.ChecksRequested.Count.ToString(CultureInfo.InvariantCulture)),
                ("SPF valid", result.Summary.SpfValid ? "yes" : "no"),
                ("DMARC valid", result.Summary.DmarcValid ? "yes" : "no"),
                ("DKIM status", ResolveDkimStatusSummary(result.Summary)),
                ("DNSSEC valid", result.Summary.DnsSecValid ? "yes" : "no"),
                ("Expires soon", result.Summary.ExpiresSoon ? "yes" : "no")
            });
    }

    private static string ResolveDkimStatusSummary(DomainDetectiveSummaryModel summary) {
        return ResolveDkimStatusSummary(summary.HasDkimRecord, summary.DkimValid);
    }

    private static string ResolveDkimStatusSummary(bool hasDkimRecord, bool dkimValid) {
        if (hasDkimRecord) {
            return dkimValid ? "valid" : "invalid";
        }

        return "no selector evidence";
    }

    private static JsonValue? BuildRenderHints(
        int analysisOverviewCount,
        int checksRequestedCount,
        int hintCount) {
        var hints = new JsonArray();

        if (analysisOverviewCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "analysis_overview",
                    new ToolColumn("check", "Check", "string"),
                    new ToolColumn("has_result", "Has result", "bool"),
                    new ToolColumn("result_type", "Result type", "string"))
                .Add("priority", 400));
        }

        if (checksRequestedCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "checks_requested",
                    new ToolColumn("value", "Requested check", "string"))
                .Add("priority", 300));
        }

        if (hintCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "summary/hints",
                    new ToolColumn("value", "Hint", "string"))
                .Add("priority", 200));
        }

        if (hints.Count == 0) {
            return null;
        }

        return JsonValue.From(hints);
    }

#if DOMAINDETECTIVE_ENABLED
    private static DomainDetectiveSummaryModel MapSummary(DomainSummary summary, int maxHints, out bool hintsTruncated) {
        var hints = (summary.Hints ?? Array.Empty<string>())
            .Where(static hint => !string.IsNullOrWhiteSpace(hint))
            .Select(static hint => hint.Trim())
            .ToArray();

        var take = Math.Min(hints.Length, maxHints);
        hintsTruncated = hints.Length > take;

        return new DomainDetectiveSummaryModel {
            HasSpfRecord = summary.HasSpfRecord,
            SpfValid = summary.SpfValid,
            HasDmarcRecord = summary.HasDmarcRecord,
            DmarcPolicy = summary.DmarcPolicy ?? string.Empty,
            DmarcValid = summary.DmarcValid,
            HasDkimRecord = summary.HasDkimRecord,
            DkimValid = summary.DkimValid,
            DkimStatus = ResolveDkimStatusSummary(summary.HasDkimRecord, summary.DkimValid),
            HasMxRecord = summary.HasMxRecord,
            DnsSecValid = summary.DnsSecValid,
            DnsSecKeyExpiresSoon = summary.DnsSecKeyExpiresSoon,
            IsPublicSuffix = summary.IsPublicSuffix,
            ExpiryDate = summary.ExpiryDate ?? string.Empty,
            DaysUntilExpiration = summary.DaysUntilExpiration,
            ExpiresSoon = summary.ExpiresSoon,
            IsExpired = summary.IsExpired,
            RegistrarLocked = summary.RegistrarLocked,
            PrivacyProtected = summary.PrivacyProtected,
            Hints = hints.Take(take).ToArray()
        };
    }

    private static IReadOnlyList<DomainDetectiveAnalysisOverviewModel> BuildAnalysisOverview(DomainHealthCheck healthCheck) {
        var map = healthCheck.GetAnalysisMap();
        if (map is null || map.Count == 0) {
            return Array.Empty<DomainDetectiveAnalysisOverviewModel>();
        }

        return map
            .OrderBy(static entry => entry.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new DomainDetectiveAnalysisOverviewModel {
                Check = entry.Key.ToString(),
                HasResult = entry.Value is not null,
                ResultType = entry.Value?.GetType().Name
            })
            .ToArray();
    }
#endif

    private sealed class DomainDetectiveDomainSummaryResultModel {
        public string Domain { get; init; } = string.Empty;
        public string DnsEndpoint { get; init; } = string.Empty;
        public int TimeoutMs { get; init; }
        public IReadOnlyList<string> ChecksRequested { get; init; } = Array.Empty<string>();
        public bool ChecksTruncated { get; init; }
        public DomainDetectiveSummaryModel Summary { get; init; } = new();
        public IReadOnlyList<DomainDetectiveAnalysisOverviewModel> AnalysisOverview { get; init; } = Array.Empty<DomainDetectiveAnalysisOverviewModel>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    private sealed class DomainDetectiveSummaryModel {
        public bool HasSpfRecord { get; init; }
        public bool SpfValid { get; init; }
        public bool HasDmarcRecord { get; init; }
        public string DmarcPolicy { get; init; } = string.Empty;
        public bool DmarcValid { get; init; }
        public bool HasDkimRecord { get; init; }
        public bool DkimValid { get; init; }
        public string DkimStatus { get; init; } = string.Empty;
        public bool HasMxRecord { get; init; }
        public bool DnsSecValid { get; init; }
        public bool DnsSecKeyExpiresSoon { get; init; }
        public bool IsPublicSuffix { get; init; }
        public string ExpiryDate { get; init; } = string.Empty;
        public int? DaysUntilExpiration { get; init; }
        public bool ExpiresSoon { get; init; }
        public bool IsExpired { get; init; }
        public bool RegistrarLocked { get; init; }
        public bool PrivacyProtected { get; init; }
        public IReadOnlyList<string> Hints { get; init; } = Array.Empty<string>();
    }

    private sealed class DomainDetectiveAnalysisOverviewModel {
        public string Check { get; init; } = string.Empty;
        public bool HasResult { get; init; }
        public string? ResultType { get; init; }
    }
}
