using System;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI.Native;

internal static class OpenAINativeImageGenerationEnvironment {
    public static ImageGenerationOptions CreateDefaultOptions() {
        return new ImageGenerationOptions {
            Enabled = ReadBoolean("INTELLIGENCEX_IMAGE_GENERATION_ENABLED") ?? false,
            Quality = ReadString("INTELLIGENCEX_IMAGE_GENERATION_QUALITY"),
            Size = ReadString("INTELLIGENCEX_IMAGE_GENERATION_SIZE"),
            OutputFormat = ReadString("INTELLIGENCEX_IMAGE_GENERATION_OUTPUT_FORMAT"),
            OutputCompression = ReadInt32("INTELLIGENCEX_IMAGE_GENERATION_OUTPUT_COMPRESSION"),
            Background = ReadString("INTELLIGENCEX_IMAGE_GENERATION_BACKGROUND"),
            PartialImages = ReadInt32("INTELLIGENCEX_IMAGE_GENERATION_PARTIAL_IMAGES"),
            OutputDirectory = ReadString("INTELLIGENCEX_IMAGE_GENERATION_OUTPUT_DIRECTORY"),
            SaveOutputImages = ReadBoolean("INTELLIGENCEX_IMAGE_GENERATION_SAVE_OUTPUTS") ?? true
        };
    }

    private static string? ReadString(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static int? ReadInt32(string name) {
        var value = ReadString(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool? ReadBoolean(string name) {
        var value = ReadString(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        if (bool.TryParse(value, out var parsed)) {
            return parsed;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return null;
    }
}
