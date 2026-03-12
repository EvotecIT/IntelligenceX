using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupUnavailablePackWarningFormatterTests {
    [Fact]
    public void BuildEntries_DeduplicatesAliasAndCanonicalPackIds() {
        var entries = StartupUnavailablePackWarningFormatter.BuildEntries(
            new[] {
                new TestPack("ADPlayground", "Active Directory", Enabled: false, DisabledReason: "Missing required module."),
                new TestPack("active_directory", "Active Directory", Enabled: false, DisabledReason: "Missing required module."),
                new TestPack("ComputerX", "System", Enabled: false, DisabledReason: "Remote endpoint unavailable.")
            },
            static pack => pack.Id,
            static pack => pack.Name,
            static pack => pack.Enabled,
            static pack => pack.DisabledReason);

        Assert.Collection(
            entries,
            static entry => {
                Assert.Equal("active_directory", entry.Id);
                Assert.Equal("Active Directory", entry.Label);
                Assert.Equal("Missing required module.", entry.Reason);
            },
            static entry => {
                Assert.Equal("system", entry.Id);
                Assert.Equal("System", entry.Label);
                Assert.Equal("Remote endpoint unavailable.", entry.Reason);
            });
    }

    [Fact]
    public void BuildEntries_HumanizesCanonicalFallbackLabelWhenNameMissing() {
        var entries = StartupUnavailablePackWarningFormatter.BuildEntries(
            new[] {
                new TestPack("ADPlayground", string.Empty, Enabled: false, DisabledReason: "Startup prerequisites missing.")
            },
            static pack => pack.Id,
            static pack => pack.Name,
            static pack => pack.Enabled,
            static pack => pack.DisabledReason);

        var entry = Assert.Single(entries);
        Assert.Equal("active_directory", entry.Id);
        Assert.Equal("Active Directory", entry.Label);
        Assert.Equal("Startup prerequisites missing.", entry.Reason);
    }

    [Fact]
    public void BuildEntries_IgnoresEmptyDisabledReason() {
        var entries = StartupUnavailablePackWarningFormatter.BuildEntries(
            new[] {
                new TestPack("ADPlayground", "Active Directory", Enabled: false, DisabledReason: " "),
                new TestPack("active_directory", "Active Directory", Enabled: false, DisabledReason: null)
            },
            static pack => pack.Id,
            static pack => pack.Name,
            static pack => pack.Enabled,
            static pack => pack.DisabledReason);

        Assert.Empty(entries);
    }

    private sealed record TestPack(string Id, string Name, bool Enabled, string? DisabledReason);
}
