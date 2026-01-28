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
                SetIfEmpty(() => settings.Tone, value => settings.Tone = value, "direct");
                SetIfEmpty(() => settings.Persona, value => settings.Persona = value, "Direct, no-nonsense reviewer");
                SetIfEmpty(() => settings.Notes, value => settings.Notes = value, "Be concise and candid. Avoid emojis.");
                break;
            case "friendly":
                SetIfEmpty(() => settings.Tone, value => settings.Tone = value, "friendly");
                SetIfEmpty(() => settings.Persona, value => settings.Persona = value, "Supportive reviewer who explains tradeoffs");
                SetIfEmpty(() => settings.Notes, value => settings.Notes = value, "Be encouraging and constructive.");
                break;
            case "funny":
            case "witty":
                SetIfEmpty(() => settings.Tone, value => settings.Tone = value, "light, witty");
                SetIfEmpty(() => settings.Persona, value => settings.Persona = value, "Helpful reviewer with light humor");
                SetIfEmpty(() => settings.Notes, value => settings.Notes = value, "Use light humor sparingly. Stay professional.");
                break;
            case "colorful":
            case "emoji":
                SetIfEmpty(() => settings.Tone, value => settings.Tone = value, "expressive");
                SetIfEmpty(() => settings.Persona, value => settings.Persona = value, "Energetic reviewer");
                SetIfEmpty(() => settings.Notes, value => settings.Notes = value, "Use emojis sparingly to highlight key points.");
                break;
            case "formal":
                SetIfEmpty(() => settings.Tone, value => settings.Tone = value, "formal");
                SetIfEmpty(() => settings.Persona, value => settings.Persona = value, "Formal reviewer");
                SetIfEmpty(() => settings.Notes, value => settings.Notes = value, "Keep a professional tone.");
                break;
        }
    }

    private static void SetIfEmpty(Func<string?> getter, Action<string> setter, string value) {
        if (string.IsNullOrWhiteSpace(getter())) {
            setter(value);
        }
    }
}
