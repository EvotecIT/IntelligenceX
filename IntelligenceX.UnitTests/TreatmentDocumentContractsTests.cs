using IntelligenceX.Json;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Treatment;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class TreatmentDocumentContractsTests {
    [Fact]
    public async Task TreatmentCarriesInlineImagesSchemaAndEphemeralBounds() {
        var client = new Client();
        var provider = new OpenAIChatTreatmentProvider(client);
        TreatmentResult result = await provider.RunAsync(new() {
            Prompt = "Read the inline image.", Ephemeral = true, MaxResponseBytes = 4096, EnforceOutputSchema = true,
            MaxInlineImageBytes = 3, Inputs = new[] { new TreatmentInputArtifact { Id = "page-1", MediaType = "image/png", ImageBytes = new byte[] { 1, 2, 3 } } },
            OutputSchema = new() { Strict = true, JsonSchema = JsonLite.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}").AsObject() }
        });
        Assert.Equal("completed", result.Status);
        Assert.NotNull(client.Options);
        var copy = new ChatOptions(client.Options!);
        Assert.True(copy.Ephemeral);
        Assert.Equal(4096, copy.MaxResponseBytes);
        Assert.NotNull(copy.ResponseFormat);
        Assert.True(copy.ResponseFormat!.Strict);
        string wire = JsonLite.Serialize(JsonValue.From(client.Input!.ToJson()));
        Assert.Contains("data:image/png;base64,AQID", wire);
        Assert.DoesNotContain("local_image", wire);
    }

    [Fact]
    public async Task AggregateImageLimitIsEnforcedBeforeTheChatBoundary() {
        var client = new Client();
        var provider = new OpenAIChatTreatmentProvider(client);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RunAsync(new() {
            Prompt = "Read images", MaxInlineImageBytes = 3, Inputs = new[] {
                new TreatmentInputArtifact { MediaType = "image/png", ImageBytes = new byte[] { 1, 2 } },
                new TreatmentInputArtifact { MediaType = "image/png", ImageBytes = new byte[] { 3, 4 } }
            }
        }));
        Assert.Null(client.Input);
    }

    private sealed class Client : ITreatmentChatClient {
        public ChatInput? Input { get; private set; }
        public ChatOptions? Options { get; private set; }
        public Task<TreatmentChatResponse> SendAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken = default) {
            Input = input; Options = options;
            return Task.FromResult(new TreatmentChatResponse("result", "completed", new[] { new TreatmentChatOutput("text", "text", "{}") }));
        }
    }
}
