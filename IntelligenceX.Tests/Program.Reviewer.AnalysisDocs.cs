namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisCatalogPowerShellDocsLinksMatchLearnPattern() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        foreach (var entry in catalog.Rules) {
            var rule = entry.Value;
            if (!string.Equals(rule.Language, "powershell", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!string.Equals(rule.Tool, "PSScriptAnalyzer", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var docsRaw = rule.Docs;
            AssertEqual(false, string.IsNullOrWhiteSpace(docsRaw), $"{rule.Id} docs is populated");
            if (string.IsNullOrWhiteSpace(docsRaw)) {
                throw new InvalidOperationException($"{rule.Id} docs is populated");
            }

            var docs = docsRaw.Trim();
            AssertEqual(false, docs.Any(char.IsWhiteSpace), $"{rule.Id} docs has no whitespace");
            AssertEqual(true, Uri.IsWellFormedUriString(docs, UriKind.Absolute), $"{rule.Id} docs is well-formed");
            if (!Uri.TryCreate(docs, UriKind.Absolute, out var uri) || uri is null) {
                throw new InvalidOperationException($"Expected {rule.Id} docs to be a valid absolute url, got '{docs}'.");
            }
            AssertEqual("https", uri.Scheme, $"{rule.Id} docs uses https");

            // Prefer Learn; tolerate harmless canonicalization differences like subdomains.
            var hostOk =
                uri.Host.Equals("learn.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".learn.microsoft.com", StringComparison.OrdinalIgnoreCase);
            AssertEqual(true, hostOk, $"{rule.Id} docs host is Learn");

            // Ignore harmless URL canonicalization differences (e.g. query strings on docs URLs).
            var normalizedUri = new UriBuilder(uri) { Query = "", Fragment = "" }.Uri;
            var path = normalizedUri.AbsolutePath;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var offset = 0;
            if (segments.Length >= 6) {
                // Learn sometimes includes a locale segment, e.g. /en-us/powershell/...
                var maybeLocale = segments[0];
                if (maybeLocale.Length == 5 && maybeLocale[2] == '-') {
                    offset = 1;
                }
            }
            AssertEqual(true, segments.Length >= (5 + offset), $"{rule.Id} docs path has enough segments");
            if (segments.Length < (5 + offset)) {
                throw new InvalidOperationException($"Expected {rule.Id} docs path to include '/powershell/utility-modules/psscriptanalyzer/rules/<slug>', got '{path}'.");
            }
            AssertEqual("powershell", segments[offset + 0], $"{rule.Id} docs uses Learn powershell path");
            AssertEqual("utility-modules", segments[offset + 1], $"{rule.Id} docs uses Learn utility-modules path");
            AssertEqual("psscriptanalyzer", segments[offset + 2], $"{rule.Id} docs uses Learn PSScriptAnalyzer path");
            AssertEqual("rules", segments[offset + 3], $"{rule.Id} docs uses Learn rules path");

            var actualSlug = segments[offset + 4].Trim('/');
            AssertEqual(false, string.IsNullOrWhiteSpace(actualSlug), $"{rule.Id} docs slug is present");
            AssertEqual(false, actualSlug.Any(char.IsWhiteSpace), $"{rule.Id} docs slug has no whitespace");
        }
    }
}
#endif
