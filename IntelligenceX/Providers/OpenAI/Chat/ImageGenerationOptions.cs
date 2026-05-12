using System;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Options for the Responses API image generation built-in tool.
/// </summary>
public sealed class ImageGenerationOptions {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public ImageGenerationOptions() { }

    /// <summary>
    /// Initializes a new instance by copying values from <paramref name="other"/>.
    /// </summary>
    public ImageGenerationOptions(ImageGenerationOptions other) {
        if (other is null) {
            throw new ArgumentNullException(nameof(other));
        }

        Enabled = other.Enabled;
        Quality = other.Quality;
        Size = other.Size;
        OutputFormat = other.OutputFormat;
        OutputCompression = other.OutputCompression;
        Background = other.Background;
        PartialImages = other.PartialImages;
        OutputDirectory = other.OutputDirectory;
        SaveOutputImages = other.SaveOutputImages;
    }

    /// <summary>
    /// Whether to expose the image generation built-in tool to the model.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional image quality hint, for example <c>low</c>, <c>medium</c>, <c>high</c>, or <c>auto</c>.
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Optional output size, for example <c>1024x1024</c>, <c>1536x1024</c>, or <c>auto</c>.
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Optional output format, for example <c>png</c>, <c>webp</c>, or <c>jpeg</c>.
    /// </summary>
    public string? OutputFormat { get; set; }

    /// <summary>
    /// Optional compression level for compressed formats when supported by the selected image model.
    /// </summary>
    public int? OutputCompression { get; set; }

    /// <summary>
    /// Optional background preference, for example <c>auto</c>, <c>transparent</c>, or <c>opaque</c>.
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Optional streaming partial image count when supported by the backend.
    /// </summary>
    public int? PartialImages { get; set; }

    /// <summary>
    /// Optional directory where generated images should be saved.
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Optional override for whether generated image base64 payloads should be persisted to disk.
    /// </summary>
    public bool? SaveOutputImages { get; set; }

    /// <summary>
    /// Creates a deep copy.
    /// </summary>
    public ImageGenerationOptions Clone() => new(this);
}
