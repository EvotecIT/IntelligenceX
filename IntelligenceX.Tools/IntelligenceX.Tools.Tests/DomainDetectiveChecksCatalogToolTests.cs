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
        var checkRows = root.GetProperty("check_rows").EnumerateArray().ToArray();
        Assert.Equal(supportedChecks.Length, checkRows.Length);
        Assert.Contains(checkRows, static node => node.GetProperty("is_default").GetBoolean());

        var renderHints = root.GetProperty("render").EnumerateArray().ToArray();
        Assert.NotEmpty(renderHints);

        var primaryTable = renderHints[0];
        Assert.Equal("table", primaryTable.GetProperty("kind").GetString());
        Assert.Equal("check_rows", primaryTable.GetProperty("rows_path").GetString());
        var primaryColumns = primaryTable.GetProperty("columns")
            .EnumerateArray()
            .Select(static node => node.GetProperty("key").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
        Assert.NotEmpty(primaryColumns);

        foreach (var key in primaryColumns) {
            Assert.True(checkRows[0].TryGetProperty(key, out _), $"Missing check_rows property for column key '{key}'.");
        }

        var aliasTable = renderHints.Single(static hint =>
            string.Equals(hint.GetProperty("rows_path").GetString(), "aliases", StringComparison.OrdinalIgnoreCase));
        var aliasColumns = aliasTable.GetProperty("columns")
            .EnumerateArray()
            .Select(static node => node.GetProperty("key").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
        Assert.NotEmpty(aliasColumns);

        foreach (var key in aliasColumns) {
            Assert.True(aliases[0].TryGetProperty(key, out _), $"Missing aliases property for column key '{key}'.");
        }

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

        var checkRows = root.GetProperty("check_rows").EnumerateArray().ToArray();
        Assert.NotEmpty(checkRows);
        Assert.DoesNotContain(checkRows, static node => node.GetProperty("is_default").GetBoolean());

        var renderHints = root.GetProperty("render").EnumerateArray().ToArray();
        Assert.Single(renderHints);
        Assert.Equal("table", renderHints[0].GetProperty("kind").GetString());
        Assert.Equal("check_rows", renderHints[0].GetProperty("rows_path").GetString());
    }
}
