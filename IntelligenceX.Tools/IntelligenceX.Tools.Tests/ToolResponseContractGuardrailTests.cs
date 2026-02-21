using System.Reflection;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Email;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.OfficeIMO;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.ReviewerSetup;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolResponseContractGuardrailTests {
    private static readonly HashSet<string> AllowedRiskyResponseProperties = new(StringComparer.Ordinal) {
        // Dynamic "receipt step output" contracts currently used by discovery tools.
        "IntelligenceX.Tools.ADPlayground.AdForestDiscoverTool+DiscoveryStep.Output",
        "IntelligenceX.Tools.ADPlayground.AdScopeDiscoveryTool+ScopeDiscoveryStep.Output",

        // Existing dynamic payload contracts kept for backward compatibility.
        "IntelligenceX.Tools.ADPlayground.AdKdcProxyPolicyTool+AdKdcProxyPolicyResult.Values",
        "IntelligenceX.Tools.EventLog.EventLogEvtxSecuritySummaryTool+SecuritySummaryEnvelope.Report",
        "IntelligenceX.Tools.EventLog.EventLogNamedEventsQueryTool+NamedEventsQueryRow.Payload",
        "IntelligenceX.Tools.TestimoX.TestimoXRulesRunTool+TestimoRuleRunRow.ResultRows",

        // Pack guidance remains intentionally open-ended.
        "IntelligenceX.Tools.Common.ToolPackInfoModel.SetupHints",
        "IntelligenceX.Tools.Common.ToolPackInfoModel.Safety",
        "IntelligenceX.Tools.Common.ToolPackInfoModel.Limits"
    };

    [Fact]
    public void ResponseContracts_ShouldNotIntroduceNewObjectLikeProperties() {
        var discovered = DiscoverRiskyResponseProperties();
        var unexpected = discovered
            .Where(static signature => !AllowedRiskyResponseProperties.Contains(signature))
            .OrderBy(static signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "New risky response-model property/properties detected. Add typed DTOs or explicitly allowlist intentional dynamic fields:\n" +
            string.Join(Environment.NewLine, unexpected));
    }

    private static IReadOnlyList<string> DiscoverRiskyResponseProperties() {
        var assemblies = new[] {
            typeof(AdPackInfoTool).Assembly,
            typeof(EventLogPackInfoTool).Assembly,
            typeof(OfficeImoPackInfoTool).Assembly,
            typeof(SystemPackInfoTool).Assembly,
            typeof(FileSystemPackInfoTool).Assembly,
            typeof(EmailPackInfoTool).Assembly,
            typeof(PowerShellPackInfoTool).Assembly,
            typeof(TestimoXPackInfoTool).Assembly,
            typeof(ReviewerSetupPackInfoTool).Assembly,
            typeof(ToolResponse).Assembly
        };

        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies.Distinct()) {
            foreach (var type in GetLoadableTypes(assembly)) {
                if (!LooksLikeResponseContractType(type)) {
                    continue;
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (property.GetIndexParameters().Length != 0) {
                        continue;
                    }

                    if (!ContainsObjectLikeType(property.PropertyType)) {
                        continue;
                    }

                    signatures.Add($"{type.FullName}.{property.Name}");
                }
            }
        }

        return signatures.OrderBy(static signature => signature, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly) {
        try {
            return assembly.GetTypes();
        } catch (ReflectionTypeLoadException ex) {
            return ex.Types.Where(static type => type is not null)!;
        }
    }

    private static bool LooksLikeResponseContractType(Type type) {
        if (string.IsNullOrWhiteSpace(type.FullName) || string.IsNullOrWhiteSpace(type.Namespace)) {
            return false;
        }
        if (!type.Namespace.StartsWith("IntelligenceX.Tools", StringComparison.Ordinal)) {
            return false;
        }
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters) {
            return false;
        }
        if (typeof(Delegate).IsAssignableFrom(type)) {
            return false;
        }

        var name = type.Name;
        return name.Contains("Result", StringComparison.Ordinal) ||
               name.Contains("Response", StringComparison.Ordinal) ||
               name.Contains("Row", StringComparison.Ordinal) ||
               name.Contains("Envelope", StringComparison.Ordinal) ||
               name.Contains("Chunk", StringComparison.Ordinal) ||
               name.Contains("Step", StringComparison.Ordinal) ||
               name.Contains("Model", StringComparison.Ordinal);
    }

    private static bool ContainsObjectLikeType(Type type) {
        if (type == typeof(object)) {
            return true;
        }
        if (type.IsArray) {
            return ContainsObjectLikeType(type.GetElementType()!);
        }
        if (type.IsGenericType) {
            foreach (var genericArgument in type.GetGenericArguments()) {
                if (ContainsObjectLikeType(genericArgument)) {
                    return true;
                }
            }
        }

        return false;
    }
}
