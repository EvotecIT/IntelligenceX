using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDnsServerConfigToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdDnsServerConfigTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = BindRequestMethod.Invoke(null, new object?[] { null });
        var request = AssertValidBindingAndGetRequest(binding);

        Assert.Empty(GetRequestProperty<IReadOnlyList<string>>(request, "ExplicitServers"));
        Assert.Null(request.GetType().GetProperty("DomainName")?.GetValue(request));
        Assert.Null(request.GetType().GetProperty("ForestName")?.GetValue(request));
        Assert.False(GetRequestProperty<bool>(request, "RecursionDisabledOnly"));
        Assert.False(GetRequestProperty<bool>(request, "MissingForwardersOnly"));
        Assert.Equal(200, GetRequestProperty<int>(request, "MaxServers"));
    }

    [Fact]
    public void BindRequest_NormalizesDistinctServersAndClampsNonPositiveMaxServers() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("dns_servers", new JsonArray()
                    .Add(" dns-01.ad.evotec.xyz ")
                    .Add("DNS-01.AD.EVOTEC.XYZ")
                    .Add(string.Empty)
                    .Add("dns-02.ad.evotec.xyz"))
                .Add("domain_name", "  ad.evotec.xyz ")
                .Add("forest_name", " evotec.xyz ")
                .Add("recursion_disabled_only", true)
                .Add("missing_forwarders_only", true)
                .Add("max_servers", 0)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal(
            new[] { "dns-01.ad.evotec.xyz", "dns-02.ad.evotec.xyz" },
            GetRequestProperty<IReadOnlyList<string>>(request, "ExplicitServers"));
        Assert.Equal("ad.evotec.xyz", GetRequestProperty<string>(request, "DomainName"));
        Assert.Equal("evotec.xyz", GetRequestProperty<string>(request, "ForestName"));
        Assert.True(GetRequestProperty<bool>(request, "RecursionDisabledOnly"));
        Assert.True(GetRequestProperty<bool>(request, "MissingForwardersOnly"));
        Assert.Equal(1, GetRequestProperty<int>(request, "MaxServers"));
    }

    [Fact]
    public void BindRequest_CapsMaxServersToSafetyLimit() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("max_servers", 50_000)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal(5000, GetRequestProperty<int>(request, "MaxServers"));
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
