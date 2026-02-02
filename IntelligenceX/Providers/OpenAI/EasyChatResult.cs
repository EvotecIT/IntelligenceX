using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Represents the simplified result of an easy chat call.
/// </summary>
public sealed class EasyChatResult {
    /// <summary>
    /// Initializes a new easy chat result.
    /// </summary>
    public EasyChatResult(string? text, IReadOnlyList<string> textBlocks, IReadOnlyList<EasyImage> images, TurnInfo turn) {
        Text = text;
        TextBlocks = textBlocks;
        Images = images;
        Turn = turn;
    }

    /// <summary>
    /// Gets the concatenated text response.
    /// </summary>
    public string? Text { get; }
    /// <summary>
    /// Gets the individual text blocks.
    /// </summary>
    public IReadOnlyList<string> TextBlocks { get; }
    /// <summary>
    /// Gets any image outputs.
    /// </summary>
    public IReadOnlyList<EasyImage> Images { get; }
    /// <summary>
    /// Gets the underlying turn information.
    /// </summary>
    public TurnInfo Turn { get; }

    /// <summary>
    /// Creates an <see cref="EasyChatResult"/> from a turn.
    /// </summary>
    /// <param name="turn">Turn info to convert.</param>
    public static EasyChatResult FromTurn(TurnInfo turn) {
        var textBlocks = new List<string>();
        foreach (var output in turn.Outputs) {
            if (output.IsText && !string.IsNullOrWhiteSpace(output.Text)) {
                textBlocks.Add(output.Text!);
            }
        }

        var images = new List<EasyImage>();
        foreach (var output in turn.ImageOutputs) {
            images.Add(EasyImage.FromOutput(output));
        }

        var text = textBlocks.Count == 0 ? null : string.Join(Environment.NewLine, textBlocks);
        return new EasyChatResult(text, textBlocks, images, turn);
    }
}

/// <summary>
/// Represents an image output from a chat call.
/// </summary>
public sealed class EasyImage {
    /// <summary>
    /// Initializes a new image output.
    /// </summary>
    public EasyImage(string? url, string? path, string? base64, string? mimeType) {
        Url = url;
        Path = path;
        Base64 = base64;
        MimeType = mimeType;
    }

    /// <summary>
    /// Gets the image URL when available.
    /// </summary>
    public string? Url { get; }
    /// <summary>
    /// Gets the local file path when available.
    /// </summary>
    public string? Path { get; }
    /// <summary>
    /// Gets the base64 payload when provided.
    /// </summary>
    public string? Base64 { get; }
    /// <summary>
    /// Gets the mime type when available.
    /// </summary>
    public string? MimeType { get; }

    internal static EasyImage FromOutput(TurnOutput output) {
        return new EasyImage(output.ImageUrl, output.ImagePath, output.Base64, output.MimeType);
    }
}
