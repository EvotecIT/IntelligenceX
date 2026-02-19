using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolArgsTests {

    [Fact]
    public void GetBoolean_ReturnsDefaultFalse_WhenMissing() {
        var args = new JsonObject();

        var value = ToolArgs.GetBoolean(args, "include_message", defaultValue: false);

        Assert.False(value);
    }

    [Fact]
    public void GetBoolean_ReturnsDefaultTrue_WhenMissing() {
        var args = new JsonObject();

        var value = ToolArgs.GetBoolean(args, "include_message", defaultValue: true);

        Assert.True(value);
    }

    [Fact]
    public void GetBoolean_ReturnsDefault_WhenValueIsNotBoolean() {
        var args = new JsonObject()
            .Add("include_message", "true");

        Assert.False(ToolArgs.GetBoolean(args, "include_message", defaultValue: false));
        Assert.True(ToolArgs.GetBoolean(args, "include_message", defaultValue: true));

        args.Add("include_message", 1);
        Assert.False(ToolArgs.GetBoolean(args, "include_message", defaultValue: false));
        Assert.True(ToolArgs.GetBoolean(args, "include_message", defaultValue: true));
    }

    [Fact]
    public void GetOptionBoundedInt32_ShouldClampWithOptionMax() {
        Assert.Equal(50, ToolArgs.GetOptionBoundedInt32(arguments: null, key: "max_results", optionMaxInclusive: 50));
        Assert.Equal(1, ToolArgs.GetOptionBoundedInt32(new JsonObject().Add("max_results", 0), "max_results", 50));
        Assert.Equal(1, ToolArgs.GetOptionBoundedInt32(new JsonObject().Add("max_results", -5), "max_results", 50));
        Assert.Equal(50, ToolArgs.GetOptionBoundedInt32(new JsonObject().Add("max_results", 999), "max_results", 50));
        Assert.Equal(23, ToolArgs.GetOptionBoundedInt32(new JsonObject().Add("max_results", 23), "max_results", 50));
        Assert.Equal(10, ToolArgs.GetOptionBoundedInt32(new JsonObject().Add("max_results", 5), "max_results", 50, minInclusive: 10));
    }

    [Fact]
    public void GetPositiveOptionBoundedInt32OrDefault_ShouldKeepDefaultForNonPositiveAndCapHighValues() {
        Assert.Equal(30, ToolArgs.GetPositiveOptionBoundedInt32OrDefault(arguments: null, key: "top", defaultValue: 30, optionMaxInclusive: 50));
        Assert.Equal(30, ToolArgs.GetPositiveOptionBoundedInt32OrDefault(new JsonObject().Add("top", 0), "top", 30, 50));
        Assert.Equal(30, ToolArgs.GetPositiveOptionBoundedInt32OrDefault(new JsonObject().Add("top", -10), "top", 30, 50));
        Assert.Equal(50, ToolArgs.GetPositiveOptionBoundedInt32OrDefault(new JsonObject().Add("top", 999), "top", 30, 50));
        Assert.Equal(12, ToolArgs.GetPositiveOptionBoundedInt32OrDefault(new JsonObject().Add("top", 12), "top", 30, 50));
    }
}
