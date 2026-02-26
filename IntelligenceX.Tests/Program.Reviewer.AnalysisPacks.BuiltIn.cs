using System;
using System.Linq;

namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisPacksAllSecurityIncludesPowerShell() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("all-security-default"), "pack all-security-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("powershell-security-default"), "pack powershell-security-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("csharp-security-default"), "pack csharp-security-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("javascript-security-default"), "pack javascript-security-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-security-default"), "pack python-security-default exists");

        var allSecurity = catalog.Packs["all-security-default"];
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("powershell-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes powershell-security-default");
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("csharp-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes csharp-security-default");
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("javascript-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes javascript-security-default");
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("python-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes python-security-default");

        foreach (var packId in allSecurity.Includes) {
            AssertEqual(true, catalog.Packs.ContainsKey(packId), $"all-security-default include exists: {packId}");
            var includedPack = catalog.Packs[packId];
            foreach (var ruleId in includedPack.Rules) {
                AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"{packId} rule exists: {ruleId}");
            }
        }
    }

    private static void TestAnalysisPacksPowerShellDefaultResolves() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("powershell-default"), "pack powershell-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("powershell-security-default"), "pack powershell-security-default exists");

        var psDefault = catalog.Packs["powershell-default"];
        AssertEqual(true, psDefault.Includes.Any(id => id.Equals("powershell-security-default", StringComparison.OrdinalIgnoreCase)),
            "powershell-default includes powershell-security-default");

        foreach (var include in psDefault.Includes) {
            AssertEqual(true, catalog.Packs.ContainsKey(include), $"powershell-default include exists: {include}");
        }
        foreach (var ruleId in psDefault.Rules) {
            AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"powershell-default rule exists: {ruleId}");
        }
    }

    private static void TestAnalysisPacksAllSecurityTiersResolve() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("all-security-50"), "pack all-security-50 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("all-security-100"), "pack all-security-100 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("all-security-500"), "pack all-security-500 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("all-security-default"), "pack all-security-default exists");

        var allSecurity50 = catalog.Packs["all-security-50"];
        AssertEqual(true, allSecurity50.Includes.Any(id => id.Equals("all-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-50 includes all-security-default");

        var securityDefaultPolicy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "all-security-default" } },
            catalog);
        var security50Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "all-security-50" } },
            catalog);
        var security100Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "all-security-100" } },
            catalog);
        var security500Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "all-security-500" } },
            catalog);

        AssertEqual(true, security50Policy.Rules.Count >= securityDefaultPolicy.Rules.Count,
            "all-security-50 resolves at least all-security-default coverage");
        AssertEqual(true, security100Policy.Rules.Count >= security50Policy.Rules.Count,
            "all-security-100 expands all-security-50");
        AssertEqual(true, security500Policy.Rules.Count >= security100Policy.Rules.Count,
            "all-security-500 expands all-security-100");
    }

    private static void TestAnalysisPacksExternalDefaultsResolve() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("javascript-default"), "pack javascript-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("javascript-security-default"), "pack javascript-security-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-default"), "pack python-default exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-security-default"), "pack python-security-default exists");

        var jsDefault = catalog.Packs["javascript-default"];
        AssertEqual(true, jsDefault.Includes.Any(id => id.Equals("javascript-security-default", StringComparison.OrdinalIgnoreCase)),
            "javascript-default includes javascript-security-default");
        foreach (var include in jsDefault.Includes) {
            AssertEqual(true, catalog.Packs.ContainsKey(include), $"javascript-default include exists: {include}");
        }
        foreach (var ruleId in jsDefault.Rules) {
            AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"javascript-default rule exists: {ruleId}");
        }

        var pyDefault = catalog.Packs["python-default"];
        AssertEqual(true, pyDefault.Includes.Any(id => id.Equals("python-security-default", StringComparison.OrdinalIgnoreCase)),
            "python-default includes python-security-default");
        foreach (var include in pyDefault.Includes) {
            AssertEqual(true, catalog.Packs.ContainsKey(include), $"python-default include exists: {include}");
        }
        foreach (var ruleId in pyDefault.Rules) {
            AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"python-default rule exists: {ruleId}");
        }
    }

    private static void TestAnalysisPacksExternalLanguageTiersResolve() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("javascript-50"), "pack javascript-50 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("javascript-100"), "pack javascript-100 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("javascript-500"), "pack javascript-500 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-50"), "pack python-50 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-100"), "pack python-100 exists");
        AssertEqual(true, catalog.Packs.ContainsKey("python-500"), "pack python-500 exists");

        var jsDefaultPolicy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "javascript-default" } },
            catalog);
        var js50Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "javascript-50" } },
            catalog);
        AssertEqual(true, js50Policy.Rules.Count >= jsDefaultPolicy.Rules.Count,
            "javascript-50 resolves at least javascript-default coverage");

        var pyDefaultPolicy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "python-default" } },
            catalog);
        var py50Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "python-50" } },
            catalog);
        AssertEqual(true, py50Policy.Rules.Count >= pyDefaultPolicy.Rules.Count,
            "python-50 resolves at least python-default coverage");

        var js100Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "javascript-100" } },
            catalog);
        var js500Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "javascript-500" } },
            catalog);
        AssertEqual(true, js100Policy.Rules.Count >= js50Policy.Rules.Count,
            "javascript-100 expands javascript-50");
        AssertEqual(true, js500Policy.Rules.Count >= js100Policy.Rules.Count,
            "javascript-500 expands javascript-100");

        var py100Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "python-100" } },
            catalog);
        var py500Policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(
            new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "python-500" } },
            catalog);
        AssertEqual(true, py100Policy.Rules.Count >= py50Policy.Rules.Count,
            "python-100 expands python-50");
        AssertEqual(true, py500Policy.Rules.Count >= py100Policy.Rules.Count,
            "python-500 expands python-100");
    }

    private static void TestAnalysisPacksPowerShell50ResolvesTo50Rules() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        AssertEqual(true, catalog.Packs.ContainsKey("powershell-50"), "pack powershell-50 exists");

        var pack = catalog.Packs["powershell-50"];
        AssertEqual(50, pack.Rules.Count, "powershell-50 declares 50 rules");
        var declaredRuleIds = pack.Rules.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEqual(50, declaredRuleIds.Length, "powershell-50 declares no duplicate rules");

        var baselineSettings = new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "powershell-default" } };
        var baselinePolicy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(baselineSettings, catalog);
        foreach (var ruleId in baselinePolicy.Rules.Keys) {
            AssertEqual(true, declaredRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase),
                $"powershell-50 includes powershell-default baseline rule: {ruleId}");
        }

        var settings = new IntelligenceX.Analysis.AnalysisSettings { Packs = new[] { "powershell-50" } };
        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);

        var allRuleIds = policy.Rules.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEqual(50, allRuleIds.Length, "powershell-50 resolves to 50 distinct rules");

        foreach (var ruleId in declaredRuleIds) {
            AssertEqual(true, policy.Rules.ContainsKey(ruleId), $"powershell-50 resolves declared rule: {ruleId}");
        }
        foreach (var ruleId in allRuleIds) {
            AssertEqual(true, declaredRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase),
                $"powershell-50 does not resolve any unexpected rules: {ruleId}");
        }

        var psRuleIds = policy.SelectByLanguage("powershell").Select(r => r.Rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEqual(allRuleIds.Length, psRuleIds.Length, "powershell-50 contains only PowerShell rules");

        foreach (var ruleId in allRuleIds) {
            AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"powershell-50 rule exists: {ruleId}");
        }
    }
}
#endif
