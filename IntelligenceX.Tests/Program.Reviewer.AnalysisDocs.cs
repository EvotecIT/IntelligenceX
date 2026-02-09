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

            AssertEqual("learn.microsoft.com", uri.Host, $"{rule.Id} docs host is Learn");

            // Ignore harmless URL canonicalization differences (e.g. query strings on docs URLs).
            var normalizedUri = new UriBuilder(uri) { Query = "", Fragment = "" }.Uri;
            var path = normalizedUri.AbsolutePath;
            const string learnPrefix = "/powershell/utility-modules/psscriptanalyzer/rules/";
            AssertEqual(true, path.StartsWith(learnPrefix, StringComparison.OrdinalIgnoreCase), $"{rule.Id} docs uses PSScriptAnalyzer Learn rules path");

            var actualSlug = path.Substring(learnPrefix.Length).Trim('/');
            AssertEqual(false, string.IsNullOrWhiteSpace(actualSlug), $"{rule.Id} docs slug is present");
            AssertEqual(false, actualSlug.Any(char.IsWhiteSpace), $"{rule.Id} docs slug has no whitespace");
        }
    }
}
#endif
