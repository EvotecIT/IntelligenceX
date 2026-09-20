using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>Protects the distinction between saved configuration and verified runtime identity.</summary>
public sealed class NativeRuntimeContextFormatterTests {
    /// <summary>Stale service metadata must not imply that a failed account is currently active.</summary>
    [Fact]
    public void UnauthenticatedContext_DoesNotPresentOldAccountOrModelAsCurrent() {
        var identity = new SessionRuntimeIdentityDto { Transport = "native", Model = "old-model" };
        var text = NativeRuntimeContextFormatter.Format(identity, "work", "old-account", signedIn: false);
        Assert.DoesNotContain("old-model", text);
        Assert.DoesNotContain("old-account", text);
        Assert.Contains("not confirmed", text);
        Assert.Contains("Profile: work", text);
    }

    /// <summary>Provider-omitted identity remains unknown rather than being taken from profile settings.</summary>
    [Fact]
    public void AuthenticatedContext_WithMissingIdentity_ReportsUnknownFields() {
        var text = NativeRuntimeContextFormatter.Format(null, "local", null, signedIn: true);
        Assert.Contains("Service model not confirmed", text);
        Assert.Contains("Signed in · account not reported", text);
    }

    /// <summary>Confirmed identity explicitly describes the service model, not a per-conversation override.</summary>
    [Fact]
    public void AuthenticatedContext_UsesReportedServiceIdentity() {
        var identity = new SessionRuntimeIdentityDto { Transport = "native", Model = " model-a " };
        var text = NativeRuntimeContextFormatter.Format(identity, "work", " account-a ", signedIn: true);
        Assert.Contains("Service model: model-a", text);
        Assert.Contains("Account: account-a", text);
        Assert.Contains("Profile: work", text);
    }
}
