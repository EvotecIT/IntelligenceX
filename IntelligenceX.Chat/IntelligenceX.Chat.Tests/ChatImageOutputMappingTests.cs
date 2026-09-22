using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards generated-image projection at the service protocol boundary.
/// </summary>
public sealed class ChatImageOutputMappingTests {
    /// <summary>
    /// Ensures the service exposes stable image references without copying large base64 payloads into NDJSON.
    /// </summary>
    [Fact]
    public void BuildChatImageOutputs_ProjectsPathUrlAndMimeType() {
        var output = new TurnOutput(
            type: "image",
            text: null,
            imageUrl: " https://example.test/generated.png ",
            imagePath: @" C:\artifacts\generated.png ",
            base64: "large-payload-is-not-forwarded",
            mimeType: " image/png ",
            raw: new JsonObject(),
            additional: null);
        var turn = new TurnInfo(
            id: "turn-image",
            status: "completed",
            outputs: new[] { output },
            imageOutputs: new[] { output },
            raw: new JsonObject(),
            additional: null);

        var images = ChatServiceSession.BuildChatImageOutputs(turn);

        var image = Assert.Single(images!);
        Assert.Equal("https://example.test/generated.png", image.Url);
        Assert.Equal(@"C:\artifacts\generated.png", image.Path);
        Assert.Equal("image/png", image.MimeType);
    }

    /// <summary>
    /// Ensures a later text-review phase cannot discard media produced by an earlier phase.
    /// </summary>
    [Fact]
    public void CaptureChatImageOutputs_AccumulatesAndDeduplicatesAcrossModelPhases() {
        var output = new TurnOutput(
            type: "image",
            text: null,
            imageUrl: "https://example.test/generated.png",
            imagePath: null,
            base64: null,
            mimeType: "image/png",
            raw: new JsonObject(),
            additional: null);
        var initialTurn = new TurnInfo(
            id: "turn-image",
            status: "completed",
            outputs: new[] { output },
            imageOutputs: new[] { output },
            raw: new JsonObject(),
            additional: null);
        var reviewTurn = new TurnInfo(
            id: "turn-review",
            status: "completed",
            outputs: Array.Empty<TurnOutput>(),
            imageOutputs: Array.Empty<TurnOutput>(),
            raw: new JsonObject(),
            additional: null);
        var images = new List<ChatImageOutputDto>();

        ChatServiceSession.CaptureChatImageOutputs(images, initialTurn);
        ChatServiceSession.CaptureChatImageOutputs(images, reviewTurn);
        ChatServiceSession.CaptureChatImageOutputs(images, initialTurn);
        var snapshot = ChatServiceSession.SnapshotChatImageOutputs(images);

        var image = Assert.Single(snapshot!);
        Assert.Equal("https://example.test/generated.png", image.Url);
        Assert.Equal("image/png", image.MimeType);
    }
}
