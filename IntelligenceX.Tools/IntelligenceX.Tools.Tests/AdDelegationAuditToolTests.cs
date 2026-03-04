using System;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDelegationAuditToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdDelegationAuditTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_NormalizesKindAndUsesDefaultMaxValuesForNonPositiveInput() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("kind", " COMPUTER ")
                .Add("enabled_only", true)
                .Add("include_spns", true)
                .Add("include_allowed_to_delegate_to", true)
                .Add("max_values_per_attribute", 0)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal("computer", GetRequestProperty<string>(request, "Kind"));
        Assert.True(GetRequestProperty<bool>(request, "EnabledOnly"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeSpns"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeAllowedToDelegateTo"));
        Assert.Equal(50, GetRequestProperty<int>(request, "MaxValuesPerAttribute"));
    }

    [Fact]
    public void BindRequest_CapsMaxValuesPerAttributeToSafetyLimit() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("max_values_per_attribute", 9999)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal(200, GetRequestProperty<int>(request, "MaxValuesPerAttribute"));
    }

    private static object AssertValidBindingAndGetRequest(object? binding) {
        Assert.NotNull(binding);
        var bindingType = binding!.GetType();
        var isValid = Assert.IsType<bool>(bindingType.GetProperty("IsValid")?.GetValue(binding));
        Assert.True(isValid);
        return bindingType.GetProperty("Request")?.GetValue(binding)
               ?? throw new InvalidOperationException("Binding request value is null.");
    }

    private static T GetRequestProperty<T>(object request, string propertyName) {
        return Assert.IsType<T>(request.GetType().GetProperty(propertyName)?.GetValue(request));
    }
}
