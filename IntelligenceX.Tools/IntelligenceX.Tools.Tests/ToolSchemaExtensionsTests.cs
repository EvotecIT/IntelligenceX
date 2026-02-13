using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolSchemaExtensionsTests {
    [Fact]
    public void WithTableViewOptions_ShouldAddProjectionProperties() {
        var schema = ToolSchema.Object(
                ("q", ToolSchema.String("query")))
            .WithTableViewOptions()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject("columns"));
        Assert.NotNull(properties.GetObject("sort_by"));
        Assert.NotNull(properties.GetObject("sort_direction"));
        Assert.NotNull(properties.GetObject("top"));
    }
}

