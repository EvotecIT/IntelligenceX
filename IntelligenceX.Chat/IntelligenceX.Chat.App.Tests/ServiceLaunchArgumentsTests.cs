using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.App.Launch;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for typed service launch argument construction.
/// </summary>
public sealed class ServiceLaunchArgumentsTests {
    /// <summary>
    /// Ensures pipe and parent-lifecycle flags are included in attached mode.
    /// </summary>
    [Fact]
    public void Build_IncludesLifecycleFlags_WhenNotDetached() {
        var args = ServiceLaunchArguments.Build("intelligencex.chat", detachedServiceMode: false, parentProcessId: 12345);

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat",
            "--exit-on-disconnect",
            "--parent-pid",
            "12345"
        }, args);
    }

    /// <summary>
    /// Ensures detached mode only carries the pipe argument pair.
    /// </summary>
    [Fact]
    public void Build_OmitsLifecycleFlags_WhenDetached() {
        var args = ServiceLaunchArguments.Build("intelligencex.chat", detachedServiceMode: true, parentProcessId: 12345);

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat"
        }, args);
    }

    /// <summary>
    /// Ensures an empty pipe name is rejected.
    /// </summary>
    [Fact]
    public void Build_Throws_WhenPipeNameMissing() {
        Assert.Throws<ArgumentException>(() => ServiceLaunchArguments.Build("  ", detachedServiceMode: false, parentProcessId: 42));
    }

    /// <summary>
    /// Ensures profile/runtime overrides are emitted when provided.
    /// </summary>
    [Fact]
    public void Build_IncludesProfileAndTransportOverrides_WhenConfigured() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                LoadProfileName = "default",
                SaveProfileName = "default",
                Model = "gpt-4.1-mini",
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:11434",
                OpenAIApiKey = "token",
                OpenAIStreaming = true,
                OpenAIAllowInsecureHttp = true
            });

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat",
            "--profile",
            "default",
            "--save-profile",
            "default",
            "--model",
            "gpt-4.1-mini",
            "--openai-transport",
            "compatible-http",
            "--openai-base-url",
            "http://127.0.0.1:11434",
            "--openai-api-key",
            "token",
            "--openai-stream",
            "--openai-allow-insecure-http"
        }, args);
    }

    /// <summary>
    /// Ensures explicit API-key clearing emits a dedicated clear flag.
    /// </summary>
    [Fact]
    public void Build_IncludesClearApiKeyFlag_WhenRequested() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                LoadProfileName = "default",
                SaveProfileName = "default",
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:1234/v1",
                ClearOpenAIApiKey = true
            });

        Assert.DoesNotContain("--openai-api-key", args);
        Assert.Contains("--openai-clear-api-key", args);
    }

    /// <summary>
    /// Ensures runtime pack toggles are emitted when requested.
    /// </summary>
    [Fact]
    public void Build_IncludesRuntimePackToggleFlags_WhenConfigured() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                PackToggles = new[] {
                    new ServiceLaunchArguments.PackToggle("powershell", true),
                    new ServiceLaunchArguments.PackToggle("testimox", false),
                    new ServiceLaunchArguments.PackToggle("officeimo", true)
                }
            });

        Assert.Equal(new[] { "officeimo", "powershell" }, ExtractArgumentValues(args, "--enable-pack-id"));
        Assert.Equal(new[] { "testimox" }, ExtractArgumentValues(args, "--disable-pack-id"));
    }

    /// <summary>
    /// Ensures pack toggle arguments normalize ids and honor the last toggle per id.
    /// </summary>
    [Fact]
    public void Build_NormalizesAndDeDuplicatesRuntimePackToggleFlags_WhenConfigured() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                PackToggles = new[] {
                    new ServiceLaunchArguments.PackToggle("Power-Shell", true),
                    new ServiceLaunchArguments.PackToggle("power_shell", false),
                    new ServiceLaunchArguments.PackToggle(" testimo x ", true)
                }
            });

        Assert.Equal(new[] { "testimox" }, ExtractArgumentValues(args, "--enable-pack-id"));
        Assert.Equal(new[] { "powershell" }, ExtractArgumentValues(args, "--disable-pack-id"));
    }

    /// <summary>
    /// Ensures runtime pack toggle arguments use canonical shared Chat pack ids for alias inputs.
    /// </summary>
    [Fact]
    public void Build_NormalizesRuntimePackToggleAliases_ToCanonicalPackIds() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                PackToggles = new[] {
                    new ServiceLaunchArguments.PackToggle("ADPlayground", true),
                    new ServiceLaunchArguments.PackToggle("ComputerX", false),
                    new ServiceLaunchArguments.PackToggle("fs", true)
                }
            });

        Assert.Equal(new[] { "active_directory", "filesystem" }, ExtractArgumentValues(args, "--enable-pack-id"));
        Assert.Equal(new[] { "system" }, ExtractArgumentValues(args, "--disable-pack-id"));
    }

    /// <summary>
    /// Ensures unknown transport values are rejected.
    /// </summary>
    [Fact]
    public void Build_Throws_WhenTransportUnknown() {
        Assert.Throws<ArgumentException>(() => ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions { OpenAITransport = "invalid" }));
    }

    /// <summary>
    /// Ensures Copilot transport aliases normalize to copilot-cli.
    /// </summary>
    [Theory]
    [InlineData("copilot")]
    [InlineData("copilot-cli")]
    [InlineData("github-copilot")]
    [InlineData("githubcopilot")]
    public void Build_NormalizesCopilotTransportAliases(string inputTransport) {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions { OpenAITransport = inputTransport });

        var transportIndex = -1;
        for (var i = 0; i < args.Count; i++) {
            if (!string.Equals(args[i], "--openai-transport", StringComparison.Ordinal)) {
                continue;
            }
            transportIndex = i;
            break;
        }
        Assert.True(transportIndex >= 0);
        Assert.True(transportIndex + 1 < args.Count);
        Assert.Equal("copilot-cli", args[transportIndex + 1]);
    }

    /// <summary>
    /// Ensures compatible-http auth mode + basic username are emitted, while password is not passed on CLI args.
    /// </summary>
    [Fact]
    public void Build_IncludesCompatibleHttpAuthModeAndBasicUsername_ButOmitsBasicPasswordArg_WhenConfigured() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:1234/v1",
                OpenAIAuthMode = "basic",
                OpenAIBasicUsername = "user",
                OpenAIBasicPassword = "secret"
            });

        Assert.Contains("--openai-auth-mode", args);
        Assert.Contains("basic", args);
        Assert.Contains("--openai-basic-username", args);
        Assert.Contains("user", args);
        Assert.DoesNotContain("--openai-basic-password", args);
        Assert.DoesNotContain("secret", args);
    }

    /// <summary>
    /// Ensures explicit Basic-auth clearing emits a dedicated clear flag.
    /// </summary>
    [Fact]
    public void Build_IncludesClearBasicAuthFlag_WhenRequested() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:1234/v1",
                ClearOpenAIBasicAuth = true
            });

        Assert.DoesNotContain("--openai-basic-username", args);
        Assert.DoesNotContain("--openai-basic-password", args);
        Assert.Contains("--openai-clear-basic-auth", args);
    }

    /// <summary>
    /// Ensures repeatable plugin paths are forwarded, while empty/duplicate entries are ignored.
    /// </summary>
    [Fact]
    public void Build_IncludesPluginPathFlags_WhenConfigured() {
        var unique = Guid.NewGuid().ToString("N");
        var mainPath = Path.GetFullPath(TempPathTestHelper.CreateTempDirectoryPath("ix-launch-main-" + unique));
        var extraPath = Path.GetFullPath(TempPathTestHelper.CreateTempDirectoryPath("ix-launch-extra-" + unique));
        var relativeRoot = Path.Combine(Directory.GetCurrentDirectory(), "ix-launch-relative-" + unique);
        var relativePluginPath = Path.Combine(relativeRoot, "plugins", "relative");
        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(extraPath);
        Directory.CreateDirectory(relativePluginPath);

        try {
            var expectedAbsoluteMain = NormalizeForAssertion(mainPath);
            var expectedAbsoluteRelative = NormalizeForAssertion(Path.GetFullPath(relativePluginPath));
            var expectedAbsoluteExtra = NormalizeForAssertion(extraPath);
            var mainPathWithTrailingSeparator = mainPath + Path.DirectorySeparatorChar;
            var mainPathWithDotSegment = Path.GetFullPath(Path.Combine(mainPath, "..", Path.GetFileName(mainPath)));
            var relativeEquivalent = Path.GetFullPath(Path.Combine(relativePluginPath, "..", "relative"));
            var extraPathWithTrailingSeparator = extraPath + Path.DirectorySeparatorChar;

            var args = ServiceLaunchArguments.Build(
                "intelligencex.chat",
                detachedServiceMode: true,
                parentProcessId: 12345,
                profileOptions: null,
                additionalPluginPaths: new[] {
                    "  " + mainPathWithTrailingSeparator + "  ",
                    mainPathWithDotSegment,
                    string.Empty,
                    relativePluginPath,
                    relativeEquivalent,
                    extraPathWithTrailingSeparator
                });

            var pluginPaths = ExtractArgumentValues(args, "--plugin-path");
            Assert.Equal(new[] {
                expectedAbsoluteMain,
                expectedAbsoluteRelative,
                expectedAbsoluteExtra
            }, pluginPaths);
        } finally {
            if (Directory.Exists(mainPath)) {
                Directory.Delete(mainPath, recursive: true);
            }

            if (Directory.Exists(extraPath)) {
                Directory.Delete(extraPath, recursive: true);
            }

            if (Directory.Exists(relativeRoot)) {
                Directory.Delete(relativeRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Ensures runtime-only built-in tool probe paths and workspace probing are forwarded to the service.
    /// </summary>
    [Fact]
    public void Build_IncludesBuiltInToolProbePathsAndWorkspaceProbing_WhenConfigured() {
        var unique = Guid.NewGuid().ToString("N");
        var mainPath = Path.GetFullPath(TempPathTestHelper.CreateTempDirectoryPath("ix-built-in-main-" + unique));
        var nestedToolsPath = Path.Combine(mainPath, "tools");
        Directory.CreateDirectory(nestedToolsPath);

        try {
            var args = ServiceLaunchArguments.Build(
                "intelligencex.chat",
                detachedServiceMode: true,
                parentProcessId: 12345,
                profileOptions: null,
                additionalPluginPaths: null,
                additionalBuiltInToolProbePaths: new[] {
                    mainPath + Path.DirectorySeparatorChar,
                    nestedToolsPath,
                    Path.GetFullPath(Path.Combine(mainPath, ".", "tools"))
                },
                enableWorkspaceBuiltInToolOutputProbing: true);

            Assert.Equal(
                new[] {
                    NormalizeForAssertion(mainPath),
                    NormalizeForAssertion(nestedToolsPath)
                },
                ExtractArgumentValues(args, "--built-in-tool-probe-path"));
            Assert.Contains("--enable-workspace-built-in-tool-output-probing", args);
        } finally {
            if (Directory.Exists(mainPath)) {
                Directory.Delete(mainPath, recursive: true);
            }
        }
    }

    private static IReadOnlyList<string> ExtractArgumentValues(IReadOnlyList<string> args, string key) {
        var values = new List<string>();
        for (var i = 0; i < args.Count - 1; i++) {
            if (!string.Equals(args[i], key, StringComparison.Ordinal)) {
                continue;
            }

            values.Add(args[i + 1]);
            i++;
        }

        return values;
    }

    private static string NormalizeForAssertion(string path) {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.Length == 0) {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)) {
            var normalizedRoot = root!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
                return root!;
            }
        }

        return normalized;
    }
}
