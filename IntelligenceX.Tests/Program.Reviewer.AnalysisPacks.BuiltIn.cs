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

        var allSecurity = catalog.Packs["all-security-default"];
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("powershell-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes powershell-security-default");
        AssertEqual(true, allSecurity.Includes.Any(id => id.Equals("csharp-security-default", StringComparison.OrdinalIgnoreCase)),
            "all-security-default includes csharp-security-default");

        var psSecurity = catalog.Packs["powershell-security-default"];
        foreach (var ruleId in psSecurity.Rules) {
            AssertEqual(true, catalog.TryGetRule(ruleId, out _), $"powershell-security-default rule exists: {ruleId}");
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
