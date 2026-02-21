using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards autonomy override persistence/load synchronization.
/// </summary>
public sealed class MainWindowAutonomyPersistenceTests {
    private static readonly FieldInfo AppStateField = typeof(MainWindow).GetField(
        "_appState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_appState field not found.");

    private static readonly FieldInfo AutonomyPlanExecuteReviewLoopField = typeof(MainWindow).GetField(
        "_autonomyPlanExecuteReviewLoop",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_autonomyPlanExecuteReviewLoop field not found.");

    private static readonly FieldInfo AutonomyMaxToolRoundsField = typeof(MainWindow).GetField(
        "_autonomyMaxToolRounds",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_autonomyMaxToolRounds field not found.");

    private static readonly FieldInfo AutonomyMaxCandidateToolsField = typeof(MainWindow).GetField(
        "_autonomyMaxCandidateTools",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_autonomyMaxCandidateTools field not found.");

    private static readonly FieldInfo AutonomyMaxReviewPassesField = typeof(MainWindow).GetField(
        "_autonomyMaxReviewPasses",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_autonomyMaxReviewPasses field not found.");

    private static readonly FieldInfo AutonomyModelHeartbeatSecondsField = typeof(MainWindow).GetField(
        "_autonomyModelHeartbeatSeconds",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_autonomyModelHeartbeatSeconds field not found.");

    private static readonly MethodInfo CaptureAutonomyOverridesIntoAppStateMethod = typeof(MainWindow).GetMethod(
        "CaptureAutonomyOverridesIntoAppState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CaptureAutonomyOverridesIntoAppState not found.");

    private static readonly MethodInfo RestoreAutonomyOverridesFromAppStateMethod = typeof(MainWindow).GetMethod(
        "RestoreAutonomyOverridesFromAppState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RestoreAutonomyOverridesFromAppState not found.");

    /// <summary>
    /// Ensures review-loop autonomy settings survive capture/restore through app state.
    /// </summary>
    [Fact]
    public void AutonomyReviewLoopOverrides_RoundTripThroughAppState() {
        var window = CreateWindowWithState();
        var state = Assert.IsType<ChatAppState>(AppStateField.GetValue(window));

        AutonomyPlanExecuteReviewLoopField.SetValue(window, true);
        AutonomyMaxReviewPassesField.SetValue(window, 2);
        AutonomyModelHeartbeatSecondsField.SetValue(window, 15);

        Invoke(CaptureAutonomyOverridesIntoAppStateMethod, window);

        Assert.True(state.AutonomyPlanExecuteReviewLoop);
        Assert.Equal(2, state.AutonomyMaxReviewPasses);
        Assert.Equal(15, state.AutonomyModelHeartbeatSeconds);

        AutonomyPlanExecuteReviewLoopField.SetValue(window, null);
        AutonomyMaxReviewPassesField.SetValue(window, null);
        AutonomyModelHeartbeatSecondsField.SetValue(window, null);

        Invoke(RestoreAutonomyOverridesFromAppStateMethod, window);

        Assert.Equal(true, ReadNullableBool(AutonomyPlanExecuteReviewLoopField, window));
        Assert.Equal(2, ReadNullableInt(AutonomyMaxReviewPassesField, window));
        Assert.Equal(15, ReadNullableInt(AutonomyModelHeartbeatSecondsField, window));
    }

    /// <summary>
    /// Ensures restore path sanitizes out-of-range review-loop values from persisted state.
    /// </summary>
    [Fact]
    public void AutonomyReviewLoopOverrides_RestoreSanitizesOutOfRangeValues() {
        var window = CreateWindowWithState();
        var state = Assert.IsType<ChatAppState>(AppStateField.GetValue(window));
        state.AutonomyPlanExecuteReviewLoop = false;
        state.AutonomyMaxReviewPasses = 99;
        state.AutonomyModelHeartbeatSeconds = 999;

        Invoke(RestoreAutonomyOverridesFromAppStateMethod, window);

        Assert.Equal(false, ReadNullableBool(AutonomyPlanExecuteReviewLoopField, window));
        Assert.Null(ReadNullableInt(AutonomyMaxReviewPassesField, window));
        Assert.Null(ReadNullableInt(AutonomyModelHeartbeatSecondsField, window));
        Assert.Equal(false, state.AutonomyPlanExecuteReviewLoop);
        Assert.Null(state.AutonomyMaxReviewPasses);
        Assert.Null(state.AutonomyModelHeartbeatSeconds);
    }

    /// <summary>
    /// Ensures autonomy tool bounds preserve the supported upper limit for long tool flows.
    /// </summary>
    [Fact]
    public void AutonomyToolBounds_RestorePreservesSupportedMaxValues() {
        var window = CreateWindowWithState();
        var state = Assert.IsType<ChatAppState>(AppStateField.GetValue(window));
        state.AutonomyMaxToolRounds = 256;
        state.AutonomyMaxCandidateTools = 256;

        Invoke(RestoreAutonomyOverridesFromAppStateMethod, window);

        Assert.Equal(256, ReadNullableInt(AutonomyMaxToolRoundsField, window));
        Assert.Equal(256, ReadNullableInt(AutonomyMaxCandidateToolsField, window));
        Assert.Equal(256, state.AutonomyMaxToolRounds);
        Assert.Equal(256, state.AutonomyMaxCandidateTools);
    }

    /// <summary>
    /// Ensures persisted autonomy tool bounds above supported range are dropped safely.
    /// </summary>
    [Fact]
    public void AutonomyToolBounds_RestoreClearsOutOfRangeValues() {
        var window = CreateWindowWithState();
        var state = Assert.IsType<ChatAppState>(AppStateField.GetValue(window));
        state.AutonomyMaxToolRounds = 257;
        state.AutonomyMaxCandidateTools = 257;

        Invoke(RestoreAutonomyOverridesFromAppStateMethod, window);

        Assert.Null(ReadNullableInt(AutonomyMaxToolRoundsField, window));
        Assert.Null(ReadNullableInt(AutonomyMaxCandidateToolsField, window));
        Assert.Null(state.AutonomyMaxToolRounds);
        Assert.Null(state.AutonomyMaxCandidateTools);
    }

    private static MainWindow CreateWindowWithState() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        AppStateField.SetValue(window, new ChatAppState());
        return window;
    }

    private static void Invoke(MethodInfo method, MainWindow window) {
        try {
            method.Invoke(window, null);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static bool? ReadNullableBool(FieldInfo field, MainWindow window) {
        var value = field.GetValue(window);
        return value switch {
            null => null,
            bool v => v,
            _ => throw new InvalidOperationException($"Unexpected {field.Name} value type '{value.GetType().FullName}'.")
        };
    }

    private static int? ReadNullableInt(FieldInfo field, MainWindow window) {
        var value = field.GetValue(window);
        return value switch {
            null => null,
            int v => v,
            _ => throw new InvalidOperationException($"Unexpected {field.Name} value type '{value.GetType().FullName}'.")
        };
    }
}
