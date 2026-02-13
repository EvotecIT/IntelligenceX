using IntelligenceX.Tools.ActiveDirectory;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ThinWrapperGuardrailTests {
    [Fact]
    public void ActiveDirectoryAssembly_ShouldNotContainLegacyKindParsers() {
        var assembly = typeof(AdObjectResolveTool).Assembly;

        Assert.Null(assembly.GetType("IntelligenceX.Tools.ActiveDirectory.ActiveDirectoryObjectKind", throwOnError: false));
        Assert.Null(assembly.GetType("IntelligenceX.Tools.ActiveDirectory.ActiveDirectoryObjectKindTools", throwOnError: false));
        Assert.Null(assembly.GetType("IntelligenceX.Tools.ActiveDirectory.SpnAccountKindTools", throwOnError: false));
    }
}
