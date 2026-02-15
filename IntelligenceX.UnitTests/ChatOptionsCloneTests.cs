using System.Collections.Generic;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ChatOptionsCloneTests {
    [Fact]
    public void Clone_ShouldDefensivelyCopyToolsList() {
        var tools = new List<ToolDefinition> {
            new ToolDefinition("t1")
        };

        var options = new ChatOptions {
            Tools = tools
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        Assert.NotNull(clone.Tools);
        Assert.NotSame(options.Tools, clone.Tools);
        Assert.Single(clone.Tools!);

        // Mutating the original list should not affect the clone.
        tools.Add(new ToolDefinition("t2"));
        Assert.Single(clone.Tools!);
    }
}

