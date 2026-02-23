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
    private static readonly HashSet<string> AllowedDynamicObjectResponseProperties = new(StringComparer.Ordinal) {
        // Dynamic "receipt step output" contracts currently used by discovery tools.
        "IntelligenceX.Tools.ADPlayground.AdForestDiscoverTool+DiscoveryStep.Output",
        "IntelligenceX.Tools.ADPlayground.AdScopeDiscoveryTool+ScopeDiscoveryStep.Output",

        // Existing dynamic payload contracts kept for backward compatibility.
        "IntelligenceX.Tools.ADPlayground.AdKdcProxyPolicyTool+AdKdcProxyPolicyResult.Values",
        "IntelligenceX.Tools.EventLog.EventLogEvtxSecuritySummaryTool+SecuritySummaryEnvelope.Report",
        "IntelligenceX.Tools.EventLog.EventLogNamedEventsQueryTool+NamedEventsQueryRow.Payload",
        "IntelligenceX.Tools.TestimoX.TestimoXRulesRunTool+TestimoRuleRunRow.ResultRows"
    };

    private static readonly HashSet<string> AllowedDynamicObjectResponseTypes = new(StringComparer.Ordinal) {
        // Pack guidance intentionally keeps these optional hints open-ended.
        "IntelligenceX.Tools.Common.ToolPackInfoModel"
    };

    private static readonly HashSet<string> AllowedUnsafeDictionaryResponseProperties = new(StringComparer.Ordinal) {
        // Structured next-action arguments intentionally allow typed dynamic values.
        "IntelligenceX.Tools.Common.ToolNextActionModel.Arguments",

        // Existing dynamic payload contracts kept for backward compatibility.
        "IntelligenceX.Tools.EventLog.EventLogNamedEventsQueryTool+NamedEventsQueryRow.Payload"
    };

    [Fact]
    public void ResponseContracts_ShouldNotIntroduceNewObjectLikeProperties() {
        var discovered = DiscoverObjectLikeResponseProperties();
        var unexpected = discovered
            .Where(static signature => !AllowedDynamicObjectResponseProperties.Contains(signature))
            .OrderBy(static signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "New risky response-model property/properties detected. Add typed DTOs or explicitly allowlist intentional dynamic fields:\n" +
            string.Join(Environment.NewLine, unexpected));
    }

    [Fact]
    public void ResponseContracts_ShouldNotIntroduceUnsafeDictionaryKeyValueProperties() {
        var discovered = DiscoverUnsafeDictionaryResponseProperties();
        var unexpected = discovered
            .Where(static signature => !AllowedUnsafeDictionaryResponseProperties.Contains(signature))
            .OrderBy(static signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "New unsafe dictionary response contract(s) detected. Prefer string/enum keys and typed values, or explicitly allowlist intentional dynamic fields:\n" +
            string.Join(Environment.NewLine, unexpected));
    }

    private static IReadOnlyList<string> DiscoverObjectLikeResponseProperties() {
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
                if (AllowedDynamicObjectResponseTypes.Contains(type.FullName!)) {
                    continue;
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (property.GetIndexParameters().Length != 0) {
                        continue;
                    }

                    if (!ContainsObjectLikeTypeOutsideDictionary(property.PropertyType)) {
                        continue;
                    }

                    signatures.Add($"{type.FullName}.{property.Name}");
                }
            }
        }

        return signatures.OrderBy(static signature => signature, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> DiscoverUnsafeDictionaryResponseProperties() {
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
                if (AllowedDynamicObjectResponseTypes.Contains(type.FullName!)) {
                    continue;
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (property.GetIndexParameters().Length != 0) {
                        continue;
                    }

                    if (!ContainsUnsafeDictionaryType(property.PropertyType)) {
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

    private static bool ContainsObjectLikeTypeOutsideDictionary(Type type) {
        if (TryGetDictionaryTypeArguments(type, out _, out _)) {
            return false;
        }

        if (type.IsArray) {
            return ContainsObjectLikeTypeOutsideDictionary(type.GetElementType()!);
        }

        if (type.IsGenericType) {
            foreach (var genericArgument in type.GetGenericArguments()) {
                if (ContainsObjectLikeTypeOutsideDictionary(genericArgument)) {
                    return true;
                }
            }
        }

        return type == typeof(object);
    }

    private static bool ContainsUnsafeDictionaryType(Type type) {
        var visited = new HashSet<Type>();
        return ContainsUnsafeDictionaryTypeCore(type, visited);
    }

    private static bool ContainsUnsafeDictionaryTypeCore(Type type, ISet<Type> visited) {
        if (!visited.Add(type)) {
            return false;
        }

        if (TryGetDictionaryTypeArguments(type, out var keyType, out var valueType)) {
            if (!IsSupportedDictionaryKeyType(keyType)) {
                return true;
            }

            if (ContainsObjectLikeType(valueType)) {
                return true;
            }

            return ContainsUnsafeDictionaryTypeCore(valueType, visited);
        }

        if (type.IsArray) {
            return ContainsUnsafeDictionaryTypeCore(type.GetElementType()!, visited);
        }

        if (type.IsGenericType) {
            foreach (var genericArgument in type.GetGenericArguments()) {
                if (ContainsUnsafeDictionaryTypeCore(genericArgument, visited)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetDictionaryTypeArguments(Type type, out Type keyType, out Type valueType) {
        if (type.IsGenericType) {
            var genericTypeDefinition = type.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(IDictionary<,>) ||
                genericTypeDefinition == typeof(IReadOnlyDictionary<,>) ||
                genericTypeDefinition == typeof(Dictionary<,>)) {
                var args = type.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        var dictionaryInterface = type.GetInterfaces()
            .FirstOrDefault(static iface =>
                iface.IsGenericType &&
                (iface.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 iface.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (dictionaryInterface is not null) {
            var args = dictionaryInterface.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];
            return true;
        }

        keyType = typeof(void);
        valueType = typeof(void);
        return false;
    }

    private static bool IsSupportedDictionaryKeyType(Type keyType) {
        var normalized = Nullable.GetUnderlyingType(keyType) ?? keyType;
        if (normalized == typeof(string) ||
            normalized == typeof(bool) ||
            normalized == typeof(byte) ||
            normalized == typeof(sbyte) ||
            normalized == typeof(short) ||
            normalized == typeof(ushort) ||
            normalized == typeof(int) ||
            normalized == typeof(uint) ||
            normalized == typeof(long) ||
            normalized == typeof(ulong) ||
            normalized == typeof(float) ||
            normalized == typeof(double) ||
            normalized == typeof(decimal) ||
            normalized == typeof(Guid) ||
            normalized == typeof(DateTime) ||
            normalized == typeof(DateTimeOffset) ||
            normalized == typeof(TimeSpan)) {
            return true;
        }

        return normalized.IsEnum;
    }
}
