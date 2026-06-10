using System;
using System.Collections.Generic;
using System.Linq;
using CertNoob.Dashboard;
using CertNoob.Pki;

namespace IntelligenceX.Tools.ADPlayground;

internal static class AdPkiToolSupport {
    private const string AnyPurposeEku = "2.5.29.37.0";
    private const string ClientAuthenticationEku = "1.3.6.1.5.5.7.3.2";
    private const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";
    private const string SmartCardLogonEku = "1.3.6.1.4.1.311.20.2.2";

    internal sealed record PkiFindingRow(
        string Id,
        string Category,
        string Severity,
        string Title,
        string Statement,
        int AffectedCount);

    internal sealed record PkiEndpointRow(
        string Policy,
        string Type,
        string Endpoint,
        bool Insecure);

    internal static PkiAssessmentSnapshot BuildAssessment(
        string? forestName,
        bool includeEnrollmentPolicyEntries,
        bool includeAcls = true,
        bool includeIssuedRequestSample = false) {
        var options = new PkiAssessmentOptions {
            IncludeDashboard = true,
            IncludeEsc = true,
            IncludeTemplateRisk = true,
            IncludeAcls = includeAcls,
            IncludeGpoAutoEnrollment = false,
            IncludeAutoEnrollmentBaseline = false,
            IncludeHostAnalysis = false,
            IncludeEnrollmentPolicyEntries = includeEnrollmentPolicyEntries,
            IncludeDirectoryPrincipals = false,
            IncludeTrustStores = false,
            IncludeKraEntries = false,
            IncludePendingRequestBacklog = false,
            IncludeFailedRequestBacklog = false,
            IncludeIssuedRequestSample = includeIssuedRequestSample,
            IncludeExpiringCertificateSample = false,
            IncludeRevokedRequestSample = false,
            IncludeAclPrincipalPosture = false
        };

        var domains = string.IsNullOrWhiteSpace(forestName)
            ? null
            : new[] { forestName.Trim() };

        return PkiAssessmentService.Build(options, domains: domains);
    }

    internal static string ResolveScopeName(PkiAssessmentSnapshot snapshot, string? fallback) {
        var domain = snapshot.Domains.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(domain)) {
            return domain!;
        }

        var dashboardDomain = snapshot.Dashboard?.Domain;
        if (!string.IsNullOrWhiteSpace(dashboardDomain)) {
            return dashboardDomain!;
        }

        return fallback ?? string.Empty;
    }

    internal static IReadOnlyList<PkiFinding> BuildFindings(PkiAssessmentSnapshot snapshot) =>
        PkiFindingsService.Build(snapshot).Findings;

    internal static IReadOnlyList<PkiFindingRow> ToFindingRows(IEnumerable<PkiFinding> findings, int maxRows) =>
        findings
            .Take(maxRows)
            .Select(f => new PkiFindingRow(
                Id: f.Id,
                Category: f.Category.ToString(),
                Severity: f.Severity.ToString(),
                Title: f.Title,
                Statement: f.Statement,
                AffectedCount: f.AffectedCount))
            .ToArray();

    internal static bool IsWeakKeyTemplate(TemplateRiskView template) =>
        template.MinimalKeySize.HasValue &&
        template.MinimalKeySize.Value >= 1024 &&
        template.MinimalKeySize.Value < 2048;

    internal static bool IsTakeoverRiskTemplate(TemplateRiskView template) =>
        template.VulnerabilityCount > 0 ||
        template.FullControlPrincipalCount > 0 ||
        template.EnrolleeSuppliesSubject ||
        template.EnrolleeSuppliesSubjectAltName;

    internal static bool IsCodeSigningTemplate(TemplateRiskView template) =>
        ContainsEku(template.ExtendedKeyUsages, CodeSigningEku);

    internal static bool IsAuthenticationCapableTemplate(TemplateRiskView template) =>
        template.ExtendedKeyUsages.Count == 0 ||
        ContainsEku(template.ExtendedKeyUsages, AnyPurposeEku) ||
        ContainsEku(template.ExtendedKeyUsages, ClientAuthenticationEku) ||
        ContainsEku(template.ExtendedKeyUsages, SmartCardLogonEku);

    internal static IReadOnlyList<PkiEndpointRow> EnumerateEndpointRows(PkiAssessmentSnapshot snapshot) {
        if (snapshot.EnrollmentPolicyEntries == null || snapshot.EnrollmentPolicyEntries.Count == 0) {
            return Array.Empty<PkiEndpointRow>();
        }

        return snapshot.EnrollmentPolicyEntries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(e => EnumerateEndpointRows(e))
            .ToArray();
    }

    internal static bool IsWeakRsaOrCryptoFinding(PkiFinding finding) =>
        string.Equals(finding.Id, PkiFindingIds.TemplateWeakMinimalKeySize, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(finding.Id, PkiFindingIds.IssuedWeakAlgorithms, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(finding.Id, PkiFindingIds.PendingRequestsWeakKeys, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(finding.Id, PkiFindingIds.FailedRequestsWeakKeys, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(finding.Category.ToString(), nameof(PkiFindingCategory.Crypto), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<PkiEndpointRow> EnumerateEndpointRows(EnrollmentPolicyEntry entry) {
        foreach (var endpoint in EnumerateEndpointValues(entry)) {
            if (string.IsNullOrWhiteSpace(endpoint.Value)) {
                continue;
            }

            var value = endpoint.Value.Trim();
            yield return new PkiEndpointRow(
                Policy: !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : entry.DistinguishedName,
                Type: endpoint.Type,
                Endpoint: value,
                Insecure: value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<(string Type, string Value)> EnumerateEndpointValues(EnrollmentPolicyEntry entry) {
        foreach (var value in entry.ServerEndpoints) {
            yield return ("ServerEndpoint", value);
        }

        foreach (var value in entry.EnrollmentServers) {
            yield return ("EnrollmentServer", value);
        }
    }

    private static bool ContainsEku(IReadOnlyCollection<string> ekus, string oid) =>
        ekus.Any(eku => string.Equals(eku?.Trim(), oid, StringComparison.OrdinalIgnoreCase));
}
