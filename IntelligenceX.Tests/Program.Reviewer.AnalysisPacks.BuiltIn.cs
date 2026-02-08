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
}
#endif
