using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI;

public sealed class EasyChatResult {
    public EasyChatResult(string? text, IReadOnlyList<string> textBlocks, IReadOnlyList<EasyImage> images, TurnInfo turn) {
        Text = text;
        TextBlocks = textBlocks;
        Images = images;
        Turn = turn;
    }

    public string? Text { get; }
    public IReadOnlyList<string> TextBlocks { get; }
    public IReadOnlyList<EasyImage> Images { get; }
    public TurnInfo Turn { get; }

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

public sealed class EasyImage {
    public EasyImage(string? url, string? path, string? base64, string? mimeType) {
        Url = url;
        Path = path;
        Base64 = base64;
        MimeType = mimeType;
    }

    public string? Url { get; }
    public string? Path { get; }
    public string? Base64 { get; }
    public string? MimeType { get; }

    internal static EasyImage FromOutput(TurnOutput output) {
        return new EasyImage(output.ImageUrl, output.ImagePath, output.Base64, output.MimeType);
    }
}
