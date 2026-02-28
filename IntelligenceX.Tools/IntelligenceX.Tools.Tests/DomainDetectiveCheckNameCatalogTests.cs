using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DomainDetectiveCheckNameCatalogTests {
    private static readonly Type CatalogType =
        typeof(DomainDetectiveChecksCatalogTool).Assembly.GetType("IntelligenceX.Tools.DomainDetective.DomainDetectiveCheckNameCatalog", throwOnError: true)
        ?? throw new InvalidOperationException("DomainDetectiveCheckNameCatalog type was not found.");

    private static readonly PropertyInfo DefaultChecksProperty =
        CatalogType.GetProperty("DefaultChecks", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("DefaultChecks property was not found.");

    private static readonly PropertyInfo AliasByTokenProperty =
        CatalogType.GetProperty("AliasByToken", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AliasByToken property was not found.");

    [Fact]
    public void DefaultChecks_ShouldExposeReadOnlyList() {
        var checks = Assert.IsAssignableFrom<IList<string>>(DefaultChecksProperty.GetValue(null));
        var snapshot = checks.ToArray();

        Assert.Throws<NotSupportedException>(() => checks.Add("NEWCHECK"));
        Assert.Throws<NotSupportedException>(() => checks[0] = "MUTATED");
        Assert.Equal(snapshot, checks.ToArray());
    }

    [Fact]
    public void AliasByToken_ShouldExposeReadOnlyDictionary() {
        var aliases = Assert.IsAssignableFrom<IDictionary<string, string>>(AliasByTokenProperty.GetValue(null));
        var snapshot = aliases.ToArray();

        Assert.Throws<NotSupportedException>(() => aliases["NAMESERVERS"] = "MUTATED");
        Assert.Throws<NotSupportedException>(() => aliases.Add("NEWALIAS", "NS"));
        Assert.Equal(snapshot, aliases.ToArray());
    }
}
