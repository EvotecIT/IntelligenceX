using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Simplified view of a chat turn, including text blocks and image outputs.
/// </summary>
public sealed class EasyChatResult {
    public EasyChatResult(string? text, IReadOnlyList<string> textBlocks, IReadOnlyList<EasyImage> images, TurnInfo turn) {
        Text = text;
        TextBlocks = textBlocks;
        Images = images;
        Turn = turn;
    }

    /// <summary>
    /// Combined text output (if any).
    /// </summary>
    public string? Text { get; }
    /// <summary>
    /// Individual text blocks returned by the model.
    /// </summary>
    public IReadOnlyList<string> TextBlocks { get; }
    /// <summary>
    /// Image outputs returned by the model.
    /// </summary>
    public IReadOnlyList<EasyImage> Images { get; }
    /// <summary>
    /// Raw turn info for advanced use cases.
    /// </summary>
    public TurnInfo Turn { get; }

    /// <summary>
    /// Builds a result from a raw turn.
    /// </summary>
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
/// Simplified image output from a chat turn.
/// </summary>
public sealed class EasyImage {
    public EasyImage(string? url, string? path, string? base64, string? mimeType) {
        Url = url;
        Path = path;
        Base64 = base64;
        MimeType = mimeType;
    }

    /// <summary>
    /// Public URL for the image (if provided).
    /// </summary>
    public string? Url { get; }
    /// <summary>
    /// Local file path for the image (if written to disk).
    /// </summary>
    public string? Path { get; }
    /// <summary>
    /// Base64 image data (if provided).
    /// </summary>
    public string? Base64 { get; }
    /// <summary>
    /// MIME type for the image (if known).
    /// </summary>
    public string? MimeType { get; }

    internal static EasyImage FromOutput(TurnOutput output) {
        return new EasyImage(output.ImageUrl, output.ImagePath, output.Base64, output.MimeType);
    }
}
