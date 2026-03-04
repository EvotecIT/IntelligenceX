using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDirectoryDiscoveryDiagnosticsToolTests {
    private static readonly MethodInfo BindRequestMethod =
        typeof(AdDirectoryDiscoveryDiagnosticsTool).GetMethod("BindRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BindRequest not found.");

    [Fact]
    public void BindRequest_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = BindRequestMethod.Invoke(null, new object?[] { null });
        var request = AssertValidBindingAndGetRequest(binding);

        Assert.Null(request.GetType().GetProperty("ForestName")?.GetValue(request));
        var domains = GetRequestProperty<IReadOnlyList<string>>(request, "Domains");
        Assert.Empty(domains);
        Assert.Equal(5000, GetRequestProperty<int>(request, "MaxIssues"));
        Assert.Equal(1500, GetRequestProperty<int>(request, "DnsResolveTimeoutMs"));
        Assert.Equal(3000, GetRequestProperty<int>(request, "LdapTimeoutMs"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeDnsSrvComparison"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeHostResolution"));
        Assert.True(GetRequestProperty<bool>(request, "IncludeDirectoryTopology"));
        Assert.False(GetRequestProperty<bool>(request, "AsIssue"));
    }

    [Fact]
    public void BindRequest_NormalizesDomainsAndClampsBoundedValues() {
        var binding = BindRequestMethod.Invoke(null, new object?[] {
            new JsonObject()
                .Add("forest_name", "  ad.evotec.xyz  ")
                .Add("domains", new JsonArray()
                    .Add(" ad.evotec.xyz ")
                    .Add("ad.evotec.xyz")
                    .Add("child.ad.evotec.xyz"))
                .Add("max_issues", 0)
                .Add("dns_resolve_timeout_ms", 999_999)
                .Add("ldap_timeout_ms", -1)
                .Add("include_dns_srv_comparison", false)
                .Add("include_host_resolution", false)
                .Add("include_directory_topology", false)
                .Add("as_issue", true)
        });

        var request = AssertValidBindingAndGetRequest(binding);
        Assert.Equal("ad.evotec.xyz", GetRequestProperty<string>(request, "ForestName"));
        Assert.Equal(
            new[] { "ad.evotec.xyz", "child.ad.evotec.xyz" },
            GetRequestProperty<IReadOnlyList<string>>(request, "Domains"));
        Assert.Equal(1, GetRequestProperty<int>(request, "MaxIssues"));
        Assert.Equal(120000, GetRequestProperty<int>(request, "DnsResolveTimeoutMs"));
        Assert.Equal(200, GetRequestProperty<int>(request, "LdapTimeoutMs"));
        Assert.False(GetRequestProperty<bool>(request, "IncludeDnsSrvComparison"));
        Assert.False(GetRequestProperty<bool>(request, "IncludeHostResolution"));
        Assert.False(GetRequestProperty<bool>(request, "IncludeDirectoryTopology"));
        Assert.True(GetRequestProperty<bool>(request, "AsIssue"));
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
