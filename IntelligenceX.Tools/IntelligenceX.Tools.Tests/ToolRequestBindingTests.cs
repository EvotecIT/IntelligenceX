using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolRequestBindingTests {
    private sealed record RequestModel(string Name, bool Enabled, IReadOnlyList<string> Tags);

    [Fact]
    public void ArgumentReader_TryReadRequiredString_ShouldFailWhenMissing() {
        var reader = new ToolArgumentReader(new JsonObject());

        var ok = reader.TryReadRequiredString("name", out var value, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, value);
        Assert.Equal("name is required.", error);
    }

    [Fact]
    public void RequestBinder_Bind_ShouldProduceTypedRequestModel() {
        var arguments = new JsonObject()
            .Add("name", " Tool ")
            .Add("enabled", true)
            .Add("tags", new JsonArray()
                .Add("a")
                .Add("A")
                .Add(" b "));

        var result = ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("name", out var name, out var error)) {
                return ToolRequestBindingResult<RequestModel>.Failure(error);
            }

            return ToolRequestBindingResult<RequestModel>.Success(new RequestModel(
                Name: name,
                Enabled: reader.Boolean("enabled"),
                Tags: reader.DistinctStringArray("tags")));
        });

        Assert.True(result.IsValid);
        Assert.NotNull(result.Request);
        Assert.Equal("Tool", result.Request!.Name);
        Assert.True(result.Request.Enabled);
        Assert.Equal(new[] { "a", "b" }, result.Request.Tags);
    }

    [Fact]
    public void Failure_WhenHintsMutableList_ShouldDefensivelyCopyAndExposeReadOnlyView() {
        var hints = new List<string> { "first" };

        var result = ToolRequestBindingResult<RequestModel>.Failure(
            error: "bad request",
            hints: hints);

        hints.Add("second");

        Assert.Equal(new[] { "first" }, result.Hints);
        var list = Assert.IsAssignableFrom<IList<string>>(result.Hints);
        Assert.Throws<NotSupportedException>(() => list.Add("x"));
    }

    [Fact]
    public void Failure_WhenHintsContainWhitespace_ShouldTrimAndDropEmptyEntries() {
        var result = ToolRequestBindingResult<RequestModel>.Failure(
            error: "bad request",
            hints: new[] { "  first hint  ", " ", string.Empty, "second hint" });

        Assert.Equal(new[] { "first hint", "second hint" }, result.Hints);
    }
}
