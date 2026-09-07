using IntelligenceX.Json;
using Xunit;
namespace IntelligenceX.UnitTests;
public sealed class JsonLiteDepthTests {
    [Theory]
    [InlineData("[", "]")]
    [InlineData("{\"value\":", "}")]
    public void NestingBeyond128ContainersIsRejected(string opening, string closing) {
        string json = string.Concat(Enumerable.Repeat(opening, 129)) + "0" + string.Concat(Enumerable.Repeat(closing, 129));
        Assert.Throws<FormatException>(() => JsonLite.Parse(json));
    }
    [Fact]
    public void NestingWithinBoundAndDelimitersInStringsAreAccepted() {
        string json = new string('[', 128) + "0" + new string(']', 128);
        Assert.NotNull(JsonLite.Parse(json).AsArray());
        string text = new string('[', 1000) + "\\\"" + new string('}', 1000);
        Assert.Equal(text, JsonLite.Parse(JsonLite.Serialize(text)).AsString());
    }
}
