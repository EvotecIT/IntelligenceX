using System;

namespace IntelligenceX.Reviewer;

internal static class ReviewStyles {
    public static void Apply(string style, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(style)) {
            return;
        }

        var key = style.Trim().ToLowerInvariant();
        switch (key) {
            case "direct":
                SetIfBlank(settings.Tone, value => settings.Tone = value, "direct and concise");
                SetIfBlank(settings.Notes, value => settings.Notes = value, "Avoid filler; keep feedback short and actionable.");
                break;
            case "friendly":
                SetIfBlank(settings.Tone, value => settings.Tone = value, "friendly and supportive");
                break;
            case "funny":
                SetIfBlank(settings.Tone, value => settings.Tone = value, "light humor, professional");
                SetIfBlank(settings.Notes, value => settings.Notes = value, "Use light jokes sparingly; avoid sarcasm.");
                break;
            case "colorful":
                SetIfBlank(settings.Tone, value => settings.Tone = value, "cheerful and lively");
                SetIfBlank(settings.Notes, value => settings.Notes = value, "Use a few relevant emojis to add color.");
                break;
            case "formal":
                SetIfBlank(settings.Tone, value => settings.Tone = value, "formal and professional");
                SetIfBlank(settings.Notes, value => settings.Notes = value, "No emojis; keep a formal tone.");
                break;
        }
    }

    private static void SetIfBlank(string? current, Action<string> set, string value) {
        if (string.IsNullOrWhiteSpace(current)) {
            set(value);
        }
    }
}
