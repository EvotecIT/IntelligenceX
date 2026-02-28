using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DomainDetectiveChecksCatalogToolTests {
    [Fact]
    public async Task InvokeAsync_ReturnsSupportedChecksDefaultsAndAliases() {
        var options = new DomainDetectiveToolOptions();
        var tool = new DomainDetectiveChecksCatalogTool(options);

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(options.MaxChecks, root.GetProperty("max_checks_per_run").GetInt32());

        var supportedChecks = root.GetProperty("supported_checks")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
        Assert.NotEmpty(supportedChecks);
        Assert.Contains("DNSHEALTH", supportedChecks, StringComparer.OrdinalIgnoreCase);

        var defaultChecks = root.GetProperty("default_checks")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
        Assert.Contains("SPF", defaultChecks, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DMARC", defaultChecks, StringComparer.OrdinalIgnoreCase);

        var aliases = root.GetProperty("aliases").EnumerateArray().ToArray();
        Assert.NotEmpty(aliases);
        Assert.Contains(
            aliases,
            static node => string.Equals(node.GetProperty("token").GetString(), "NAMESERVERS", StringComparison.OrdinalIgnoreCase)
                && string.Equals(node.GetProperty("canonical").GetString(), "NS", StringComparison.OrdinalIgnoreCase));

        var normalizationRules = root.GetProperty("normalization_rules")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
        Assert.NotEmpty(normalizationRules);
    }

    [Fact]
    public async Task InvokeAsync_AllowsAliasAndDefaultSuppressionFlags() {
        var tool = new DomainDetectiveChecksCatalogTool(new DomainDetectiveToolOptions());
        var arguments = new JsonObject()
            .Add("include_aliases", false)
            .Add("include_default_checks", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("aliases").GetArrayLength());
        Assert.Equal(0, root.GetProperty("default_checks").GetArrayLength());
        Assert.True(root.GetProperty("supported_checks").GetArrayLength() > 0);
    }
}
