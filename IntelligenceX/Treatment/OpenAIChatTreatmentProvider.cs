using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Treatment;

/// <summary>
/// Treatment provider backed by the existing IntelligenceX OpenAI chat client.
/// </summary>
public sealed class OpenAIChatTreatmentProvider : ITreatmentProvider {
    private readonly ITreatmentChatClient _client;

    /// <summary>
    /// Initializes a new provider using an IntelligenceX client.
    /// </summary>
    public OpenAIChatTreatmentProvider(IntelligenceXClient client) : this(new IntelligenceXClientTreatmentChatClient(client)) { }

    /// <summary>
    /// Initializes a new provider using a treatment chat client.
    /// </summary>
    public OpenAIChatTreatmentProvider(ITreatmentChatClient client) {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<TreatmentResult> RunAsync(TreatmentRequest request, CancellationToken cancellationToken = default) {
        TreatmentPromptBuilder.Validate(request);

        var prompt = TreatmentPromptBuilder.Build(request);
        var input = ChatInput.FromText(prompt);
        AddImageInputs(input, request);

        var options = BuildOptions(request);
        var response = await _client.SendAsync(input, options, cancellationToken).ConfigureAwait(false);
        var text = JoinText(response.Outputs);
        var assets = BuildAssets(request, response);
        var json = TryExtractJson(text);
        var id = !string.IsNullOrWhiteSpace(request.Id) ? request.Id! : response.Id;

        return new TreatmentResult(id, response.Status, text, json, assets, response.Raw, request.Metadata);
    }

    private static ChatOptions BuildOptions(TreatmentRequest request) {
        return new ChatOptions {
            Model = request.Model,
            Instructions = request.Instructions,
            ReasoningEffort = request.ReasoningEffort,
            TextVerbosity = request.TextVerbosity,
            Temperature = request.Temperature,
            WorkingDirectory = request.WorkingDirectory,
            Workspace = request.Workspace,
            AllowNetwork = request.AllowNetwork,
            NewThread = request.NewThread,
            TelemetryFeature = "treatment",
            TelemetrySurface = "IntelligenceX.Treatment",
            RequireWorkspaceForFileAccess = true,
            ImageGeneration = MapImageOptions(request.ImageGeneration)
        };
    }

    private static ImageGenerationOptions? MapImageOptions(TreatmentImageOptions? options) {
        if (options is null) {
            return null;
        }

        return new ImageGenerationOptions {
            Enabled = options.Enabled,
            OutputDirectory = options.OutputDirectory,
            Quality = options.Quality,
            Size = options.Size,
            OutputFormat = options.OutputFormat,
            Background = options.Background,
            OutputCompression = options.OutputCompression,
            PartialImages = options.PartialImages
        };
    }

    private static void AddImageInputs(ChatInput input, TreatmentRequest request) {
        foreach (var artifact in request.Inputs) {
            if (artifact is null) {
                continue;
            }
            if (!IsImageArtifact(artifact)) {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(artifact.Path)) {
                input.AddImagePath(ResolveLocalArtifactPath(artifact.Path!, request));
            } else if (artifact.Uri is not null) {
                input.AddImageUrl(artifact.Uri.ToString());
            }
        }
    }

    private static bool IsImageArtifact(TreatmentInputArtifact artifact) {
        var mediaType = NormalizeMediaType(artifact.MediaType);
        if (IsSupportedImageMediaType(mediaType)) {
            return true;
        }

        var extension = Path.GetExtension(artifact.Path ?? artifact.Uri?.AbsolutePath ?? string.Empty);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedImageMediaType(string mediaType) =>
        mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    private static string ResolveLocalArtifactPath(string path, TreatmentRequest request) {
        var baseDirectory = request.WorkingDirectory ?? request.Workspace;
        return TreatmentLocalInputReader.ResolvePath(path, baseDirectory);
    }

    private static string NormalizeMediaType(string? mediaType) {
        if (string.IsNullOrWhiteSpace(mediaType)) {
            return string.Empty;
        }

        var semicolon = mediaType!.IndexOf(';');
        return (semicolon < 0 ? mediaType : mediaType.Substring(0, semicolon)).Trim();
    }

    private static string? JoinText(IReadOnlyList<TreatmentChatOutput> outputs) {
        var sb = new StringBuilder();
        foreach (var output in outputs) {
            if (!string.IsNullOrWhiteSpace(output.Text)) {
                if (sb.Length > 0) {
                    sb.AppendLine();
                }
                sb.Append(output.Text!.Trim());
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static IReadOnlyList<TreatmentAsset> BuildAssets(TreatmentRequest request, TreatmentChatResponse response) {
        var assets = new List<TreatmentAsset>();
        foreach (var output in response.Outputs) {
            if (!output.IsImage) {
                continue;
            }
            assets.Add(new TreatmentAsset(
                id: output.Id,
                modality: TreatmentOutputModality.Image,
                purpose: "generated",
                path: output.ImagePath,
                uri: TryCreateUri(output.ImageUrl),
                mediaType: output.MimeType,
                altText: null,
                caption: null,
                credit: null,
                model: request.Model,
                prompt: request.Prompt,
                revisedPrompt: output.RevisedPrompt,
                sourceInputIds: CollectInputIds(request),
                raw: output.Raw));
        }
        return assets;
    }

    private static IReadOnlyList<string> CollectInputIds(TreatmentRequest request) {
        var ids = new List<string>();
        foreach (var input in request.Inputs) {
            if (!string.IsNullOrWhiteSpace(input?.Id)) {
                ids.Add(input!.Id!);
            }
        }
        return ids;
    }

    private static Uri? TryCreateUri(string? value) {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static JsonValue? TryExtractJson(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        var trimmed = StripJsonFence(text!.Trim());
        try {
            return JsonLite.Parse(trimmed);
        } catch {
            return null;
        }
    }

    private static string StripJsonFence(string text) {
        if (!text.StartsWith("```", StringComparison.Ordinal)) {
            return text;
        }

        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine) {
            return text;
        }
        return text.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
    }
}

/// <summary>
/// Chat client abstraction used by treatment providers.
/// </summary>
public interface ITreatmentChatClient {
    /// <summary>
    /// Sends a chat treatment prompt to the backing provider.
    /// </summary>
    Task<TreatmentChatResponse> SendAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter from IntelligenceXClient chat turns to treatment chat responses.
/// </summary>
public sealed class IntelligenceXClientTreatmentChatClient : ITreatmentChatClient {
    private readonly IntelligenceXClient _client;

    /// <summary>
    /// Initializes a new adapter.
    /// </summary>
    public IntelligenceXClientTreatmentChatClient(IntelligenceXClient client) {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<TreatmentChatResponse> SendAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken = default) {
        var turn = await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        return TreatmentChatResponse.FromTurn(turn);
    }
}

/// <summary>
/// Provider-neutral chat response used by treatment providers.
/// </summary>
public sealed class TreatmentChatResponse {
    /// <summary>
    /// Initializes a new treatment chat response.
    /// </summary>
    public TreatmentChatResponse(string id, string? status, IReadOnlyList<TreatmentChatOutput>? outputs, object? raw = null) {
        Id = id;
        Status = status;
        Outputs = outputs ?? Array.Empty<TreatmentChatOutput>();
        Raw = raw;
    }

    /// <summary>
    /// Provider response id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Provider status.
    /// </summary>
    public string? Status { get; }
    /// <summary>
    /// Provider outputs.
    /// </summary>
    public IReadOnlyList<TreatmentChatOutput> Outputs { get; }
    /// <summary>
    /// Raw provider response.
    /// </summary>
    public object? Raw { get; }

    internal static TreatmentChatResponse FromTurn(TurnInfo turn) {
        var outputs = new List<TreatmentChatOutput>();
        foreach (var output in turn.Outputs) {
            outputs.Add(TreatmentChatOutput.FromTurnOutput(output));
        }
        return new TreatmentChatResponse(turn.Id, turn.Status, outputs, turn);
    }
}

/// <summary>
/// Provider-neutral chat output used by treatment providers.
/// </summary>
public sealed class TreatmentChatOutput {
    /// <summary>
    /// Initializes a new treatment chat output.
    /// </summary>
    public TreatmentChatOutput(string? id, string type, string? text = null, string? imageUrl = null, string? imagePath = null,
        string? base64 = null, string? mimeType = null, string? revisedPrompt = null, object? raw = null) {
        Id = id;
        Type = type;
        Text = text;
        ImageUrl = imageUrl;
        ImagePath = imagePath;
        Base64 = base64;
        MimeType = mimeType;
        RevisedPrompt = revisedPrompt;
        Raw = raw;
    }

    /// <summary>
    /// Output id.
    /// </summary>
    public string? Id { get; }
    /// <summary>
    /// Output type.
    /// </summary>
    public string Type { get; }
    /// <summary>
    /// Text output.
    /// </summary>
    public string? Text { get; }
    /// <summary>
    /// Image URL.
    /// </summary>
    public string? ImageUrl { get; }
    /// <summary>
    /// Local image path.
    /// </summary>
    public string? ImagePath { get; }
    /// <summary>
    /// Base64 image payload.
    /// </summary>
    public string? Base64 { get; }
    /// <summary>
    /// Output MIME type.
    /// </summary>
    public string? MimeType { get; }
    /// <summary>
    /// Revised prompt returned by the provider.
    /// </summary>
    public string? RevisedPrompt { get; }
    /// <summary>
    /// Raw provider output.
    /// </summary>
    public object? Raw { get; }
    /// <summary>
    /// Whether this output is an image.
    /// </summary>
    public bool IsImage => string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase);

    internal static TreatmentChatOutput FromTurnOutput(TurnOutput output) {
        return new TreatmentChatOutput(
            id: output.Raw.GetString("id"),
            type: output.Type,
            text: output.Text,
            imageUrl: output.ImageUrl,
            imagePath: output.ImagePath,
            base64: output.Base64,
            mimeType: output.MimeType,
            revisedPrompt: output.Raw.GetString("revised_prompt") ?? output.Raw.GetString("revisedPrompt"),
            raw: output);
    }
}
