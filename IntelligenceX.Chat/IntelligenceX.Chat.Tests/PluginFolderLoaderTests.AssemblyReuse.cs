using System;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class PluginFolderLoaderTests {
    [Fact]
    public void CanReuseLoadedAssembly_AllowsExactPathMatch() {
        var loadedAssembly = Assembly.GetExecutingAssembly();
        var requestedName = AssemblyName.GetAssemblyName(loadedAssembly.Location);

        Assert.True(PluginFolderToolPackLoader.CanReuseLoadedAssembly(loadedAssembly, requestedName, loadedAssembly.Location));
    }

    [Fact]
    public void CanReuseLoadedAssembly_RejectsSimpleNameOnlyMatchWhenVersionDiffers() {
        var loadedAssembly = Assembly.GetExecutingAssembly();
        var requestedName = AssemblyName.GetAssemblyName(loadedAssembly.Location);
        var loadedVersion = requestedName.Version ?? new Version(1, 0, 0, 0);
        requestedName.Version = new Version(loadedVersion.Major + 1, loadedVersion.Minor, loadedVersion.Build, loadedVersion.Revision);
        var requestedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), Path.GetFileName(loadedAssembly.Location));

        Assert.False(PluginFolderToolPackLoader.CanReuseLoadedAssembly(loadedAssembly, requestedName, requestedPath));
    }
}
