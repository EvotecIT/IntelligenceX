using System;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdGpoBlockedInheritanceToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdGpoBlockedInheritanceTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = BindRequestMethod.Invoke(null, new object?[] { null });
        var request = AssertValidBindingAndGetRequest(binding);

        Assert.True(GetRequestProperty<bool>(request, "OnlyBlocked"));
        Assert.Equal(200000, GetRequestProperty<int>(request, "MaxRows"));
    }

    [Fact]
    public void BindRequest_AllowsExplicitOnlyBlockedFalseAndClampsNonPositiveMaxRows() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("only_blocked", false)
                .Add("max_rows", 0)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.False(GetRequestProperty<bool>(request, "OnlyBlocked"));
        Assert.Equal(1, GetRequestProperty<int>(request, "MaxRows"));
    }

    [Fact]
    public void BindRequest_CapsMaxRowsToSafetyLimit() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("max_rows", 999999)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal(500000, GetRequestProperty<int>(request, "MaxRows"));
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
