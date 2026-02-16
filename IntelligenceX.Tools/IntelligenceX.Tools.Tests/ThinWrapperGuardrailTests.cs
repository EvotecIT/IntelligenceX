using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ThinWrapperGuardrailTests {
    [Fact]
    public void ActiveDirectoryAssembly_ShouldNotContainLegacyKindParsers() {
        var assembly = typeof(AdObjectResolveTool).Assembly;

        Assert.Null(assembly.GetType("IntelligenceX.Tools.ADPlayground.ActiveDirectoryObjectKind", throwOnError: false));
        Assert.Null(assembly.GetType("IntelligenceX.Tools.ADPlayground.ActiveDirectoryObjectKindTools", throwOnError: false));
        Assert.Null(assembly.GetType("IntelligenceX.Tools.ADPlayground.SpnAccountKindTools", throwOnError: false));
    }
}
