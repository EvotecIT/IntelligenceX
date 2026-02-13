using System;
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
}
