using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    private static readonly MethodInfo BuildSemanticMemoryVectorMethod = typeof(MainWindow).GetMethod(
        "BuildSemanticMemoryVector",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSemanticMemoryVector not found.");

    private static readonly MethodInfo ComputeSemanticVectorCosineMethod = typeof(MainWindow).GetMethod(
        "ComputeSemanticVectorCosine",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeSemanticVectorCosine not found.");

    private static readonly MethodInfo ComputeMmrLambdaMethod = typeof(MainWindow).GetMethod(
        "ComputeMmrLambda",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeMmrLambda not found.");

    private static readonly MethodInfo ComputeMinimumRelevanceGateMethod = typeof(MainWindow).GetMethod(
        "ComputeMinimumRelevanceGate",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeMinimumRelevanceGate not found.");

    private static readonly MethodInfo NormalizeMemoryUpdatedUtcForRecencyMethod = typeof(MainWindow).GetMethod(
        "NormalizeMemoryUpdatedUtcForRecency",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizeMemoryUpdatedUtcForRecency not found.");

    private static readonly MethodInfo BuildPersistentMemoryLinesForEmptyQueryMethod = typeof(MainWindow).GetMethod(
        "BuildPersistentMemoryLinesForEmptyQuery",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPersistentMemoryLinesForEmptyQuery not found.");

    private static readonly MethodInfo TryExtractMemoryFactFromRegexMethod = typeof(MainWindow).GetMethod(
        "TryExtractMemoryFactFromRegex",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryExtractMemoryFactFromRegex not found.");

    private static readonly MethodInfo ClearPersistentMemoryAsyncMethod = typeof(MainWindow).GetMethod(
        "ClearPersistentMemoryAsync",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearPersistentMemoryAsync not found.");

    private static readonly FieldInfo MemoryRememberIntentRegexField = typeof(MainWindow).GetField(
        "MemoryRememberIntentRegex",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MemoryRememberIntentRegex not found.");

    private static readonly FieldInfo AppStateField = typeof(MainWindow).GetField(
        "_appState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_appState field not found.");

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
    /// Ensures short non-Latin words are preserved while numeric-only/short Latin filler tokens are dropped.
    /// </summary>
    [Fact]
    public void TokenizeMemorySemanticText_KeepsNonLatinSignalAndDropsNumericNoise() {
        var tokens = InvokeTokenize("\u7ee7\u7eed \u6267\u884c to do ad0 42");

        Assert.Contains("\u7ee7\u7eed", tokens);
        Assert.Contains("\u6267\u884c", tokens);
        Assert.Contains("ad0", tokens);
        Assert.DoesNotContain("42", tokens);
        Assert.DoesNotContain("to", tokens);
        Assert.DoesNotContain("do", tokens);
    }

    /// <summary>
    /// Ensures English verbs are not hardcoded out via stopword lists.
    /// </summary>
    [Fact]
    public void TokenizeMemorySemanticText_DoesNotDropEnglishActionWordsByHardcodedList() {
        var tokens = InvokeTokenize("check replication health");

        Assert.Contains("check", tokens);
        Assert.Contains("replication", tokens);
        Assert.Contains("health", tokens);
    }

    /// <summary>
    /// Ensures semantic vectors score related phrasing above unrelated content.
    /// </summary>
    [Fact]
    public void SemanticMemoryVector_CosineSimilarityRanksRelatedTextHigher() {
        var query = InvokeBuildSemanticMemoryVector("replication health on ado1");
        var related = InvokeBuildSemanticMemoryVector("ado1 replication status and health");
        var unrelated = InvokeBuildSemanticMemoryVector("certificate expiration report for exchange");

        var relatedScore = InvokeComputeSemanticVectorCosine(query, related);
        var unrelatedScore = InvokeComputeSemanticVectorCosine(query, unrelated);

        Assert.True(relatedScore > unrelatedScore);
        Assert.True(relatedScore >= 0d && relatedScore <= 1d);
        Assert.True(unrelatedScore >= 0d && unrelatedScore <= 1d);
    }

    /// <summary>
    /// Ensures non-Latin semantic vectors still produce stable positive similarity.
    /// </summary>
    [Fact]
    public void SemanticMemoryVector_CosineSimilaritySupportsNonLatinPhrases() {
        var query = InvokeBuildSemanticMemoryVector("\u7ee7\u7eed \u68c0\u67e5 AD0 \u590d\u5236");
        var related = InvokeBuildSemanticMemoryVector("\u68c0\u67e5 AD0 \u590d\u5236 \u7ee7\u7eed");
        var unrelated = InvokeBuildSemanticMemoryVector("\u7535\u5b50\u90ae\u4ef6 \u53d1\u9001 \u62a5\u544a");

        var relatedScore = InvokeComputeSemanticVectorCosine(query, related);
        var unrelatedScore = InvokeComputeSemanticVectorCosine(query, unrelated);

        Assert.True(relatedScore > 0d);
        Assert.True(relatedScore > unrelatedScore);
    }

    /// <summary>
    /// Ensures MMR relevance weight increases as user requests become more specific.
    /// </summary>
    [Fact]
    public void ComputeMmrLambda_IncreasesWithUserTokenCount() {
        var shortLambda = InvokeComputeMmrLambda(1);
        var mediumLambda = InvokeComputeMmrLambda(4);
        var longLambda = InvokeComputeMmrLambda(8);

        Assert.True(shortLambda < mediumLambda);
        Assert.True(mediumLambda < longLambda);
        Assert.InRange(shortLambda, 0d, 1d);
        Assert.InRange(longLambda, 0d, 1d);
    }

    /// <summary>
    /// Ensures minimum relevance gate is stricter for longer, more precise user requests.
    /// </summary>
    [Fact]
    public void ComputeMinimumRelevanceGate_IncreasesWithUserTokenCount() {
        var shortGate = InvokeComputeMinimumRelevanceGate(1);
        var mediumGate = InvokeComputeMinimumRelevanceGate(4);
        var longGate = InvokeComputeMinimumRelevanceGate(8);

        Assert.True(shortGate < mediumGate);
        Assert.True(mediumGate < longGate);
        Assert.InRange(shortGate, 0d, 1d);
        Assert.InRange(longGate, 0d, 1d);
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

    /// <summary>
    /// Ensures locale-sensitive dotted-I forms still overlap after canonical token normalization.
    /// </summary>
    [Fact]
    public void CountNewTokenMatches_MatchesTurkishDottedIForms() {
        var userTokens = InvokeTokenize("istanbul replication");
        var candidateTokens = InvokeTokenize("İSTANBUL health");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var overlap = InvokeCountNewTokenMatches(userTokens, candidateTokens, seen);

        Assert.Equal(1, overlap);
    }

    /// <summary>
    /// Ensures recency normalization preserves UTC timestamps without alteration.
    /// </summary>
    [Fact]
    public void NormalizeMemoryUpdatedUtcForRecency_PreservesUtcValues() {
        var nowUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
        var value = nowUtc.AddHours(-4);

        var normalized = InvokeNormalizeMemoryUpdatedUtcForRecency(value, nowUtc);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(value, normalized);
    }

    /// <summary>
    /// Ensures local timestamps are converted to UTC for recency scoring.
    /// </summary>
    [Fact]
    public void NormalizeMemoryUpdatedUtcForRecency_ConvertsLocalValues() {
        var nowUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
        var localValue = nowUtc.AddHours(-3).ToLocalTime();

        var normalized = InvokeNormalizeMemoryUpdatedUtcForRecency(localValue, nowUtc);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(localValue.ToUniversalTime(), normalized);
    }

    /// <summary>
    /// Ensures future UTC timestamps are clamped to the scoring clock.
    /// </summary>
    [Fact]
    public void NormalizeMemoryUpdatedUtcForRecency_ClampsFutureUtcValues() {
        var nowUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
        var futureUtc = nowUtc.AddHours(4);

        var normalized = InvokeNormalizeMemoryUpdatedUtcForRecency(futureUtc, nowUtc);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(nowUtc, normalized);
    }

    /// <summary>
    /// Ensures unspecified timestamps prefer the interpretation closest to the scoring clock.
    /// </summary>
    [Fact]
    public void NormalizeMemoryUpdatedUtcForRecency_ResolvesUnspecifiedByNearestInterpretation() {
        var nowUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
        var localClockStamp = nowUtc.AddHours(-2).ToLocalTime();
        var unspecified = DateTime.SpecifyKind(localClockStamp, DateTimeKind.Unspecified);

        var normalized = InvokeNormalizeMemoryUpdatedUtcForRecency(unspecified, nowUtc);
        var asLocalUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Local).ToUniversalTime();
        var asUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);
        var expected = Math.Abs((nowUtc - asLocalUtc).TotalHours) <= Math.Abs((nowUtc - asUtc).TotalHours)
            ? asLocalUtc
            : asUtc;

        Assert.Equal(expected, normalized);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }

    /// <summary>
    /// Ensures unspecified timestamps never normalize to values after the scoring clock.
    /// </summary>
    [Fact]
    public void NormalizeMemoryUpdatedUtcForRecency_ClampsFutureUnspecifiedValues() {
        var nowUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
        var unspecifiedFuture = DateTime.SpecifyKind(nowUtc.AddHours(3), DateTimeKind.Unspecified);

        var normalized = InvokeNormalizeMemoryUpdatedUtcForRecency(unspecifiedFuture, nowUtc);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.True(normalized <= nowUtc);
    }

    /// <summary>
    /// Ensures empty-query ranking uses normalized UTC ordering when timestamp kinds are mixed.
    /// </summary>
    [Fact]
    public void BuildPersistentMemoryLinesForEmptyQuery_NormalizesUpdatedUtcBeforeSorting() {
        var nowUtc = DateTime.UtcNow;
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(nowUtc);
        if (localOffset == TimeSpan.Zero) {
            return;
        }

        var gapMinutes = Math.Max(1, (int)Math.Min(20, Math.Abs(localOffset.TotalMinutes) / 2d));
        var gap = TimeSpan.FromMinutes(gapMinutes);
        var unspecifiedActualUtc = localOffset > TimeSpan.Zero ? nowUtc - (gap + gap) : nowUtc - gap;
        var utcActualUtc = localOffset > TimeSpan.Zero ? nowUtc - gap : nowUtc - (gap + gap);
        var unspecifiedLocalClock = TimeZoneInfo.ConvertTimeFromUtc(unspecifiedActualUtc, TimeZoneInfo.Local);
        var unspecifiedUpdated = DateTime.SpecifyKind(unspecifiedLocalClock, DateTimeKind.Unspecified);
        var utcUpdated = DateTime.SpecifyKind(utcActualUtc, DateTimeKind.Utc);

        var facts = new List<ChatMemoryFactState> {
            new() {
                Fact = "utc-fact",
                Weight = 3,
                UpdatedUtc = utcUpdated
            },
            new() {
                Fact = "unspecified-fact",
                Weight = 3,
                UpdatedUtc = unspecifiedUpdated
            }
        };

        var normalizedUtc = InvokeNormalizeMemoryUpdatedUtcForRecency(utcUpdated, nowUtc);
        var normalizedUnspecified = InvokeNormalizeMemoryUpdatedUtcForRecency(unspecifiedUpdated, nowUtc);
        var expectedFirst = normalizedUnspecified > normalizedUtc ? "unspecified-fact" : "utc-fact";

        var lines = InvokeBuildPersistentMemoryLinesForEmptyQuery(facts);

        Assert.NotEmpty(lines);
        Assert.Equal(expectedFirst, lines[0]);
    }

    /// <summary>
    /// Ensures preference phrases that start with "to use" are retained for memory capture.
    /// </summary>
    [Fact]
    public void TryExtractMemoryFactFromRegex_AllowsToUsePreferencePhrases() {
        var regex = Assert.IsType<Regex>(MemoryRememberIntentRegexField.GetValue(null));
        var args = new object?[] { regex, "remember this to use short bullets", null };

        var ok = Assert.IsType<bool>(TryExtractMemoryFactFromRegexMethod.Invoke(null, args));

        Assert.True(ok);
        Assert.Equal("to use short bullets", Assert.IsType<string>(args[2]));
    }

    /// <summary>
    /// Ensures clearly imperative task phrases are still rejected.
    /// </summary>
    [Fact]
    public void TryExtractMemoryFactFromRegex_RejectsImperativeTaskPhrases() {
        var regex = Assert.IsType<Regex>(MemoryRememberIntentRegexField.GetValue(null));
        var args = new object?[] { regex, "remember this to do domain cleanup", null };

        var ok = Assert.IsType<bool>(TryExtractMemoryFactFromRegexMethod.Invoke(null, args));

        Assert.False(ok);
        Assert.Null(args[2]);
    }

    /// <summary>
    /// Ensures null legacy memory-fact state short-circuits without throwing.
    /// </summary>
    [Fact]
    public async Task ClearPersistentMemoryAsync_ReturnsWhenMemoryFactsNull() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var state = new ChatAppState {
            MemoryFacts = null!
        };

        AppStateField.SetValue(window, state);
        await InvokeClearPersistentMemoryAsync(window);

        Assert.Null(state.MemoryFacts);
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

    private static Dictionary<int, double> InvokeBuildSemanticMemoryVector(string text) {
        var result = BuildSemanticMemoryVectorMethod.Invoke(null, new object?[] { text });
        return Assert.IsType<Dictionary<int, double>>(result);
    }

    private static double InvokeComputeSemanticVectorCosine(
        IReadOnlyDictionary<int, double> left,
        IReadOnlyDictionary<int, double> right) {
        var result = ComputeSemanticVectorCosineMethod.Invoke(null, new object?[] { left, right });
        return Assert.IsType<double>(result);
    }

    private static double InvokeComputeMmrLambda(int userTokenCount) {
        var result = ComputeMmrLambdaMethod.Invoke(null, new object?[] { userTokenCount });
        return Assert.IsType<double>(result);
    }

    private static double InvokeComputeMinimumRelevanceGate(int userTokenCount) {
        var result = ComputeMinimumRelevanceGateMethod.Invoke(null, new object?[] { userTokenCount });
        return Assert.IsType<double>(result);
    }

    private static DateTime InvokeNormalizeMemoryUpdatedUtcForRecency(DateTime value, DateTime nowUtc) {
        var result = NormalizeMemoryUpdatedUtcForRecencyMethod.Invoke(null, new object?[] { value, nowUtc });
        return Assert.IsType<DateTime>(result);
    }

    private static IReadOnlyList<string> InvokeBuildPersistentMemoryLinesForEmptyQuery(List<ChatMemoryFactState> facts) {
        var result = BuildPersistentMemoryLinesForEmptyQueryMethod.Invoke(null, new object?[] { facts });
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(result);
    }

    private static Task InvokeClearPersistentMemoryAsync(MainWindow window) {
        try {
            var invocationResult = ClearPersistentMemoryAsyncMethod.Invoke(window, null);
            return Assert.IsAssignableFrom<Task>(invocationResult);
        } catch (TargetInvocationException ex) {
            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            throw;
        }
    }
}
