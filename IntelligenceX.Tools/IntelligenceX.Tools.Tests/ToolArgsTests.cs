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
}

