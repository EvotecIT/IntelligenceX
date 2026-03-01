using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

/// <summary>
/// Guards fallback and DN helpers used by ad_dc_fleet_posture tool output shaping.
/// </summary>
public sealed class AdDcFleetPostureToolTests {
    /// <summary>
    /// Ensures resolver prefers discovered DC facts when reported count is zero.
    /// </summary>
    [Fact]
    public void ResolveDomainControllerCount_PrefersDiscoveredCountWhenReportedIsZero() {
        var resolved = AdDcFleetPostureTool.ResolveDomainControllerCount(reportedCount: 0, discoveredFactsCount: 3);
        Assert.Equal(3, resolved);
    }

    /// <summary>
    /// Ensures resolver never decreases a non-zero reported count.
    /// </summary>
    [Fact]
    public void ResolveDomainControllerCount_PreservesHigherReportedCount() {
        var resolved = AdDcFleetPostureTool.ResolveDomainControllerCount(reportedCount: 5, discoveredFactsCount: 3);
        Assert.Equal(5, resolved);
    }

    /// <summary>
    /// Ensures helper builds Domain Controllers container DN from a resolved domain DN.
    /// </summary>
    [Fact]
    public void TryBuildDomainControllersDn_BuildsExpectedContainerDn() {
        var dn = AdDcFleetPostureTool.TryBuildDomainControllersDn("DC=ad,DC=evotec,DC=xyz");
        Assert.Equal("CN=Domain Controllers,DC=ad,DC=evotec,DC=xyz", dn);
    }

    /// <summary>
    /// Ensures helper can still derive legacy OU path when needed for compatibility.
    /// </summary>
    [Fact]
    public void TryBuildLegacyDomainControllersDn_BuildsExpectedOuDn() {
        var dn = AdDcFleetPostureTool.TryBuildLegacyDomainControllersDn("DC=ad,DC=evotec,DC=xyz");
        Assert.Equal("OU=Domain Controllers,DC=ad,DC=evotec,DC=xyz", dn);
    }

    /// <summary>
    /// Ensures helper returns null when domain DN input is missing.
    /// </summary>
    [Fact]
    public void TryBuildDomainControllersDn_ReturnsNullForMissingDomainDn() {
        var dn = AdDcFleetPostureTool.TryBuildDomainControllersDn(" ");
        Assert.Null(dn);
    }
}
