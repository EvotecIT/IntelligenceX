using System;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class AdProjectionArgumentSanitizerTests {
    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenColumnsUnsupported_StripsProjectionArgumentsOnly() {
        var arguments = new JsonObject()
            .Add("domain_name", "ad.evotec.xyz")
            .Add("columns", new JsonArray().Add("dns_host_name"))
            .Add("sort_by", "dns_host_name")
            .Add("sort_direction", "desc")
            .Add("top", 10);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: new[] { "domain_name", "domain_controller", "site" });

        Assert.NotNull(shaped);
        Assert.True(HasKey(shaped!, "domain_name"));
        Assert.False(HasKey(shaped, "columns"));
        Assert.False(HasKey(shaped, "sort_by"));
        Assert.False(HasKey(shaped, "sort_direction"));
        Assert.False(HasKey(shaped, "top"));
    }

    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenProjectionIsSupported_KeepsOriginalObject() {
        var arguments = new JsonObject()
            .Add("columns", new JsonArray().Add("domain-controller"))
            .Add("sort_by", "domain_controller")
            .Add("sort_direction", "asc")
            .Add("top", 5);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: new[] { "domain_controller", "site" });

        Assert.Same(arguments, shaped);
    }

    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenSortByUnsupported_StripsProjectionArguments() {
        var arguments = new JsonObject()
            .Add("domain_name", "ad.evotec.xyz")
            .Add("sort_by", "dns_host_name")
            .Add("sort_direction", "asc")
            .Add("top", 25);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: Array.Empty<string>());

        Assert.NotNull(shaped);
        Assert.True(HasKey(shaped!, "domain_name"));
        Assert.False(HasKey(shaped, "sort_by"));
        Assert.False(HasKey(shaped, "sort_direction"));
        Assert.False(HasKey(shaped, "top"));
    }

    private static bool HasKey(JsonObject value, string key) {
        foreach (var pair in value) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
