using System;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies language-neutral structured memory extraction from user text.
/// </summary>
public sealed class MainWindowStructuredMemoryIntentTests {
    private static readonly MethodInfo TryExtractMemoryIntentMethod = typeof(MainWindow).GetMethod(
                                                                           "TryExtractMemoryIntent",
                                                                           BindingFlags.NonPublic | BindingFlags.Static)
                                                                       ?? throw new InvalidOperationException("TryExtractMemoryIntent method not found.");

    /// <summary>
    /// Ensures ix_memory envelope payload is parsed without English lexical cues.
    /// </summary>
    [Fact]
    public void TryExtractMemoryIntent_ParsesIxMemoryEnvelopePayload() {
        const string input = """
                             ```ix_memory
                             {"memory":"Cliente prefiere respuestas cortas y concretas."}
                             ```
                             """;

        var args = new object?[] { input, null };
        var parsed = (bool)(TryExtractMemoryIntentMethod.Invoke(null, args) ?? false);
        var memoryFact = args[1] as string;

        Assert.True(parsed);
        Assert.Equal("Cliente prefiere respuestas cortas y concretas", memoryFact);
    }

    /// <summary>
    /// Ensures direct JSON memory payload is parsed as structured intent.
    /// </summary>
    [Fact]
    public void TryExtractMemoryIntent_ParsesDirectJsonPayload() {
        const string input = "{\"fact\":\"Відповідай лаконічно та по суті.\"}";

        var args = new object?[] { input, null };
        var parsed = (bool)(TryExtractMemoryIntentMethod.Invoke(null, args) ?? false);
        var memoryFact = args[1] as string;

        Assert.True(parsed);
        Assert.Equal("Відповідай лаконічно та по суті", memoryFact);
    }
}
