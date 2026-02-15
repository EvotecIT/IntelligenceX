using System;
using System.Globalization;
using System.Reflection;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

[CollectionDefinition("CultureSensitive", DisableParallelization = true)]
public sealed class CultureSensitiveCollectionDefinition { }

[Collection("CultureSensitive")]
public sealed class CultureInvariantTests {
    private static readonly MethodInfo CanonicalizeImplicitPendingActionConfirmationPhraseMethod =
        typeof(ChatServiceSession).GetMethod("CanonicalizeImplicitPendingActionConfirmationPhrase", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CanonicalizeImplicitPendingActionConfirmationPhrase not found.");

    [Fact]
    public void CanonicalizeImplicitPendingActionConfirmationPhrase_UsesInvariantCasingUnderTurkishCulture() {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            // Under Turkish culture, ToLower() would map 'I' -> 'ı' (dotless i). We want invariant casing.
            var normalized = CanonicalizeImplicitPendingActionConfirmationPhraseMethod.Invoke(null, new object?[] { "I" });
            Assert.Equal("i", Assert.IsType<string>(normalized));
        } finally {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}

