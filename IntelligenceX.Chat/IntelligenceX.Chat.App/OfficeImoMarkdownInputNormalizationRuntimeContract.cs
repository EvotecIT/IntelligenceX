using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralizes the OfficeIMO transcript input-normalization contract used during transcript cleanup.
/// </summary>
internal static class OfficeImoMarkdownInputNormalizationRuntimeContract {
    private static readonly Lazy<OfficeImoInputNormalizationBridge?> OfficeImoInputNormalizationBridgeLazy =
        new(CreateOfficeImoInputNormalizationBridge);

    private static readonly string[] OfficeImoInputNormalizationPropertyNames = [
        "NormalizeZeroWidthSpacingArtifacts",
        "NormalizeEmojiWordJoins",
        "NormalizeCompactNumberedChoiceBoundaries",
        "NormalizeSentenceCollapsedBullets",
        "NormalizeLooseStrongDelimiters",
        "NormalizeTightStrongBoundaries",
        "NormalizeOrderedListMarkerSpacing",
        "NormalizeOrderedListParenMarkers",
        "NormalizeOrderedListCaretArtifacts",
        "NormalizeTightParentheticalSpacing",
        "NormalizeNestedStrongDelimiters",
        "NormalizeTightArrowStrongBoundaries",
        "NormalizeBrokenStrongArrowLabels",
        "NormalizeTightColonSpacing",
        "NormalizeWrappedSignalFlowStrongRuns",
        "NormalizeSignalFlowLabelSpacing",
        "NormalizeCollapsedMetricChains",
        "NormalizeHostLabelBulletArtifacts",
        "NormalizeHeadingListBoundaries",
        "NormalizeCompactStrongLabelListBoundaries",
        "NormalizeCompactHeadingBoundaries",
        "NormalizeStandaloneHashHeadingSeparators",
        "NormalizeBrokenTwoLineStrongLeadIns",
        "NormalizeColonListBoundaries",
        "NormalizeCompactFenceBodyBoundaries",
        "NormalizeCollapsedOrderedListBoundaries",
        "NormalizeOrderedListStrongDetailClosures",
        "NormalizeDanglingTrailingStrongListClosers",
        "NormalizeMetricValueStrongRuns"
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
            var normalizerType = typeof(MarkdownInputNormalizer);
            if (optionsType == null) {
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
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            return null;
        }
    }

    private static MethodInfo? ResolveOfficeImoInputNormalizationPresetFactory(Type optionsType) {
        try {
            var presetsType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizationPresets, OfficeIMO.Markdown", throwOnError: false);
            if (presetsType == null) {
                return null;
            }

            foreach (var factoryName in new[] { "CreateIntelligenceXTranscript", "CreateChatTranscript" }) {
                var factory = presetsType.GetMethod(
                    factoryName,
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (factory != null && optionsType.IsAssignableFrom(factory.ReturnType)) {
                    return factory;
                }
            }

            return null;
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            return null;
        }
    }

    private static bool IsCompatibilityFallbackException(Exception exception) {
        var unwrapped = UnwrapInvocationException(exception);
        return unwrapped is TypeLoadException
            or FileNotFoundException
            or FileLoadException
            or BadImageFormatException
            or MissingMethodException
            or MissingMemberException
            or MemberAccessException
            or NotSupportedException
            or InvalidCastException;
    }

    private static Exception UnwrapInvocationException(Exception exception) {
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } invocationException) {
            current = invocationException.InnerException!;
        }

        return current;
    }

    private sealed class OfficeImoInputNormalizationBridge(
        Type optionsType,
        MethodInfo normalizeMethod,
        MethodInfo? presetFactoryMethod,
        PropertyInfo[] enabledProperties) {
        public string Normalize(string text) {
            try {
                var options = CreateOptionsInstance();
                if (options == null) {
                    return text;
                }

                for (var i = 0; i < enabledProperties.Length; i++) {
                    enabledProperties[i].SetValue(options, true);
                }

                var normalized = normalizeMethod.Invoke(null, [text, options]) as string;
                return normalized ?? text;
            } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
                return text;
            }
        }

        private object? CreateOptionsInstance() {
            try {
                if (presetFactoryMethod != null) {
                    var presetOptions = presetFactoryMethod.Invoke(null, null);
                    if (presetOptions != null && optionsType.IsInstanceOfType(presetOptions)) {
                        return presetOptions;
                    }
                }
            } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
                // Fall through to legacy property-based construction below.
            }

            return Activator.CreateInstance(optionsType);
        }
    }
}
