using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolAlternateEngineSelectorNamesTests {
    [Theory]
    [InlineData(new[] { "computer_name", "engine", "status" }, "engine")]
    [InlineData(new[] { "target", "Backend_Id" }, "backend_id")]
    [InlineData(new[] { "computer_name", "transport" }, "")]
    public void TryResolveSelectorArgumentName_ShouldMatchCanonicalSelectorArguments(string[] arguments, string expected) {
        var matched = ToolAlternateEngineSelectorNames.TryResolveSelectorArgumentName(arguments, out var actual);

        Assert.Equal(expected.Length > 0, matched);
        Assert.Equal(expected, actual);
    }
}
