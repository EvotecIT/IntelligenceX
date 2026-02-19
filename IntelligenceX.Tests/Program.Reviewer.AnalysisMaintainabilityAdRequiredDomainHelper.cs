namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalAdRequiredDomainHelperRuleFlagsMissingCanonicalHelpers() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-ad-domain-helper-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);
            Directory.CreateDirectory(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.ADPlayground"));

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.ADPlayground",
                "SampleBadAdDomainTool.cs"), """
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed class SampleBadAdDomainTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_bad_ad_domain_tool",
        "Sample AD tool with required domain but non-canonical parsing.",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return.")))
            .Required("domain_name")
            .NoAdditionalProperties());

    public SampleBadAdDomainTool(ActiveDirectoryToolOptions options) : base(options) { }

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var maxResults = ResolveBoundedMaxResults(arguments);
        return Task.FromResult(ToolResponse.Ok(new {
            domainName,
            maxResults
        }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run AD required-domain helper missing canonical path exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL002", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.ADPlayground/SampleBadAdDomainTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run AD required-domain helper missing canonical path finding");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalAdRequiredDomainHelperRuleAcceptsCanonicalHelpers() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-ad-domain-helper-good-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);
            Directory.CreateDirectory(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.ADPlayground"));

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.ADPlayground",
                "SampleGoodAdDomainTool.cs"), """
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed class SampleGoodAdDomainTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_good_ad_domain_tool",
        "Sample AD tool with required domain using canonical helper.",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return.")))
            .Required("domain_name")
            .NoAdditionalProperties());

    public SampleGoodAdDomainTool(ActiveDirectoryToolOptions options) : base(options) { }

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        if (!TryReadRequiredDomainQueryRequest(arguments, out var request, out var errorResponse)) {
            return Task.FromResult(errorResponse!);
        }

        return Task.FromResult(ToolResponse.Ok(new {
            request.DomainName,
            request.MaxResults
        }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run AD required-domain helper canonical path exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL002", StringComparison.OrdinalIgnoreCase)),
                "analyze run AD required-domain helper canonical path no finding");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif
