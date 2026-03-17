using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests runtime scheduler diagnostics projected into the Chat app options payload.
/// </summary>
public sealed class MainWindowRuntimeSchedulerStateTests {
    private static readonly MethodInfo BuildCapabilitySnapshotStateMethod = typeof(MainWindow).GetMethod(
        "BuildCapabilitySnapshotState",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCapabilitySnapshotState not found.");

    private static readonly MethodInfo BuildBackgroundSchedulerStateMethod = typeof(MainWindow).GetMethod(
        "BuildBackgroundSchedulerState",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBackgroundSchedulerState not found.");

    private static readonly MethodInfo BuildPublishedBackgroundSchedulerStateMethod = typeof(MainWindow).GetMethod(
        "BuildPublishedBackgroundSchedulerState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildPublishedBackgroundSchedulerState not found.");

    private static readonly MethodInfo RestoreBackgroundSchedulerSnapshotAfterRefreshFailureMethod = typeof(MainWindow).GetMethod(
        "RestoreBackgroundSchedulerSnapshotAfterRefreshFailure",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RestoreBackgroundSchedulerSnapshotAfterRefreshFailure not found.");

    private static readonly MethodInfo ClearBackgroundSchedulerSnapshotsMethod = typeof(MainWindow).GetMethod(
        "ClearBackgroundSchedulerSnapshots",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearBackgroundSchedulerSnapshots not found.");

    private static readonly MethodInfo ApplyBackgroundSchedulerSnapshotMethod = typeof(MainWindow).GetMethod(
        "ApplyBackgroundSchedulerSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ApplyBackgroundSchedulerSnapshot not found.");

    private static readonly MethodInfo ValidateBackgroundSchedulerMaintenanceWindowScopeMethod = typeof(MainWindow).GetMethod(
        "ValidateBackgroundSchedulerMaintenanceWindowScope",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ValidateBackgroundSchedulerMaintenanceWindowScope not found.");

    private static readonly MethodInfo BuildBackgroundSchedulerContinuationPlanMethod = typeof(MainWindow).GetMethod(
        "BuildBackgroundSchedulerContinuationPlan",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBackgroundSchedulerContinuationPlan not found.");

    private static readonly FieldInfo BackgroundSchedulerStatusSnapshotField = typeof(MainWindow).GetField(
        "_backgroundSchedulerStatusSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_backgroundSchedulerStatusSnapshot not found.");

    private static readonly FieldInfo BackgroundSchedulerScopedStatusSnapshotField = typeof(MainWindow).GetField(
        "_backgroundSchedulerScopedStatusSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_backgroundSchedulerScopedStatusSnapshot not found.");

    private static readonly FieldInfo BackgroundSchedulerGlobalStatusSnapshotField = typeof(MainWindow).GetField(
        "_backgroundSchedulerGlobalStatusSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_backgroundSchedulerGlobalStatusSnapshot not found.");

    private static readonly FieldInfo ServiceProfileNamesField = typeof(MainWindow).GetField(
        "_serviceProfileNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_serviceProfileNames not found.");

    private static readonly FieldInfo ServiceActiveProfileNameField = typeof(MainWindow).GetField(
        "_serviceActiveProfileName",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_serviceActiveProfileName not found.");

    private static readonly FieldInfo AppProfileNameField = typeof(MainWindow).GetField(
        "_appProfileName",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_appProfileName not found.");

    private static T GetPlanProperty<T>(object plan, string propertyName) {
        var property = plan.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException($"Plan property '{propertyName}' not found.");
        return Assert.IsType<T>(property.GetValue(plan));
    }

    /// <summary>
    /// Ensures capability snapshot diagnostics retain background scheduler details for the UI bridge.
    /// </summary>
    [Fact]
    public void BuildCapabilitySnapshotState_EmbedsBackgroundSchedulerState() {
        var snapshot = new SessionCapabilitySnapshotDto {
            RegisteredTools = 3,
            EnabledPackCount = 2,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = true,
            AllowedRootCount = 1,
            BackgroundScheduler = new SessionCapabilityBackgroundSchedulerDto {
                DaemonEnabled = true,
                Paused = false,
                QueuedItemCount = 4,
                ReadyItemCount = 2,
                RunningItemCount = 1,
                TrackedThreadCount = 3,
                ActiveMaintenanceWindowSpecs = new[] { "daily@23:30/120;pack=system" },
                ActiveMaintenanceWindows = new[] {
                    new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                        Spec = "daily@23:30/120;pack=system",
                        Day = "daily",
                        StartTimeLocal = "23:30",
                        DurationMinutes = 120,
                        PackId = "system",
                        Scoped = true
                    }
                },
                RecentActivity = new[] {
                    new SessionCapabilityBackgroundSchedulerActivityDto {
                        Outcome = "completed",
                        ThreadId = "thread-1",
                        ItemId = "item-1",
                        ToolName = "system_disk_inventory",
                        Reason = "completed",
                        OutputCount = 2
                    }
                },
                ThreadSummaries = new[] {
                    new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                        ThreadId = "thread-1",
                        QueuedItemCount = 2,
                        DependencyBlockedItemCount = 1,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 3,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0,
                        RecentEvidenceTools = new[] { "system_disk_inventory" },
                        DependencyHelperToolNames = new[] { "eventlog_channels_list" },
                        DependencyRecoveryReason = "background_prerequisite_auth_context_required",
                        DependencyNextAction = "request_runtime_auth_context",
                        ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                            ThreadId = "thread-1",
                            NextAction = "request_runtime_auth_context",
                            RecoveryReason = "background_prerequisite_auth_context_required",
                            HelperToolNames = new[] { "eventlog_channels_list" },
                            InputArgumentNames = new[] { "profile_id" },
                            SuggestedRequests = new[] {
                                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                    RequestKind = "list_profiles",
                                    Purpose = "discover_runtime_profiles"
                                },
                                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                    RequestKind = "set_profile",
                                    Purpose = "apply_runtime_auth_context",
                                    RequiredArgumentNames = new[] { "profileName" },
                                    SatisfiesInputArgumentNames = new[] { "profile_id" },
                                    SuggestedArguments = new[] {
                                        new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
                                            Name = "newThread",
                                            Value = "false",
                                            ValueKind = "boolean"
                                        }
                                    }
                                }
                            },
                            StatusSummary = "Waiting on runtime auth context: profile_id."
                        },
                        DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
                        DependencyAuthenticationArgumentNames = new[] { "profile_id" }
                    }
                }
            }
        };

        var state = BuildCapabilitySnapshotStateMethod.Invoke(null, new object?[] { snapshot });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        var root = document.RootElement;
        var scheduler = root.GetProperty("backgroundScheduler");
        Assert.Equal("Scoped maintenance active for 1 window(s).", scheduler.GetProperty("statusSummary").GetString());
        Assert.Equal("system", scheduler.GetProperty("activeMaintenanceWindows")[0].GetProperty("packId").GetString());
        Assert.Equal(2, scheduler.GetProperty("readyItemCount").GetInt32());
        Assert.Equal("system_disk_inventory", scheduler.GetProperty("recentActivity")[0].GetProperty("toolName").GetString());
        Assert.Equal("thread-1", scheduler.GetProperty("threadSummaries")[0].GetProperty("threadId").GetString());
        Assert.Equal("request_runtime_auth_context", scheduler.GetProperty("threadSummaries")[0].GetProperty("dependencyNextAction").GetString());
        Assert.Equal("request_runtime_auth_context", scheduler.GetProperty("threadSummaries")[0].GetProperty("continuationHint").GetProperty("nextAction").GetString());
        Assert.Equal("set_profile", scheduler.GetProperty("threadSummaries")[0].GetProperty("continuationHint").GetProperty("suggestedRequests")[1].GetProperty("requestKind").GetString());
        Assert.Equal("profile_id", scheduler.GetProperty("threadSummaries")[0].GetProperty("dependencyAuthenticationArgumentNames")[0].GetString());
        Assert.Equal("Ready=1, running=0, queued=2.", scheduler.GetProperty("threadSummaries")[0].GetProperty("statusSummary").GetString());
        Assert.Equal(4, scheduler.GetProperty("queuedItemCount").GetInt32());
    }

    /// <summary>
    /// Ensures paused scheduler state reports the active pause reason in UI diagnostics.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesPausedReason() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            Paused = true,
            ManualPauseActive = true,
            PauseReason = "manual_pause:300s:maintenance"
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Paused: manual_pause:300s:maintenance", document.RootElement.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures global scheduled maintenance pauses are summarized distinctly from scoped maintenance activity.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesGlobalScheduledMaintenancePause() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            Paused = true,
            ScheduledPauseActive = true,
            ActiveMaintenanceWindowSpecs = new[] { "daily@23:30/120" },
            ActiveMaintenanceWindows = new[] {
                new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                    Spec = "daily@23:30/120",
                    Day = "daily",
                    StartTimeLocal = "23:30",
                    DurationMinutes = 120,
                    Scoped = false
                }
            }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Global maintenance active for 1 window(s).", document.RootElement.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures non-paused snapshots do not report scoped maintenance when only global active windows are present.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_DoesNotReportScopedMaintenanceForGlobalActiveWindows() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            Paused = false,
            ActiveMaintenanceWindowSpecs = new[] { "daily@23:30/120" },
            ActiveMaintenanceWindows = new[] {
                new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                    Spec = "daily@23:30/120",
                    Day = "daily",
                    StartTimeLocal = "23:30",
                    DurationMinutes = 120,
                    Scoped = false
                }
            }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Background scheduler is idle.", document.RootElement.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures dependency-blocked scheduler state surfaces deterministic runtime-auth next steps instead of idle text.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesBlockedRuntimeAuthContext() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            DependencyBlockedThreadCount = 1,
            DependencyBlockedItemCount = 2,
            DependencyHelperToolNames = new[] { "eventlog_channels_list" },
            DependencyRecoveryReason = "background_prerequisite_auth_context_required",
            DependencyNextAction = "request_runtime_auth_context",
            DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
            DependencyAuthenticationArgumentNames = new[] { "profile_id" }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Waiting on runtime auth context: profile_id.", document.RootElement.GetProperty("statusSummary").GetString());
        Assert.Equal("request_runtime_auth_context", document.RootElement.GetProperty("dependencyNextAction").GetString());
        Assert.Equal("profile_id", document.RootElement.GetProperty("dependencyAuthenticationArgumentNames")[0].GetString());
    }

    /// <summary>
    /// Ensures dependency-blocked scheduler state surfaces helper retry waits instead of idle text.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesBlockedHelperRetry() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            DependencyBlockedThreadCount = 1,
            DependencyBlockedItemCount = 1,
            DependencyHelperToolNames = new[] { "eventlog_channels_list" },
            DependencyRecoveryReason = "background_prerequisite_retry_cooldown",
            DependencyNextAction = "wait_for_helper_retry",
            DependencyRetryCooldownHelperToolNames = new[] { "eventlog_channels_list" }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Waiting on helper retry: eventlog_channels_list.", document.RootElement.GetProperty("statusSummary").GetString());
        Assert.Equal("wait_for_helper_retry", document.RootElement.GetProperty("dependencyNextAction").GetString());
        Assert.Equal("eventlog_channels_list", document.RootElement.GetProperty("dependencyRetryCooldownHelperToolNames")[0].GetString());
    }

    /// <summary>
    /// Ensures published per-thread scheduler summaries retain dependency recovery hints and thread-level status text.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_EmbedsPerThreadDependencyRecoveryState() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-auth",
                    QueuedItemCount = 1,
                    DependencyBlockedItemCount = 1,
                    ReadyItemCount = 0,
                    RunningItemCount = 0,
                    CompletedItemCount = 2,
                    PendingReadOnlyItemCount = 1,
                    PendingUnknownItemCount = 0,
                    DependencyHelperToolNames = new[] { "eventlog_channels_list" },
                    DependencyRecoveryReason = "background_prerequisite_auth_context_required",
                    DependencyNextAction = "request_runtime_auth_context",
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-auth",
                        NextAction = "request_runtime_auth_context",
                        RecoveryReason = "background_prerequisite_auth_context_required",
                        HelperToolNames = new[] { "eventlog_channels_list" },
                        InputArgumentNames = new[] { "profile_id" },
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "list_profiles",
                                Purpose = "discover_runtime_profiles"
                            },
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "set_profile",
                                Purpose = "apply_runtime_auth_context",
                                RequiredArgumentNames = new[] { "profileName" },
                                SatisfiesInputArgumentNames = new[] { "profile_id" },
                                SuggestedArguments = new[] {
                                    new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
                                        Name = "newThread",
                                        Value = "false",
                                        ValueKind = "boolean"
                                    }
                                }
                            }
                        },
                        StatusSummary = "Waiting on runtime auth context: profile_id."
                    },
                    DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
                    DependencyAuthenticationArgumentNames = new[] { "profile_id" }
                }
            }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        var threadSummary = document.RootElement.GetProperty("threadSummaries")[0];
        Assert.Equal("thread-auth", threadSummary.GetProperty("threadId").GetString());
        Assert.Equal("request_runtime_auth_context", threadSummary.GetProperty("dependencyNextAction").GetString());
        Assert.Equal("request_runtime_auth_context", threadSummary.GetProperty("continuationHint").GetProperty("nextAction").GetString());
        Assert.Equal("discover_runtime_profiles", threadSummary.GetProperty("continuationHint").GetProperty("suggestedRequests")[0].GetProperty("purpose").GetString());
        Assert.Equal("profile_id", threadSummary.GetProperty("dependencyAuthenticationArgumentNames")[0].GetString());
        Assert.Equal("Waiting on runtime auth context: profile_id.", threadSummary.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures runtime-auth continuations resolve to an app profile application plan when the saved app profile is available.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerContinuationPlan_UsesSavedAppProfileForRuntimeAuthRecovery() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-auth",
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-auth",
                        NextAction = "request_runtime_auth_context",
                        RecoveryReason = "background_prerequisite_auth_context_required",
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "list_profiles",
                                Purpose = "discover_runtime_profiles"
                            },
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "set_profile",
                                Purpose = "apply_runtime_auth_context",
                                RequiredArgumentNames = new[] { "profileName" },
                                SatisfiesInputArgumentNames = new[] { "profile_id" }
                            }
                        }
                    }
                }
            }
        };

        var plan = BuildBackgroundSchedulerContinuationPlanMethod.Invoke(null, new object?[] {
            scheduler,
            "thread-auth",
            "ops",
            new[] { "ops", "lab" },
            "lab"
        });

        Assert.NotNull(plan);
        Assert.False(GetPlanProperty<bool>(plan!, "RefreshServiceProfiles"));
        Assert.True(GetPlanProperty<bool>(plan!, "ApplyServiceProfile"));
        Assert.True(GetPlanProperty<bool>(plan!, "RefreshSchedulerThread"));
        Assert.Equal("ops", GetPlanProperty<string>(plan!, "ProfileName"));
        Assert.Empty(GetPlanProperty<string[]>(plan!, "MissingArgumentNames"));
    }

    /// <summary>
    /// Ensures runtime-auth continuations first refresh profile inventory when the app has not loaded service profiles yet.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerContinuationPlan_RefreshesProfilesBeforeRuntimeAuthRecoveryWhenUnknown() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-auth",
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-auth",
                        NextAction = "request_runtime_auth_context",
                        RecoveryReason = "background_prerequisite_auth_context_required",
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "list_profiles",
                                Purpose = "discover_runtime_profiles"
                            },
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "set_profile",
                                Purpose = "apply_runtime_auth_context",
                                RequiredArgumentNames = new[] { "profileName" }
                            }
                        }
                    }
                }
            }
        };

        var plan = BuildBackgroundSchedulerContinuationPlanMethod.Invoke(null, new object?[] {
            scheduler,
            "thread-auth",
            "ops",
            Array.Empty<string>(),
            null
        });

        Assert.NotNull(plan);
        Assert.True(GetPlanProperty<bool>(plan!, "RefreshServiceProfiles"));
        Assert.False(GetPlanProperty<bool>(plan!, "ApplyServiceProfile"));
        Assert.False(GetPlanProperty<bool>(plan!, "RefreshSchedulerThread"));
        Assert.Empty(GetPlanProperty<string[]>(plan!, "MissingArgumentNames"));
    }

    /// <summary>
    /// Ensures retry/pending continuations resolve to a scoped scheduler refresh plan instead of profile mutation.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerContinuationPlan_UsesScopedRefreshForHelperRetryRecovery() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-retry",
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-retry",
                        NextAction = "wait_for_helper_retry",
                        RecoveryReason = "background_prerequisite_retry_cooldown",
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "get_background_scheduler_status",
                                Purpose = "refresh_helper_retry_status"
                            }
                        }
                    }
                }
            }
        };

        var plan = BuildBackgroundSchedulerContinuationPlanMethod.Invoke(null, new object?[] {
            scheduler,
            "thread-retry",
            "ops",
            new[] { "ops" },
            "ops"
        });

        Assert.NotNull(plan);
        Assert.False(GetPlanProperty<bool>(plan!, "RefreshServiceProfiles"));
        Assert.False(GetPlanProperty<bool>(plan!, "ApplyServiceProfile"));
        Assert.True(GetPlanProperty<bool>(plan!, "RefreshSchedulerThread"));
        Assert.Empty(GetPlanProperty<string[]>(plan!, "MissingArgumentNames"));
    }

    /// <summary>
    /// Ensures scheduler state published for the UI keeps tracked-thread counts plus ready/running id samples
    /// even when not every tracked thread has a detailed thread summary entry.
    /// </summary>
    [Fact]
    public void BuildCapabilitySnapshotState_EmbedsReadyThreadIdsWhenThreadSummariesAreSampled() {
        var snapshot = new SessionCapabilitySnapshotDto {
            RegisteredTools = 0,
            EnabledPackCount = 0,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = true,
            AllowedRootCount = 0,
            BackgroundScheduler = new SessionCapabilityBackgroundSchedulerDto {
                DaemonEnabled = true,
                TrackedThreadCount = 8,
                ReadyItemCount = 3,
                ReadyThreadIds = new[] { "thread-missing" },
                ThreadSummaries = new[] {
                    new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                        ThreadId = "thread-visible",
                        QueuedItemCount = 0,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 0,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0
                    }
                }
            }
        };

        var state = BuildCapabilitySnapshotStateMethod.Invoke(null, new object?[] { snapshot });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        var scheduler = document.RootElement.GetProperty("backgroundScheduler");
        Assert.Equal(8, scheduler.GetProperty("trackedThreadCount").GetInt32());
        Assert.Equal("thread-missing", scheduler.GetProperty("readyThreadIds")[0].GetString());
        Assert.Equal("thread-visible", scheduler.GetProperty("threadSummaries")[0].GetProperty("threadId").GetString());
    }

    /// <summary>
    /// Ensures app refresh keeps larger thread-id samples for sidebar fallback without inflating
    /// the caller's requested thread summary cap.
    /// </summary>
    [Fact]
    public void ResolveBackgroundSchedulerThreadSummaryLimit_RespectsRequestedCap() {
        var threadIdSampleLimit = MainWindow.ResolveBackgroundSchedulerThreadIdSampleLimit(includeThreadSummaries: true);
        var threadSummaryLimit = MainWindow.ResolveBackgroundSchedulerThreadSummaryLimit(maxThreadSummaries: 8);

        Assert.Equal(ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems, threadIdSampleLimit);
        Assert.Equal(8, threadSummaryLimit);
    }

    /// <summary>
    /// Ensures app refresh clamps invalid negative thread summary caps to the request contract floor.
    /// </summary>
    [Fact]
    public void ResolveBackgroundSchedulerThreadSummaryLimit_ClampsNegativeValuesToZero() {
        var threadSummaryLimit = MainWindow.ResolveBackgroundSchedulerThreadSummaryLimit(maxThreadSummaries: -5);

        Assert.Equal(0, threadSummaryLimit);
    }

    /// <summary>
    /// Ensures the app rejects ambiguous maintenance-window scope selections before building or sending a request.
    /// </summary>
    [Fact]
    public void ValidateBackgroundSchedulerMaintenanceWindowScope_RejectsPackAndThreadTogether() {
        var ex = Assert.Throws<TargetInvocationException>(() =>
            ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { "system", "thread-42" }));

        var argument = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("either packId or threadId", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures scoped maintenance-window validation still allows pack-only and thread-only requests.
    /// </summary>
    [Fact]
    public void ValidateBackgroundSchedulerMaintenanceWindowScope_AllowsSingleScopeTarget() {
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { "system", null });
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { null, "thread-42" });
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { null, null });
    }

    /// <summary>
    /// Ensures a scoped scheduler refresh failure preserves the prior scoped snapshot
    /// instead of overwriting thread-specific diagnostics with global scheduler state.
    /// </summary>
    [Fact]
    public void RestoreBackgroundSchedulerSnapshotAfterRefreshFailure_ScopedRefreshPreservesScopedSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            QueuedItemCount = 5
        };
        var scopedSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        };

        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerScopedStatusSnapshotField.SetValue(window, scopedSnapshot);

        RestoreBackgroundSchedulerSnapshotAfterRefreshFailureMethod.Invoke(window, new object?[] { true });

        var effective = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var restoredScoped = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerScopedStatusSnapshotField.GetValue(window));
        var preservedGlobal = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Same(globalSnapshot, effective);
        Assert.Same(scopedSnapshot, restoredScoped);
        Assert.Same(globalSnapshot, preservedGlobal);
    }

    /// <summary>
    /// Ensures an unscoped scheduler refresh failure falls back to the preserved global snapshot
    /// instead of keeping a stale scoped snapshot active in the published runtime state.
    /// </summary>
    [Fact]
    public void RestoreBackgroundSchedulerSnapshotAfterRefreshFailure_UnscopedRefreshRestoresGlobalSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            QueuedItemCount = 5
        };
        var scopedSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        };

        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerScopedStatusSnapshotField.SetValue(window, scopedSnapshot);

        RestoreBackgroundSchedulerSnapshotAfterRefreshFailureMethod.Invoke(window, new object?[] { false });

        var restored = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var preservedScoped = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerScopedStatusSnapshotField.GetValue(window));
        var preservedGlobal = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Same(globalSnapshot, restored);
        Assert.Same(scopedSnapshot, preservedScoped);
        Assert.Same(globalSnapshot, preservedGlobal);
    }

    /// <summary>
    /// Ensures a scoped scheduler refresh caches thread-specific data separately
    /// without replacing the effective global runtime snapshot used by sidebar diagnostics.
    /// </summary>
    [Fact]
    public void ApplyBackgroundSchedulerSnapshot_ScopedUpdatePreservesEffectiveGlobalSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 4
        };
        var scopedSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        };

        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);

        ApplyBackgroundSchedulerSnapshotMethod.Invoke(window, new object?[] { scopedSnapshot, true });

        var effective = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var scoped = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerScopedStatusSnapshotField.GetValue(window));
        var global = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Same(globalSnapshot, effective);
        Assert.Same(scopedSnapshot, scoped);
        Assert.Same(globalSnapshot, global);
    }

    /// <summary>
    /// Ensures published options keep runtimeScheduler global/effective after a scoped refresh
    /// while exposing thread-specific data separately through runtimeSchedulerScoped.
    /// </summary>
    [Fact]
    public void BuildPublishedBackgroundSchedulerState_PublishesGlobalEffectiveAndScopedSchedulerSeparately() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 4,
            ReadyThreadIds = new[] { "thread-global" }
        };
        var scopedSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1,
            ReadyThreadIds = new[] { "thread-scoped" }
        };

        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerScopedStatusSnapshotField.SetValue(window, scopedSnapshot);

        var schedulerState = BuildPublishedBackgroundSchedulerStateMethod.Invoke(window, new object?[] { null });
        Assert.NotNull(schedulerState);
        var schedulerStateType = schedulerState.GetType();
        var effective = schedulerStateType.GetField("Item1")?.GetValue(schedulerState);
        var scoped = schedulerStateType.GetField("Item2")?.GetValue(schedulerState);
        var global = schedulerStateType.GetField("Item3")?.GetValue(schedulerState);

        using var effectiveDocument = JsonDocument.Parse(JsonSerializer.Serialize(effective));
        using var scopedDocument = JsonDocument.Parse(JsonSerializer.Serialize(scoped));
        using var globalDocument = JsonDocument.Parse(JsonSerializer.Serialize(global));

        Assert.Equal(4, effectiveDocument.RootElement.GetProperty("queuedItemCount").GetInt32());
        Assert.Equal("thread-global", effectiveDocument.RootElement.GetProperty("readyThreadIds")[0].GetString());
        Assert.Equal(4, globalDocument.RootElement.GetProperty("queuedItemCount").GetInt32());
        Assert.Equal("thread-scoped", scopedDocument.RootElement.GetProperty("scopeThreadId").GetString());
        Assert.Equal(1, scopedDocument.RootElement.GetProperty("queuedItemCount").GetInt32());
    }

    /// <summary>
    /// Ensures live published scheduler state exposes a direct continuation command when the current app profile can resume a blocked thread.
    /// </summary>
    [Fact]
    public void BuildPublishedBackgroundSchedulerState_EmbedsContinuationCommandForBlockedThread() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-auth",
                    DependencyBlockedItemCount = 1,
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-auth",
                        NextAction = "request_runtime_auth_context",
                        RecoveryReason = "background_prerequisite_auth_context_required",
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "set_profile",
                                Purpose = "apply_runtime_auth_context",
                                RequiredArgumentNames = new[] { "profileName" }
                            }
                        }
                    }
                }
            }
        };

        AppProfileNameField.SetValue(window, "ops");
        ServiceProfileNamesField.SetValue(window, new[] { "ops", "lab" });
        ServiceActiveProfileNameField.SetValue(window, "lab");
        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);

        var schedulerState = BuildPublishedBackgroundSchedulerStateMethod.Invoke(window, new object?[] { null });
        Assert.NotNull(schedulerState);
        var effective = schedulerState.GetType().GetField("Item1")?.GetValue(schedulerState);

        using var effectiveDocument = JsonDocument.Parse(JsonSerializer.Serialize(effective));
        var command = effectiveDocument.RootElement
            .GetProperty("threadSummaries")[0]
            .GetProperty("continuationCommand");
        Assert.Equal("scheduler_continue_thread", command.GetProperty("command").GetString());
        Assert.Equal("thread-auth", command.GetProperty("threadId").GetString());
        Assert.True(command.GetProperty("enabled").GetBoolean());
        Assert.Equal("ops", command.GetProperty("profileName").GetString());
    }

    /// <summary>
    /// Ensures live published scheduler state marks continuation commands disabled when required profile context is unavailable.
    /// </summary>
    [Fact]
    public void BuildPublishedBackgroundSchedulerState_DisablesContinuationCommandWhenProfileIsMissing() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ThreadSummaries = new[] {
                new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                    ThreadId = "thread-auth",
                    DependencyBlockedItemCount = 1,
                    ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                        ThreadId = "thread-auth",
                        NextAction = "request_runtime_auth_context",
                        RecoveryReason = "background_prerequisite_auth_context_required",
                        SuggestedRequests = new[] {
                            new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                RequestKind = "set_profile",
                                Purpose = "apply_runtime_auth_context",
                                RequiredArgumentNames = new[] { "profileName" }
                            }
                        }
                    }
                }
            }
        };

        AppProfileNameField.SetValue(window, "ops");
        ServiceProfileNamesField.SetValue(window, Array.Empty<string>());
        ServiceActiveProfileNameField.SetValue(window, null);
        BackgroundSchedulerStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);

        var schedulerState = BuildPublishedBackgroundSchedulerStateMethod.Invoke(window, new object?[] { null });
        Assert.NotNull(schedulerState);
        var effective = schedulerState.GetType().GetField("Item1")?.GetValue(schedulerState);

        using var effectiveDocument = JsonDocument.Parse(JsonSerializer.Serialize(effective));
        var command = effectiveDocument.RootElement
            .GetProperty("threadSummaries")[0]
            .GetProperty("continuationCommand");
        Assert.Equal("scheduler_continue_thread", command.GetProperty("command").GetString());
        Assert.False(command.GetProperty("enabled").GetBoolean());
        Assert.Equal("profileName", command.GetProperty("missingArgumentNames")[0].GetString());
    }

    /// <summary>
    /// Ensures disconnect/cache-clear cleanup blanks both scoped and global scheduler snapshots
    /// before the next options payload is published to the web shell.
    /// </summary>
    [Fact]
    public void ClearBackgroundSchedulerSnapshots_ClearsScopedAndGlobalSnapshots() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        BackgroundSchedulerStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 2
        });
        BackgroundSchedulerScopedStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        });
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 4
        });

        ClearBackgroundSchedulerSnapshotsMethod.Invoke(window, Array.Empty<object?>());

        Assert.Null(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        Assert.Null(BackgroundSchedulerScopedStatusSnapshotField.GetValue(window));
        Assert.Null(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
    }

    /// <summary>
    /// Ensures successful unscoped scheduler updates replace any previously cached scoped snapshot
    /// so the published runtime scheduler state reflects the new global result immediately.
    /// </summary>
    [Fact]
    public void ApplyBackgroundSchedulerSnapshot_UnscopedUpdateReplacesScopedSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var refreshedGlobalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 7
        };

        BackgroundSchedulerStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 3
        });
        BackgroundSchedulerScopedStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        });
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 3
        });

        ApplyBackgroundSchedulerSnapshotMethod.Invoke(window, new object?[] { refreshedGlobalSnapshot, false });

        var effective = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var global = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Null(BackgroundSchedulerScopedStatusSnapshotField.GetValue(window));
        Assert.Same(refreshedGlobalSnapshot, effective);
        Assert.Same(refreshedGlobalSnapshot, global);
    }
}
