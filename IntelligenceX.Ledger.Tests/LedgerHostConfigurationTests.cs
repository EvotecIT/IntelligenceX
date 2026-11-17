using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerHostConfigurationTests {
    [Fact]
    public void ServiceHostDoesNotWatchItsWorkingDirectory() {
        var builder = Program.CreateWebBuilder();
        AssertStaticConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
    }

    [Fact]
    public void McpHostReadsNoConfigurationFiles() {
        var builder = IntelligenceX.Ledger.Mcp.Program.CreateHostBuilder();
        Assert.Empty(((IConfigurationBuilder)builder.Configuration).Sources.OfType<FileConfigurationSource>());
        AssertStaticConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
    }

    private static void AssertStaticConfiguration(IConfigurationBuilder configuration, string contentRoot) {
        Assert.DoesNotContain(configuration.Sources.OfType<FileConfigurationSource>(), source => source.ReloadOnChange);
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.TrimEndingDirectorySeparator(contentRoot));
    }
}
