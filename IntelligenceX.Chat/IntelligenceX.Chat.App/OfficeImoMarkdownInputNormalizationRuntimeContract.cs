using System;
using System.Collections.Generic;
using System.Reflection;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Owns optional OfficeIMO markdown input-normalizer discovery for transcript cleanup.
/// </summary>
internal static class OfficeImoMarkdownInputNormalizationRuntimeContract {
    private static readonly Lazy<OfficeImoInputNormalizationBridge?> OfficeImoInputNormalizationBridgeLazy =
        new(CreateOfficeImoInputNormalizationBridge);

    private static readonly string[] OfficeImoInputNormalizationPropertyNames = [
        "NormalizeLooseStrongDelimiters",
        "NormalizeTightStrongBoundaries",
        "NormalizeOrderedListMarkerSpacing",
        "NormalizeOrderedListParenMarkers",
        "NormalizeOrderedListCaretArtifacts",
        "NormalizeTightParentheticalSpacing",
        "NormalizeNestedStrongDelimiters",
        "NormalizeTightArrowStrongBoundaries",
        "NormalizeTightColonSpacing"
    ];

    public static string NormalizeForTranscriptCleanup(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var bridge = OfficeImoInputNormalizationBridgeLazy.Value;
        if (bridge == null) {
            return text;
        }

        return bridge.Normalize(text);
    }

    private static OfficeImoInputNormalizationBridge? CreateOfficeImoInputNormalizationBridge() {
        try {
            var optionsType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizationOptions, OfficeIMO.Markdown", throwOnError: false);
            var normalizerType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizer, OfficeIMO.Markdown", throwOnError: false);
            if (optionsType == null || normalizerType == null) {
                return null;
            }

            var normalizeMethod = normalizerType.GetMethod(
                "Normalize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), optionsType],
                modifiers: null);
            if (normalizeMethod == null) {
                return null;
            }

            var presetFactoryMethod = ResolveOfficeImoInputNormalizationPresetFactory(optionsType);
            var enabledProperties = new List<PropertyInfo>(OfficeImoInputNormalizationPropertyNames.Length);
            foreach (var propertyName in OfficeImoInputNormalizationPropertyNames) {
                var property = optionsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is { CanWrite: true } && property.PropertyType == typeof(bool)) {
                    enabledProperties.Add(property);
                }
            }

            if (presetFactoryMethod == null && enabledProperties.Count == 0) {
                return null;
            }

            return new OfficeImoInputNormalizationBridge(optionsType, normalizeMethod, presetFactoryMethod, enabledProperties.ToArray());
        } catch {
            return null;
        }
    }

    private static MethodInfo? ResolveOfficeImoInputNormalizationPresetFactory(Type optionsType) {
        try {
            var presetsType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizationPresets, OfficeIMO.Markdown", throwOnError: false);
            if (presetsType == null) {
                return null;
            }

            var createChatTranscriptMethod = presetsType.GetMethod(
                "CreateChatTranscript",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (createChatTranscriptMethod == null || !optionsType.IsAssignableFrom(createChatTranscriptMethod.ReturnType)) {
                return null;
            }

            return createChatTranscriptMethod;
        } catch {
            return null;
        }
    }

    private sealed class OfficeImoInputNormalizationBridge(Type optionsType, MethodInfo normalizeMethod, MethodInfo? presetFactoryMethod, PropertyInfo[] enabledProperties) {
        public string Normalize(string text) {
            try {
                var options = CreateOptionsInstance(out var usedPresetFactory);
                if (options == null) {
                    return text;
                }

                if (!usedPresetFactory) {
                    for (var i = 0; i < enabledProperties.Length; i++) {
                        enabledProperties[i].SetValue(options, true);
                    }
                }

                var normalized = normalizeMethod.Invoke(null, [text, options]) as string;
                return string.IsNullOrEmpty(normalized) ? text : normalized;
            } catch {
                return text;
            }
        }

        private object? CreateOptionsInstance(out bool usedPresetFactory) {
            usedPresetFactory = false;
            try {
                if (presetFactoryMethod != null) {
                    var presetOptions = presetFactoryMethod.Invoke(null, null);
                    if (presetOptions != null && optionsType.IsInstanceOfType(presetOptions)) {
                        usedPresetFactory = true;
                        return presetOptions;
                    }
                }
            } catch {
                // Fall back to the legacy property-enabling path below.
            }

            return Activator.CreateInstance(optionsType);
        }
    }
}
