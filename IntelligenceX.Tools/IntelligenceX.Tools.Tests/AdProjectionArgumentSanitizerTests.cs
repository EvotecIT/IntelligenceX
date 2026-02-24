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

    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenAdminCountProjectionUnsupported_StripsProjectionArguments() {
        var arguments = new JsonObject()
            .Add("sam_account_name_contains", "przemyslaw.klys")
            .Add("columns", new JsonArray().Add("display_name"))
            .Add("sort_by", "display_name")
            .Add("sort_direction", "asc")
            .Add("top", 20);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: new[] {
                "forest_name",
                "domain_name",
                "sam_account_name",
                "last_logon",
                "last_logon_timestamp",
                "never_logged_on",
                "days_since_last_logon"
            });

        Assert.NotNull(shaped);
        Assert.True(HasKey(shaped!, "sam_account_name_contains"));
        Assert.False(HasKey(shaped, "columns"));
        Assert.False(HasKey(shaped, "sort_by"));
        Assert.False(HasKey(shaped, "sort_direction"));
        Assert.False(HasKey(shaped, "top"));
    }

    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenReplicationStatusColumnsContainLegacyAliases_StripsProjectionArguments() {
        var arguments = new JsonObject()
            .Add("computer_names", new JsonArray().Add("AD0.ad.evotec.xyz"))
            .Add("columns", new JsonArray().Add("server").Add("partner").Add("status"))
            .Add("sort_by", "last_success")
            .Add("sort_direction", "desc")
            .Add("top", 200);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: new[] {
                "server",
                "source_dsa",
                "destination_dsa",
                "transport_type",
                "last_successful_sync",
                "last_failure_time",
                "status",
                "failure_message"
            });

        Assert.NotNull(shaped);
        Assert.True(HasKey(shaped!, "computer_names"));
        Assert.False(HasKey(shaped, "columns"));
        Assert.False(HasKey(shaped, "sort_by"));
        Assert.False(HasKey(shaped, "sort_direction"));
        Assert.False(HasKey(shaped, "top"));
    }

    [Fact]
    public void RemoveUnsupportedProjectionArguments_WhenReplicationSummaryColumnsContainUnsupportedScope_StripsProjectionArguments() {
        var arguments = new JsonObject()
            .Add("stale_threshold_hours", 24)
            .Add("columns", new JsonArray().Add("scope").Add("server"))
            .Add("sort_by", "largest_delta_hours")
            .Add("sort_direction", "desc")
            .Add("top", 100);

        var shaped = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            arguments,
            availableColumns: new[] {
                "server",
                "fails",
                "total",
                "percentage_error",
                "largest_delta",
                "replication_error"
            });

        Assert.NotNull(shaped);
        Assert.True(HasKey(shaped!, "stale_threshold_hours"));
        Assert.False(HasKey(shaped, "columns"));
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
