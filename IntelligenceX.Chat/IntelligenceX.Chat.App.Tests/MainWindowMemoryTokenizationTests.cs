using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests persistent-memory semantic tokenization and overlap dedupe behavior.
/// </summary>
public sealed class MainWindowMemoryTokenizationTests {
    private static readonly MethodInfo TokenizeMethod = typeof(MainWindow).GetMethod(
        "TokenizeMemorySemanticText",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TokenizeMemorySemanticText not found.");

    private static readonly MethodInfo CountNewTokenMatchesMethod = typeof(MainWindow).GetMethod(
        "CountNewTokenMatches",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CountNewTokenMatches not found.");

    /// <summary>
    /// Ensures tokenization yields stable semantic tokens for precomposed/decomposed Unicode forms.
    /// </summary>
    [Fact]
    public void TokenizeMemorySemanticText_NormalizesUnicodeForms() {
        var precomposed = InvokeTokenize("Résumé");
        var decomposed = InvokeTokenize("Re\u0301sume\u0301");

        Assert.Single(precomposed);
        Assert.Single(decomposed);
        Assert.True(precomposed.SetEquals(decomposed));
    }

    /// <summary>
    /// Ensures duplicate tokens are counted once when they appear in fact text and tags.
    /// </summary>
    [Fact]
    public void CountNewTokenMatches_DedupesAcrossFactAndTagTokens() {
        var userTokens = InvokeTokenize("replication health");
        var factTokens = InvokeTokenize("Replication");
        var tagTokens = InvokeTokenize("REPLICATION HEALTH");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var factOverlap = InvokeCountNewTokenMatches(userTokens, factTokens, seen);
        var tagOverlap = InvokeCountNewTokenMatches(userTokens, tagTokens, seen);

        Assert.Equal(1, factOverlap);
        Assert.Equal(1, tagOverlap);
    }

    /// <summary>
    /// Ensures overlap counting remains case-insensitive even when callers use case-sensitive token sets.
    /// </summary>
    [Fact]
    public void CountNewTokenMatches_DedupesMixedCaseFromDifferentComparers() {
        var userTokens = new HashSet<string>(StringComparer.Ordinal) { "replication" };
        var candidateTokens = new HashSet<string>(StringComparer.Ordinal) { "REPLICATION", "Replication" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var overlap = InvokeCountNewTokenMatches(userTokens, candidateTokens, seen);

        Assert.Equal(1, overlap);
    }

    private static HashSet<string> InvokeTokenize(string input) {
        var result = TokenizeMethod.Invoke(null, new object?[] { input });
        return Assert.IsType<HashSet<string>>(result);
    }

    private static int InvokeCountNewTokenMatches(
        IReadOnlySet<string> userTokens,
        IReadOnlySet<string> candidateTokens,
        HashSet<string> seen) {
        var result = CountNewTokenMatchesMethod.Invoke(null, new object?[] { userTokens, candidateTokens, seen });
        return Assert.IsType<int>(result);
    }
}
