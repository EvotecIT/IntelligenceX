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
            case "blunt":
                SetIfEmpty(ref settings.Tone, "direct");
                SetIfEmpty(ref settings.Persona, "Direct, no-nonsense reviewer");
                SetIfEmpty(ref settings.Notes, "Be concise and candid. Avoid emojis.");
                break;
            case "friendly":
                SetIfEmpty(ref settings.Tone, "friendly");
                SetIfEmpty(ref settings.Persona, "Supportive reviewer who explains tradeoffs");
                SetIfEmpty(ref settings.Notes, "Be encouraging and constructive.");
                break;
            case "funny":
            case "witty":
                SetIfEmpty(ref settings.Tone, "light, witty");
                SetIfEmpty(ref settings.Persona, "Helpful reviewer with light humor");
                SetIfEmpty(ref settings.Notes, "Use light humor sparingly. Stay professional.");
                break;
            case "colorful":
            case "emoji":
                SetIfEmpty(ref settings.Tone, "expressive");
                SetIfEmpty(ref settings.Persona, "Energetic reviewer");
                SetIfEmpty(ref settings.Notes, "Use emojis sparingly to highlight key points.");
                break;
            case "formal":
                SetIfEmpty(ref settings.Tone, "formal");
                SetIfEmpty(ref settings.Persona, "Formal reviewer");
                SetIfEmpty(ref settings.Notes, "Keep a professional tone.");
                break;
        }
    }

    private static void SetIfEmpty(ref string? target, string value) {
        if (string.IsNullOrWhiteSpace(target)) {
            target = value;
        }
    }
}
