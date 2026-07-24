using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal static ChatImageOutputDto[]? BuildChatImageOutputs(TurnInfo turn) {
        var images = new List<ChatImageOutputDto>(turn.ImageOutputs.Count);
        CaptureChatImageOutputs(images, turn);
        return SnapshotChatImageOutputs(images);
    }

    internal static void CaptureChatImageOutputs(List<ChatImageOutputDto> images, TurnInfo turn) {
        foreach (var output in turn.ImageOutputs) {
            if (string.IsNullOrWhiteSpace(output.ImageUrl) &&
                string.IsNullOrWhiteSpace(output.ImagePath)) {
                continue;
            }

            var url = NormalizeImageOutputValue(output.ImageUrl);
            var path = NormalizeImageOutputValue(output.ImagePath);
            var duplicate = false;
            for (var i = 0; i < images.Count; i++) {
                if (string.Equals(images[i].Url, url, System.StringComparison.Ordinal) &&
                    string.Equals(images[i].Path, path, System.StringComparison.Ordinal)) {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate) {
                images.Add(new ChatImageOutputDto {
                    Url = url,
                    Path = path,
                    MimeType = NormalizeImageOutputValue(output.MimeType)
                });
            }
        }
    }

    internal static ChatImageOutputDto[]? SnapshotChatImageOutputs(IReadOnlyList<ChatImageOutputDto> images) {
        return images.Count == 0 ? null : images.ToArray();
    }

    private static string? NormalizeImageOutputValue(string? value) {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
