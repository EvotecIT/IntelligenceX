using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private static readonly string[] DefaultChecks = {
        "DNSHEALTH",
        "SOA",
        "NS",
        "MX",
        "SPF",
        "DMARC",
        "DNSSEC",
        "TTL"
    };
    private static readonly IReadOnlyDictionary<string, string> CheckAliasByToken = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["NAMESERVER"] = "NS",
        ["NAMESERVERS"] = "NS",
        ["NAMESERVERRECORD"] = "NS",
        ["NAMESERVERRECORDS"] = "NS",
        ["NSRECORD"] = "NS",
        ["NSRECORDS"] = "NS",
        ["MXRECORD"] = "MX",
        ["MXRECORDS"] = "MX",
        ["MAILSERVER"] = "MX",
        ["MAILSERVERS"] = "MX",
        ["SPFRECORD"] = "SPF",
        ["SPFRECORDS"] = "SPF",
        ["DMARCRECORD"] = "DMARC",
        ["DMARCRECORDS"] = "DMARC",
        ["DKIMRECORD"] = "DKIM",
        ["DKIMRECORDS"] = "DKIM",
        ["CAARECORD"] = "CAA",
        ["CAARECORDS"] = "CAA"
    };

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
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectiveDomainSummaryTool"/> class.
    /// </summary>
    public DomainDetectiveDomainSummaryTool(DomainDetectiveToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var domain = ToolArgs.GetOptionalTrimmed(arguments, "domain");
        if (string.IsNullOrWhiteSpace(domain)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "domain is required.",
                hints: new[] { "Provide a registrable domain name such as contoso.com." },
                isTransient: false);
        }

        var requestedChecks = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("checks"));
        if (requestedChecks.Count == 0) {
            requestedChecks.AddRange(DefaultChecks);
        }

        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", Options.DefaultTimeoutMs, 1000, Options.MaxTimeoutMs);
        var maxHints = ToolArgs.GetCappedInt32(arguments, "max_hints", Options.MaxHints, 1, Options.MaxHints);
        var includeAnalysisOverview = ToolArgs.GetBoolean(arguments, "include_analysis_overview", defaultValue: true);
        var endpointText = ToolArgs.GetTrimmedOrDefault(arguments, "dns_endpoint", "System");

#if !DOMAINDETECTIVE_ENABLED
        return ToolResponse.Error(
            errorCode: "dependency_unavailable",
            error: "DomainDetective dependency is not available in this build.",
            hints: new[] {
                "Provide DomainDetective as a sibling source checkout or package reference.",
                "Disable the domaindetective pack when running in builds without the dependency."
            },
            isTransient: false);
#else
        if (!Enum.TryParse<DnsEndpoint>(endpointText, ignoreCase: true, out var endpoint)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported dns_endpoint '{endpointText}'.",
                hints: new[] { "Use a known DnsClientX endpoint such as System, Cloudflare, Google, or Quad9." },
                isTransient: false);
        }

        if (!TryResolveChecks(requestedChecks, out var checks, out var invalidChecks)) {
            return ToolResponse.Error(
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
            linkedCts.CancelAfter(timeoutMs);

            await healthCheck.Verify(
                domainName: domain,
                healthCheckTypes: checks,
                cancellationToken: linkedCts.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ToolResponse.Error(
                errorCode: "timeout",
                error: $"DomainDetective run timed out after timeout_ms={timeoutMs}.",
                hints: new[] {
                    "Lower the number of checks in checks[] for faster runs.",
                    "Increase timeout_ms for slower networks/domains."
                },
                isTransient: true);
        } catch (ArgumentException ex) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: ex.Message,
                hints: new[] { "Verify domain format and requested check names." },
                isTransient: false);
        } catch (Exception ex) {
            return ToolResponse.Error(
                errorCode: "query_failed",
                error: $"DomainDetective execution failed: {ex.Message}",
                hints: new[] {
                    "Retry with a narrower checks[] selection.",
                    "Confirm DNS/network connectivity for the target domain."
                },
                isTransient: true);
        }

        var summary = healthCheck.BuildSummary();
        var mappedSummary = MapSummary(summary, maxHints, out var hintsTruncated);
        if (hintsTruncated) {
            warnings.Add($"Summary hints were capped to {maxHints}.");
        }

        var analysisOverview = includeAnalysisOverview
            ? BuildAnalysisOverview(healthCheck)
            : Array.Empty<DomainDetectiveAnalysisOverviewModel>();

        var result = new DomainDetectiveDomainSummaryResultModel {
            Domain = domain,
            DnsEndpoint = endpoint.ToString(),
            TimeoutMs = timeoutMs,
            ChecksRequested = checks.Select(static check => check.ToString()).ToArray(),
            ChecksTruncated = requestedChecks.Count > checks.Length,
            Summary = mappedSummary,
            AnalysisOverview = analysisOverview,
            Warnings = warnings
        };

        var summaryMarkdown = ToolMarkdown.SummaryFacts(
            title: "DomainDetective domain summary",
            facts: new[] {
                ("Domain", result.Domain),
                ("Checks", result.ChecksRequested.Count.ToString(CultureInfo.InvariantCulture)),
                ("SPF valid", result.Summary.SpfValid ? "yes" : "no"),
                ("DMARC valid", result.Summary.DmarcValid ? "yes" : "no"),
                ("DNSSEC valid", result.Summary.DnsSecValid ? "yes" : "no"),
                ("Expires soon", result.Summary.ExpiresSoon ? "yes" : "no")
            });

        var meta = ToolOutputHints.Meta(count: 1, truncated: result.ChecksTruncated || hintsTruncated)
            .Add("domain", result.Domain)
            .Add("checks", result.ChecksRequested.Count)
            .Add("dns_endpoint", result.DnsEndpoint)
            .Add("hints_count", result.Summary.Hints.Count);

        return ToolResponse.OkModel(result, meta: meta, summaryMarkdown: summaryMarkdown);
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
        parsed = default;
        if (string.IsNullOrWhiteSpace(check)) {
            return false;
        }

        var trimmed = check.Trim();
        if (Enum.TryParse<HealthCheckType>(trimmed, ignoreCase: true, out parsed)) {
            return true;
        }

        var normalized = NormalizeDomainDetectiveCheckName(trimmed);
        if (normalized.Length == 0) {
            return false;
        }

        return Enum.TryParse<HealthCheckType>(normalized, ignoreCase: true, out parsed);
    }
#endif

    private static string NormalizeDomainDetectiveCheckName(string check) {
        var token = NormalizeCheckLookupToken(check);
        if (token.Length == 0) {
            return string.Empty;
        }

        if (CheckAliasByToken.TryGetValue(token, out var alias)) {
            return alias;
        }

        return token;
    }

    private static string NormalizeCheckLookupToken(string value) {
        var input = (value ?? string.Empty).Trim();
        if (input.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
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
