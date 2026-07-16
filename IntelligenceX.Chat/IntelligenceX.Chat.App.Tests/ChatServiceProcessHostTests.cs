using System.IO;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Launch;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Protects process-host launch and secret-handling contracts.
/// </summary>
public sealed class ChatServiceProcessHostTests {
    /// <summary>
    /// Ensures an explicitly missing service root is reported without attempting a process launch.
    /// </summary>
    [Fact]
    public async Task EnsureRunningAsync_MissingSourceReturnsSourceNotFound() {
        using var host = new ChatServiceProcessHost();
        var missingDirectory = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", "missing-" + System.Guid.NewGuid().ToString("N"));

        var result = await host.EnsureRunningAsync(new ChatServiceProcessStartOptions {
            PipeName = "intelligencex.chat.tests",
            ParentProcessId = 123,
            ServiceSourceDirectory = missingDirectory
        });

        Assert.False(result.IsRunning);
        Assert.Equal(ChatServiceProcessStartFailure.SourceNotFound, result.Failure);
    }

    /// <summary>
    /// Ensures a compatible-http password is supplied only through the child-process environment.
    /// </summary>
    [Fact]
    public void CreateStartInfo_BasicPasswordUsesEnvironmentInsteadOfArguments() {
        var profile = new ChatServiceLaunchProfileOptions {
            OpenAIAuthMode = "basic",
            OpenAIBasicUsername = "operator",
            OpenAIBasicPassword = "secret-value"
        };
        var arguments = ServiceLaunchArguments.Build(
            "intelligencex.chat.tests",
            detachedServiceMode: true,
            parentProcessId: 123,
            profileOptions: profile);

        var startInfo = ChatServiceProcessHost.CreateStartInfo(
            serviceDirectory: @"C:\runtime",
            executablePath: @"C:\runtime\IntelligenceX.Chat.Service.exe",
            assemblyPath: @"C:\runtime\IntelligenceX.Chat.Service.dll",
            hasExecutable: true,
            launchArguments: arguments,
            profileOptions: profile);

        Assert.DoesNotContain("secret-value", startInfo.ArgumentList);
        Assert.Equal("secret-value", startInfo.Environment[ChatServiceEnvironmentVariables.OpenAIBasicPassword]);
    }

    /// <summary>
    /// Ensures framework-dependent service launches prepend the service assembly to dotnet arguments.
    /// </summary>
    [Fact]
    public void CreateStartInfo_FrameworkDependentPayloadPrependsAssemblyPath() {
        var assemblyPath = Path.Combine("C:\\runtime", "IntelligenceX.Chat.Service.dll");
        var startInfo = ChatServiceProcessHost.CreateStartInfo(
            serviceDirectory: @"C:\runtime",
            executablePath: @"C:\runtime\IntelligenceX.Chat.Service.exe",
            assemblyPath: assemblyPath,
            hasExecutable: false,
            launchArguments: new[] { "--pipe", "intelligencex.chat.tests" },
            profileOptions: null);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(assemblyPath, startInfo.ArgumentList[0]);
        Assert.Equal("--pipe", startInfo.ArgumentList[1]);
        Assert.Equal("intelligencex.chat.tests", startInfo.ArgumentList[2]);
        Assert.False(startInfo.Environment.ContainsKey(ChatServiceEnvironmentVariables.OpenAIBasicPassword));
    }
}
