using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.ScheduledTasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class SystemScheduledTaskLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_BlankTaskPath_ShouldFailRequiredValidation() {
        var tool = new SystemScheduledTaskLifecycleTool(new SystemToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("task_path", "   ")
                .Add("operation", "disable")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("task_path is required", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_RootTaskPath_ShouldFailExactPathValidation() {
        var tool = new SystemScheduledTaskLifecycleTool(new SystemToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("task_path", "\\")
                .Add("operation", "disable")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("task_path must identify an exact scheduled task path", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunKnownTask_ShouldReturnWritePreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        ScheduledTaskInfo? knownTask;
        try {
            knownTask = TaskSchedulerQuery.Get()
                .FirstOrDefault(static task => !string.IsNullOrWhiteSpace(task.Path));
        } catch {
            return;
        }

        if (knownTask is null) {
            return;
        }

        var tool = new SystemScheduledTaskLifecycleTool(new SystemToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("task_path", knownTask.Path)
                .Add("operation", knownTask.Enabled ? "disable" : "enable")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(knownTask.Path, root.GetProperty("task_path").GetString());
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("write_candidate").GetBoolean());
        Assert.True(root.TryGetProperty("before", out var before));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, before.ValueKind);
        Assert.True(root.TryGetProperty("after", out var after));
        Assert.True(
            after.ValueKind == global::System.Text.Json.JsonValueKind.Object
            || after.ValueKind == global::System.Text.Json.JsonValueKind.Null);
    }
}
