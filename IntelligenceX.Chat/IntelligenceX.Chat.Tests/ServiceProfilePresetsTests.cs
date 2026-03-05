using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Profiles;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ServiceProfilePresetsTests {
    [Fact]
    public void GetBuiltInPresetNames_ReturnsReadOnlyNonArrayView() {
        var names = ServiceProfilePresets.GetBuiltInPresetNames();

        Assert.Equal(new[] { ServiceProfilePresets.PluginOnly }, names);
        Assert.False(names is string[]);
        var list = Assert.IsAssignableFrom<IList<string>>(names);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = "mutated");
    }

    [Fact]
    public void TryResolveStoredOrBuiltInProfile_PrefersStoredProfileOverBuiltInPreset() {
        var storedProfile = new ServiceProfile {
            Model = "stored-model",
            EnableBuiltInPackLoading = true
        };

        var success = ServiceProfilePresets.TryResolveStoredOrBuiltInProfile(
            "plugin-only",
            allowStoredProfiles: true,
            candidateName => string.Equals(candidateName, "plugin-only", StringComparison.Ordinal) ? storedProfile : null,
            out var resolvedName,
            out var profile,
            out var storedProfilesUnavailable);

        Assert.True(success);
        Assert.False(storedProfilesUnavailable);
        Assert.Equal("plugin-only", resolvedName);
        Assert.Same(storedProfile, profile);
        Assert.Equal("stored-model", profile!.Model);
    }

    [Fact]
    public void TryResolveStoredOrBuiltInProfile_ReportsSavedProfilesUnavailable_WhenNoStateDbAndPresetDoesNotExist() {
        var success = ServiceProfilePresets.TryResolveStoredOrBuiltInProfile(
            "custom-profile",
            allowStoredProfiles: false,
            static _ => null,
            out var resolvedName,
            out var profile,
            out var storedProfilesUnavailable);

        Assert.False(success);
        Assert.True(storedProfilesUnavailable);
        Assert.Equal("custom-profile", resolvedName);
        Assert.Null(profile);
    }

    [Fact]
    public void TryResolveStoredOrBuiltInProfile_DoesNotReportSavedProfilesUnavailable_ForBuiltInTypoInNoStateMode() {
        var success = ServiceProfilePresets.TryResolveStoredOrBuiltInProfile(
            "plugin-onli",
            allowStoredProfiles: false,
            static _ => null,
            out var resolvedName,
            out var profile,
            out var storedProfilesUnavailable);

        Assert.False(success);
        Assert.False(storedProfilesUnavailable);
        Assert.Equal("plugin-onli", resolvedName);
        Assert.Null(profile);
    }

    [Fact]
    public void TryResolveStoredOrBuiltInProfile_ResolvesBuiltInAliasInNoStateMode() {
        var success = ServiceProfilePresets.TryResolveStoredOrBuiltInProfile(
            "plugin_only",
            allowStoredProfiles: false,
            static _ => null,
            out var resolvedName,
            out var profile,
            out var storedProfilesUnavailable);

        Assert.True(success);
        Assert.False(storedProfilesUnavailable);
        Assert.Equal("plugin-only", resolvedName);
        Assert.NotNull(profile);
        Assert.False(profile!.EnableBuiltInPackLoading);
        Assert.True(profile.EnableDefaultPluginPaths);
    }

    [Fact]
    public async Task TryResolveStoredOrBuiltInProfileAsync_PrefersStoredAliasCandidateBeforePresetFallback() {
        var storedProfile = new ServiceProfile {
            Model = "stored-alias-model"
        };

        var resolution = await ServiceProfilePresets.TryResolveStoredOrBuiltInProfileAsync(
            "plugin_only",
            allowStoredProfiles: true,
            (candidateName, _) => Task.FromResult<ServiceProfile?>(
                string.Equals(candidateName, "plugin_only", StringComparison.Ordinal) ? storedProfile : null),
            CancellationToken.None);

        Assert.True(resolution.Success);
        Assert.False(resolution.StoredProfilesUnavailable);
        Assert.Equal("plugin_only", resolution.ResolvedName);
        Assert.Same(storedProfile, resolution.Profile);
        Assert.Equal("stored-alias-model", resolution.Profile!.Model);
    }

    [Fact]
    public async Task TryResolveStoredOrBuiltInProfileAsync_DoesNotReportSavedProfilesUnavailable_ForBuiltInTypoInNoStateMode() {
        var resolution = await ServiceProfilePresets.TryResolveStoredOrBuiltInProfileAsync(
            "plugin-onli",
            allowStoredProfiles: false,
            static (_, _) => Task.FromResult<ServiceProfile?>(null),
            CancellationToken.None);

        Assert.False(resolution.Success);
        Assert.False(resolution.StoredProfilesUnavailable);
        Assert.Equal("plugin-onli", resolution.ResolvedName);
        Assert.Null(resolution.Profile);
    }

    [Fact]
    public async Task TryResolveStoredOrBuiltInProfileAsync_ResolvesBuiltInAliasInNoStateMode() {
        var resolution = await ServiceProfilePresets.TryResolveStoredOrBuiltInProfileAsync(
            "plugin_only",
            allowStoredProfiles: false,
            static (_, _) => Task.FromResult<ServiceProfile?>(null),
            CancellationToken.None);

        Assert.True(resolution.Success);
        Assert.False(resolution.StoredProfilesUnavailable);
        Assert.Equal("plugin-only", resolution.ResolvedName);
        Assert.NotNull(resolution.Profile);
        Assert.False(resolution.Profile!.EnableBuiltInPackLoading);
        Assert.True(resolution.Profile.EnableDefaultPluginPaths);
    }

    [Fact]
    public void MergeBuiltInPresetNames_DedupesStoredCollisionsCaseInsensitively() {
        var merged = ServiceProfilePresets.MergeBuiltInPresetNames(new[] {
            "PLUGIN-ONLY",
            "custom-profile",
            "plugin_only",
            "Custom-Profile"
        });

        Assert.Equal(new[] { "plugin-only", "custom-profile" }, merged);
    }
}
