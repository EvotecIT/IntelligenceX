using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdReplicationStatusToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdReplicationStatusTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = BindRequestMethod.Invoke(null, new object?[] { null });
        var request = AssertValidBindingAndGetRequest(binding);

        Assert.Empty(GetRequestProperty<IReadOnlyList<string>>(request, "RequestedComputerNames"));
        Assert.False(GetRequestProperty<bool>(request, "HealthOnly"));
    }

    [Fact]
    public void BindRequest_NormalizesAndDeduplicatesRequestedComputerNames() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("computer_names", new JsonArray()
                    .Add(" dc1.ad.evotec.xyz ")
                    .Add("DC1.AD.EVOTEC.XYZ")
                    .Add(string.Empty)
                    .Add("dc2.ad.evotec.xyz"))
                .Add("health_only", true)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal(
            new[] { "dc1.ad.evotec.xyz", "dc2.ad.evotec.xyz" },
            GetRequestProperty<IReadOnlyList<string>>(request, "RequestedComputerNames"));
        Assert.True(GetRequestProperty<bool>(request, "HealthOnly"));
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
        return Assert.IsAssignableFrom<T>(request.GetType().GetProperty(propertyName)?.GetValue(request));
    }
}
