using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the shared persistent-memory contract used by every desktop shell.
/// </summary>
public sealed class DesktopChatMemorySelectorTests {
    /// <summary>Ensures character n-grams retain relevant non-whitespace-script memory.</summary>
    [Fact]
    public void Select_UsesScriptAwareSimilarityWhenExactTokensDoNotOverlap() {
        var now = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
        var selection = DesktopChatMemorySelector.Select(
            [
                new ChatMemoryFactState {
                    Fact = "界面默认使用深色主题",
                    Weight = 5,
                    UpdatedUtc = now
                },
                new ChatMemoryFactState {
                    Fact = "域控制器复制实验室是 ad.evotec.xyz",
                    Weight = 1,
                    UpdatedUtc = now.AddHours(-1)
                }
            ],
            "检查域复制健康",
            now);

        Assert.Contains("域控制器复制实验室是 ad.evotec.xyz", selection.Lines);
        Assert.DoesNotContain("界面默认使用深色主题", selection.Lines);
        Assert.True(selection.TopSimilarity >= 0.12d);
    }

    private static readonly DateTime NowUtc = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Ranks matching memory above unrelated higher-weight facts.</summary>
    [Fact]
    public void Select_RanksFactsForTheCurrentQuestion() {
        var facts = new List<ChatMemoryFactState> {
            Fact("The UI uses a cobalt theme", weight: 5),
            Fact("The AD replication lab is ad.evotec.xyz", weight: 3, tags: ["directory", "replication"]),
            Fact("Exchange reports are exported to HTML", weight: 4)
        };

        var selection = DesktopChatMemorySelector.Select(facts, "Check AD replication health", NowUtc);

        Assert.Contains("The AD replication lab is ad.evotec.xyz", selection.Lines);
        Assert.DoesNotContain("The UI uses a cobalt theme", selection.Lines);
        Assert.DoesNotContain("Exchange reports are exported to HTML", selection.Lines);
    }

    /// <summary>Falls back predictably when the question has no matching memory.</summary>
    [Fact]
    public void Select_UsesWeightAndRecencyWhenQuestionHasNoMemoryMatch() {
        var facts = new List<ChatMemoryFactState> {
            Fact("Older important preference", weight: 5, updatedUtc: NowUtc.AddDays(-7)),
            Fact("Recent normal preference", weight: 3, updatedUtc: NowUtc.AddHours(-1)),
            Fact("Low priority detail", weight: 1, updatedUtc: NowUtc)
        };

        var selection = DesktopChatMemorySelector.Select(facts, "Completely unrelated question", NowUtc);

        Assert.Equal(3, selection.Lines.Count);
        Assert.Equal("Older important preference", selection.Lines[0]);
    }

    /// <summary>Avoids injecting repeated variants of the same fact.</summary>
    [Fact]
    public void Select_AvoidsNearDuplicateContextLines() {
        var facts = new List<ChatMemoryFactState> {
            Fact("AD replication health is green", weight: 5),
            Fact("AD replication health is green now", weight: 4)
        };

        var selection = DesktopChatMemorySelector.Select(facts, "AD replication health", NowUtc);

        Assert.Single(selection.Lines);
    }

    /// <summary>Normalizes equivalent composed and decomposed Unicode text.</summary>
    [Fact]
    public void Tokenize_NormalizesEquivalentUnicodeForms() {
        var precomposed = DesktopChatMemorySelector.Tokenize("Résumé");
        var decomposed = DesktopChatMemorySelector.Tokenize("Re\u0301sume\u0301");

        Assert.True(precomposed.SetEquals(decomposed));
        Assert.Contains("resume", precomposed);
    }

    /// <summary>Preserves short non-Latin intent while excluding weak numeric and Latin noise.</summary>
    [Fact]
    public void Tokenize_KeepsShortNonLatinSignalAndDropsNumericNoise() {
        var tokens = DesktopChatMemorySelector.Tokenize("继 续行 to do ad0 42");

        Assert.Contains("继", tokens);
        Assert.Contains("续行", tokens);
        Assert.Contains("ad0", tokens);
        Assert.DoesNotContain("to", tokens);
        Assert.DoesNotContain("do", tokens);
        Assert.DoesNotContain("42", tokens);
    }

    /// <summary>Normalizes persisted state consistently before either shell consumes it.</summary>
    [Fact]
    public void NormalizeFacts_DeduplicatesClampsAndCapsStoredState() {
        var facts = Enumerable.Range(0, 125)
            .Select(index => Fact(
                $"Fact {index}",
                weight: index == 0 ? 99 : 3,
                tags: [" useful ", "USEFUL", new string('x', 41)],
                updatedUtc: NowUtc.AddMinutes(-index)))
            .ToList();
        facts.Add(Fact("Fact 0", weight: 1));

        var normalized = DesktopChatMemorySelector.NormalizeFacts(facts, NowUtc);

        Assert.Equal(120, normalized.Count);
        var first = Assert.Single(normalized, fact => fact.Fact == "Fact 0");
        Assert.Equal(5, first.Weight);
        Assert.Equal(["useful"], first.Tags);
        Assert.All(normalized, fact => Assert.Equal(DateTimeKind.Utc, fact.UpdatedUtc.Kind));
    }

    private static ChatMemoryFactState Fact(
        string text,
        int weight = 3,
        string[]? tags = null,
        DateTime? updatedUtc = null) {
        return new ChatMemoryFactState {
            Fact = text,
            Weight = weight,
            Tags = tags ?? Array.Empty<string>(),
            UpdatedUtc = updatedUtc ?? NowUtc
        };
    }
}
