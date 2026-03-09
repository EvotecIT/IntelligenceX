using System;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdReplicationSummaryToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdReplicationSummaryTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_ReadsForestNameAlongsideDomainScopeOptions() {
        var tool = new AdReplicationSummaryTool(new ActiveDirectoryToolOptions());
        var binding = BindRequestMethod.Invoke(tool, new object?[] {
            new JsonObject()
                .Add("domain_controller", " ad1.ad.evotec.xyz ")
                .Add("domain_name", " child.ad.evotec.xyz ")
                .Add("forest_name", " ad.evotec.xyz ")
                .Add("include_details", true)
                .Add("max_domain_controllers", 123)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal("ad1.ad.evotec.xyz", GetRequestProperty<string>(request, "DomainController"));
        Assert.Equal("child.ad.evotec.xyz", GetRequestProperty<string>(request, "DomainName"));
        Assert.Equal("ad.evotec.xyz", GetRequestProperty<string>(request, "ForestName"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeDetails"));
        Assert.Equal(123, GetRequestProperty<int>(request, "MaxDomainControllers"));
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
