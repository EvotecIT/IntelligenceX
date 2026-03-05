using System;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDnsZoneConfigToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdDnsZoneConfigTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_RequiresDnsServer() {
        var binding = BindRequestMethod.Invoke(null, new object?[] { new JsonObject() });

        Assert.NotNull(binding);
        var bindingType = binding!.GetType();
        var isValid = Assert.IsType<bool>(bindingType.GetProperty("IsValid")?.GetValue(binding));
        Assert.False(isValid);
        Assert.Equal("invalid_argument", Assert.IsType<string>(bindingType.GetProperty("ErrorCode")?.GetValue(binding)));
        Assert.Equal("dns_server is required.", Assert.IsType<string>(bindingType.GetProperty("Error")?.GetValue(binding)));
    }

    [Fact]
    public void BindRequest_NormalizesInputsAndUsesExpectedDefaults() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("dns_server", " dns-01.ad.evotec.xyz ")
                .Add("zone_name_contains", " corp ")
                .Add("dynamic_updates_only", true)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal("dns-01.ad.evotec.xyz", GetRequestProperty<string>(request, "DnsServer"));
        Assert.Equal("corp", GetRequestProperty<string>(request, "ZoneNameContains"));
        Assert.True(GetRequestProperty<bool>(request, "DynamicUpdatesOnly"));
        Assert.False(GetRequestProperty<bool>(request, "InsecureUpdatesOnly"));
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
