using System.Text.Json;
using Xunit;

namespace IntelligenceX.UnitTests.Packaging;

public sealed class PowerForgePackagingContractTests
{
    [Fact]
    public void ChatMsi_PreservesLegacyInstallerIdentityAndHarvestExcludes()
    {
        using var document = OpenPublishConfig();
        var installer = document.RootElement
            .GetProperty("Installers")
            .EnumerateArray()
            .Single(item => item.GetProperty("Id").GetString() == "IntelligenceX.Chat.App.Msi");

        var product = installer.GetProperty("Authoring").GetProperty("Product");
        Assert.Equal("IntelligenceX Chat", product.GetProperty("Name").GetString());
        Assert.Equal("{a2b787a5-f539-4763-add6-2baa2c2518c7}", product.GetProperty("UpgradeCode").GetString());

        var excludes = installer.GetProperty("HarvestExcludePatterns")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("IntelligenceX.Chat.App.exe", excludes);
        Assert.Contains("run-chat.ps1", excludes);
        Assert.Contains("run-chat.cmd", excludes);
        Assert.Contains("README.md", excludes);
        Assert.Contains("portable-bundle.json", excludes);
        Assert.Contains("createdump.exe", excludes);
    }

    [Fact]
    public void InstallerWrapper_PreservesLegacyHostFallback()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Build", "Advanced", "Build-Installer.ps1"));

        Assert.Contains("Use-LegacyInstallerFlow", script, StringComparison.Ordinal);
        Assert.Contains("Build-Installer.Legacy.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Build-Project.ps1", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ResolveRepositoryRoot(), "Build", "Internal", "Build-Installer.Legacy.ps1")));
        Assert.True(File.Exists(Path.Combine(ResolveRepositoryRoot(), "Installer", "IntelligenceX.Chat", "IntelligenceX.Chat.wxs")));
    }

    [Theory]
    [InlineData("Build-Project.ps1")]
    [InlineData("Build-Release.ps1")]
    public void SigningTimeoutZero_DoesNotEnableSigningByItself(string scriptName)
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Build", scriptName));

        Assert.DoesNotContain("'SignTimeoutSeconds'" + Environment.NewLine + "    'SignTimestampUrl'", script, StringComparison.Ordinal);
        Assert.Contains("$script:BoundCliParameters.ContainsKey('SignTimeoutSeconds')", script, StringComparison.Ordinal);
        Assert.Contains("$SignTimeoutSeconds -gt 0", script, StringComparison.Ordinal);
        Assert.Contains("$hasExplicitSigningOverride = 'SignTimeoutSeconds'", script, StringComparison.Ordinal);
    }

    private static JsonDocument OpenPublishConfig()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Build", "powerforge.dotnetpublish.json")));

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IntelligenceX.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
