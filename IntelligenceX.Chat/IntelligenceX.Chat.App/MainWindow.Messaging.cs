using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private async void OnWebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args) {
        try {
            var raw = args.TryGetWebMessageAsString();
            if (!TryParseJsonObject(raw, out var root)) {
                return;
            }

            var type = TryGetString(root, "type");
            switch (type) {
                case "connect":
                    await ConnectAsync(fromUserAction: true, connectBudgetOverride: DispatchConnectBudget).ConfigureAwait(true);
                    break;
                case "login":
                    await LoginAsync().ConfigureAwait(true);
                    break;
                case "relogin":
                    await ReLoginFromMenuAsync().ConfigureAwait(true);
                    break;
                case "switch_account":
                    await SwitchAccountFromMenuAsync().ConfigureAwait(true);
                    break;
                case "send":
                    {
                        var text = (TryGetString(root, "text") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(text)) {
                            await SendPromptAsync(text).ConfigureAwait(true);
                        }
                        break;
                    }
                case "send_clipboard":
                    await SendClipboardAsync().ConfigureAwait(true);
                    break;
                case "cancel_turn":
                    await CancelActiveTurnAsync().ConfigureAwait(true);
                    break;
                case "export":
                    await ExportTranscriptAsync().ConfigureAwait(true);
                    break;
                case "copy":
                    CopyTranscript();
                    break;
                case "clear":
                    ClearConversation();
                    break;
                case "new_conversation":
                    await NewConversationAsync().ConfigureAwait(true);
                    break;
                case "switch_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await SwitchConversationAsync(conversationId).ConfigureAwait(true);
                        }
                        break;
                    }
                case "rename_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await RenameConversationAsync(conversationId, title).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_conversation_model":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        var model = TryGetString(root, "model");
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await SetConversationModelAsync(conversationId, model).ConfigureAwait(true);
                        }
                        break;
                    }
                case "delete_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await DeleteConversationAsync(conversationId).ConfigureAwait(true);
                        }
                        break;
                    }
                case "toggle_debug":
                    _debugMode = !_debugMode;
                    await SetStatusAsync(_debugMode ? SessionStatus.DebugModeOn() : ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(true);
                    break;
                case "options_refresh":
                    await RefreshLocalRuntimeDetectionAsync(publishOptions: false).ConfigureAwait(true);
                    if (_client is not null) {
                        if (ShouldRefreshToolCatalogOnOptionsRefresh()) {
                            await RefreshToolCatalogFromServiceAsync(_client, publishOptions: false, appendWarnings: true).ConfigureAwait(true);
                        }
                        await RefreshBackgroundSchedulerStatusAsync(
                            _client,
                            publishOptions: false,
                            appendWarnings: true,
                            includeRecentActivity: true,
                            includeThreadSummaries: true,
                            maxRecentActivity: 8,
                            maxThreadSummaries: 8).ConfigureAwait(true);
                    }
                    await PublishOptionsStateAsync().ConfigureAwait(true);
                    break;
                case "scheduler_refresh":
                    await RefreshBackgroundSchedulerFromUiAsync(TryGetString(root, "threadId")).ConfigureAwait(true);
                    break;
                case "scheduler_continue_thread":
                    await ContinueBackgroundSchedulerThreadFromUiAsync(TryGetString(root, "threadId")).ConfigureAwait(true);
                    break;
                case "scheduler_pause":
                    await SetBackgroundSchedulerPausedFromUiAsync(
                        paused: true,
                        pauseMinutesText: TryGetString(root, "minutes"),
                        reason: "app_operator_pause").ConfigureAwait(true);
                    break;
                case "scheduler_resume":
                    await SetBackgroundSchedulerPausedFromUiAsync(
                        paused: false,
                        pauseMinutesText: null,
                        reason: "app_operator_resume").ConfigureAwait(true);
                    break;
                case "scheduler_add_maintenance":
                    await AddBackgroundSchedulerMaintenanceWindowFromUiAsync(
                        TryGetString(root, "day"),
                        TryGetString(root, "startTimeLocal"),
                        TryGetString(root, "durationMinutes"),
                        TryGetString(root, "packId"),
                        TryGetString(root, "threadId")).ConfigureAwait(true);
                    break;
                case "scheduler_remove_maintenance":
                    await RemoveBackgroundSchedulerMaintenanceWindowFromUiAsync(TryGetString(root, "spec")).ConfigureAwait(true);
                    break;
                case "scheduler_clear_maintenance":
                    await ClearBackgroundSchedulerMaintenanceWindowsFromUiAsync().ConfigureAwait(true);
                    break;
                case "scheduler_set_thread_block":
                    await SetBackgroundSchedulerThreadBlockedFromUiAsync(
                        TryGetString(root, "threadId"),
                        TryGetBoolean(root, "blocked") == true,
                        TryGetString(root, "durationMinutes"),
                        TryGetBoolean(root, "untilNextMaintenanceWindow") == true,
                        TryGetBoolean(root, "untilNextMaintenanceWindowStart") == true).ConfigureAwait(true);
                    break;
                case "scheduler_set_pack_block":
                    await SetBackgroundSchedulerPackBlockedFromUiAsync(
                        TryGetString(root, "packId"),
                        TryGetBoolean(root, "blocked") == true,
                        TryGetString(root, "durationMinutes"),
                        TryGetBoolean(root, "untilNextMaintenanceWindow") == true,
                        TryGetBoolean(root, "untilNextMaintenanceWindowStart") == true).ConfigureAwait(true);
                    break;
                case "scheduler_clear_thread_blocks":
                    await ClearBackgroundSchedulerThreadBlocksFromUiAsync().ConfigureAwait(true);
                    break;
                case "scheduler_clear_pack_blocks":
                    await ClearBackgroundSchedulerPackBlocksFromUiAsync().ConfigureAwait(true);
                    break;
                case "auto_detect_local_runtime":
                    {
                        var forceRefresh = TryGetBoolean(root, "forceRefresh");
                        await AutoDetectAndApplyLocalRuntimeAsync(forceRefresh ?? true).ConfigureAwait(true);
                        break;
                    }
                case "refresh_models":
                    {
                        var forceRefresh = TryGetBoolean(root, "forceRefresh");
                        await RefreshModelsFromUiAsync(forceRefresh ?? true).ConfigureAwait(true);
                        break;
                    }
                case "debug_copy_startup_log":
                    CopyStartupLogToClipboard();
                    break;
                case "debug_export_transcript_forensics":
                    await ExportTranscriptForensicsAsync().ConfigureAwait(true);
                    break;
                case "debug_memory_recompute":
                    await ForceRecomputeMemoryCacheAsync().ConfigureAwait(true);
                    break;
                case "set_time_mode":
                    {
                        var mode = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(mode)) {
                            await SetTimeModeAsync(mode).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_export_save_mode":
                    {
                        var mode = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(mode)) {
                            await SetExportSaveModeAsync(mode).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_export_default_format":
                    {
                        var format = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(format)) {
                            await SetExportDefaultFormatAsync(format).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_export_visual_theme_mode":
                    {
                        var mode = (TryGetString(root, "value") ?? string.Empty).Trim();
                        await SetExportVisualThemeModeAsync(mode).ConfigureAwait(true);
                        break;
                    }
                case "set_export_docx_visual_max_width":
                    {
                        var width = TryGetString(root, "value");
                        await SetExportDocxVisualMaxWidthPxAsync(width).ConfigureAwait(true);
                        break;
                    }
                case "clear_export_last_directory":
                    await ClearExportLastDirectoryAsync().ConfigureAwait(true);
                    break;
                case "set_autonomy":
                    {
                        var maxRounds = TryGetString(root, "maxToolRounds");
                        var parallelMode = TryGetString(root, "parallelMode");
                        var turnTimeout = TryGetString(root, "turnTimeoutSeconds");
                        var toolTimeout = TryGetString(root, "toolTimeoutSeconds");
                        var weightedRouting = TryGetString(root, "weightedToolRouting");
                        var maxCandidates = TryGetString(root, "maxCandidateTools");
                        var planExecuteReviewLoop = TryGetString(root, "planExecuteReviewLoop");
                        var maxReviewPasses = TryGetString(root, "maxReviewPasses");
                        var modelHeartbeatSeconds = TryGetString(root, "modelHeartbeatSeconds");
                        await SetAutonomyOverridesAsync(maxRounds, parallelMode, turnTimeout, toolTimeout, weightedRouting, maxCandidates, planExecuteReviewLoop, maxReviewPasses, modelHeartbeatSeconds)
                            .ConfigureAwait(true);
                        break;
                    }
                case "reset_autonomy":
                    await ResetAutonomyOverridesAsync().ConfigureAwait(true);
                    break;
                case "set_memory_enabled":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetPersistentMemoryEnabledAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_show_turn_trace":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetShowAssistantTurnTraceAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_show_draft_bubbles":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetShowAssistantDraftBubblesAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_proactive_mode":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetProactiveModeAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_queue_auto_dispatch":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetQueueAutoDispatchAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "run_next_queued":
                    await RunNextQueuedTurnAsync().ConfigureAwait(true);
                    break;
                case "clear_queued_turns":
                    await ClearQueuedTurnsAsync().ConfigureAwait(true);
                    break;
                case "add_memory_note":
                    {
                        var text = TryGetString(root, "text");
                        var weight = TryGetString(root, "weight");
                        var parsedWeight = ParseAutonomyInt(weight, min: 1, max: 5) ?? 3;
                        await AddMemoryFactAsync(text, parsedWeight).ConfigureAwait(true);
                        break;
                    }
                case "remove_memory_fact":
                    await RemoveMemoryFactAsync(TryGetString(root, "id")).ConfigureAwait(true);
                    break;
                case "clear_memory":
                    await ClearPersistentMemoryAsync().ConfigureAwait(true);
                    break;
                case "set_theme":
                    {
                        var theme = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(theme)) {
                            await SetThemePresetAsync(theme).ConfigureAwait(true);
                        }
                        break;
                    }
                case "apply_profile_update":
                    {
                        var update = new OnboardingProfileUpdate();
                        update.Scope = ParseProfileUpdateScope(TryGetString(root, "scope"));

                        if (root.TryGetProperty("userName", out var userNameElement)) {
                            update.HasUserName = true;
                            update.UserName = userNameElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => userNameElement.GetString(),
                                _ => userNameElement.GetRawText()
                            };
                        }

                        if (root.TryGetProperty("persona", out var personaElement)) {
                            update.HasAssistantPersona = true;
                            update.AssistantPersona = personaElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => personaElement.GetString(),
                                _ => personaElement.GetRawText()
                            };
                        }

                        if (root.TryGetProperty("theme", out var themeElement)) {
                            update.HasThemePreset = true;
                            update.ThemePreset = themeElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => themeElement.GetString(),
                                _ => themeElement.GetRawText()
                            };
                        }

                        _ = await ApplyProfileUpdateAsync(update, autoCompleteOnboardingForProfileScope: true).ConfigureAwait(true);
                        break;
                    }
                case "set_tool_enabled":
                    {
                        var toolName = (TryGetString(root, "name") ?? string.Empty).Trim();
                        var enabled = TryGetBoolean(root, "enabled");
                        if (!string.IsNullOrWhiteSpace(toolName) && enabled.HasValue) {
                            SetToolEnabled(toolName, enabled.Value);
                            await PublishOptionsStateAsync().ConfigureAwait(true);
                            await PersistAppStateAsync().ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_pack_enabled":
                    {
                        var packId = (TryGetString(root, "packId") ?? string.Empty).Trim();
                        var enabled = TryGetBoolean(root, "enabled");
                        if (!string.IsNullOrWhiteSpace(packId) && enabled.HasValue) {
                            if (await SetToolPackEnabledAsync(packId, enabled.Value).ConfigureAwait(true)) {
                                await PublishOptionsStateAsync().ConfigureAwait(true);
                                await PersistAppStateAsync().ConfigureAwait(true);
                            } else {
                                await PublishOptionsStateAsync().ConfigureAwait(true);
                            }
                        }
                        break;
                    }
                case "save_profile":
                    {
                        var userName = (TryGetString(root, "userName") ?? string.Empty).Trim();
                        var persona = (TryGetString(root, "persona") ?? string.Empty).Trim();
                        var theme = (TryGetString(root, "theme") ?? "default").Trim();
                        await SaveProfileAsync(userName, persona, theme).ConfigureAwait(true);
                        break;
                    }
                case "switch_profile":
                    {
                        var profileName = (TryGetString(root, "name") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(profileName)) {
                            await SwitchProfileAsync(profileName).ConfigureAwait(true);
                        }
                        break;
                    }
                case "apply_local_provider":
                    {
                        var transport = TryGetString(root, "transport");
                        var baseUrl = TryGetString(root, "baseUrl");
                        var model = TryGetString(root, "model");
                        var openAIAuthMode = TryGetString(root, "openAIAuthMode");
                        var openAIBasicUsername = TryGetString(root, "openAIBasicUsername");
                        var openAIBasicPassword = TryGetString(root, "openAIBasicPassword");
                        var openAIAccountId = TryGetString(root, "openAIAccountId");
                        var activeNativeAccountSlot = TryGetInt32(root, "activeNativeAccountSlot");
                        var activeSlotAccountId = TryGetString(root, "activeSlotAccountId");
                        var reasoningEffort = TryGetString(root, "reasoningEffort");
                        var reasoningSummary = TryGetString(root, "reasoningSummary");
                        var textVerbosity = TryGetString(root, "textVerbosity");
                        var temperature = TryGetString(root, "temperature");
                        var apiKey = TryGetString(root, "apiKey");
                        var clearBasicAuth = TryGetBoolean(root, "clearBasicAuth");
                        var clearApiKey = TryGetBoolean(root, "clearApiKey");
                        var forceRefresh = TryGetBoolean(root, "forceRefresh");
                        var requestId = TryGetInt64(root, "requestId");
                        await ApplyLocalProviderAsync(
                                transport,
                                baseUrl,
                                model,
                                openAIAuthMode,
                                openAIBasicUsername,
                                openAIBasicPassword,
                                openAIAccountId,
                                activeNativeAccountSlot,
                                activeSlotAccountId,
                                reasoningEffort,
                                reasoningSummary,
                                textVerbosity,
                                temperature,
                                apiKey,
                                clearBasicAuth ?? false,
                                clearApiKey ?? false,
                                forceRefresh ?? true,
                                requestId)
                            .ConfigureAwait(true);
                        break;
                    }
                case "restart_onboarding":
                    await RestartOnboardingAsync().ConfigureAwait(true);
                    break;
                case "window_minimize":
                    MinimizeWindow();
                    break;
                case "window_maximize":
                    ToggleMaximizeWindow();
                    await PublishSessionStateAsync().ConfigureAwait(true);
                    break;
                case "window_close":
                    Close();
                    break;
                case "window_drag":
                    BeginDragMoveWindow();
                    break;
                case "window_titlebar_metrics":
                    UpdateNativeTitleBarRegions(root);
                    break;
                case "login_prompt":
                    {
                        var loginId = (TryGetString(root, "loginId") ?? string.Empty).Trim();
                        var promptId = (TryGetString(root, "promptId") ?? string.Empty).Trim();
                        var input = (TryGetString(root, "input") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(loginId) && !string.IsNullOrWhiteSpace(promptId) && !string.IsNullOrWhiteSpace(input)) {
                            await SubmitLoginPromptAsync(loginId, promptId, input).ConfigureAwait(true);
                        }
                        break;
                    }
                case "copy_message":
                    {
                        var indexStr = TryGetString(root, "index");
                        if (int.TryParse(indexStr, out var idx) && idx >= 0 && idx < _messages.Count) {
                            var dp = new DataPackage();
                            dp.SetText(_messages[idx].Text);
                            Clipboard.SetContent(dp);
                            Clipboard.Flush();
                        }
                        break;
                    }
                case "omd_copy":
                    {
                        var copyText = TryGetString(root, "text");
                        if (!string.IsNullOrEmpty(copyText)) {
                            var dp = new DataPackage();
                            dp.SetText(copyText);
                            Clipboard.SetContent(dp);
                            Clipboard.Flush();
                        }
                        break;
                    }
                case "export_table_artifact":
                    {
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        var exportId = (TryGetString(root, "exportId") ?? string.Empty).Trim();
                        var outputPath = (TryGetString(root, "outputPath") ?? string.Empty).Trim();
                        if (!root.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array) {
                            await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(true);
                            AppendSystem(SystemNotice.ExportMissingRowsPayload());
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(format)) {
                            await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(true);
                            AppendSystem(SystemNotice.ExportMissingFormat());
                            break;
                        }

                        await ExportTableArtifactAsync(format, title, rowsElement, exportId, outputPath).ConfigureAwait(true);
                        break;
                    }
                case "pick_export_path":
                    {
                        var requestId = (TryGetString(root, "requestId") ?? string.Empty).Trim();
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        await PickDataViewExportPathAsync(requestId, format, title).ConfigureAwait(true);
                        break;
                    }
                case "data_view_export_action":
                    {
                        var action = (TryGetString(root, "action") ?? string.Empty).Trim();
                        var path = (TryGetString(root, "path") ?? string.Empty).Trim();
                        await HandleDataViewExportActionAsync(action, path).ConfigureAwait(true);
                        break;
                    }
                case "pick_visual_export_path":
                    {
                        var requestId = (TryGetString(root, "requestId") ?? string.Empty).Trim();
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        if (!TryNormalizeVisualExportFormat(format, out var normalizedVisualFormat)) {
                            await NotifyVisualExportPathSelectedAsync(
                                requestId,
                                ok: false,
                                path: null,
                                message: "Unsupported visual export format.",
                                canceled: false).ConfigureAwait(true);
                            break;
                        }

                        await PickVisualExportPathAsync(requestId, normalizedVisualFormat, title).ConfigureAwait(true);
                        break;
                    }
                case "export_visual_artifact":
                    {
                        var exportId = (TryGetString(root, "exportId") ?? string.Empty).Trim();
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        var outputPath = (TryGetString(root, "outputPath") ?? string.Empty).Trim();
                        var mimeType = (TryGetString(root, "mimeType") ?? string.Empty).Trim();
                        var dataBase64 = TryGetString(root, "dataBase64") ?? string.Empty;
                        if (!TryNormalizeVisualExportFormat(format, out var normalizedVisualFormat)) {
                            await NotifyVisualExportResultAsync(exportId, format, ok: false, filePath: null, message: "Unsupported visual export format.").ConfigureAwait(true);
                            break;
                        }

                        if (dataBase64.Length > MaxVisualExportBase64Chars) {
                            await NotifyVisualExportResultAsync(exportId, normalizedVisualFormat, ok: false, filePath: null, message: "Export payload exceeds maximum allowed size.").ConfigureAwait(true);
                            break;
                        }

                        if (outputPath.Length > 0
                            && !TryNormalizeVisualExportPath(outputPath, normalizedVisualFormat, out outputPath, out var outputPathError)) {
                            await NotifyVisualExportResultAsync(exportId, normalizedVisualFormat, ok: false, filePath: null, message: outputPathError).ConfigureAwait(true);
                            break;
                        }

                        await ExportVisualArtifactAsync(normalizedVisualFormat, title, dataBase64, mimeType, exportId, outputPath).ConfigureAwait(true);
                        break;
                    }
                case "visual_export_action":
                    {
                        var action = (TryGetString(root, "action") ?? string.Empty).Trim();
                        var path = (TryGetString(root, "path") ?? string.Empty).Trim();
                        await HandleVisualExportActionAsync(action, path).ConfigureAwait(true);
                        break;
                    }
                case "open_visual_popout":
                    {
                        try {
                            var popoutDirectory = GetVisualPopoutDirectoryPath();
                            CleanupStaleVisualPopoutFiles(popoutDirectory);

                            var title = TryGetString(root, "title");
                            var mimeType = (TryGetString(root, "mimeType") ?? string.Empty).Trim();
                            var dataBase64 = TryGetString(root, "dataBase64");
                            if (!TryPrepareVisualPopoutRequest(
                                    title,
                                    mimeType,
                                    dataBase64,
                                    out var normalizedTitle,
                                    out var normalizedFormat,
                                    out var payloadBytes,
                                    out var requestErrorMessage)) {
                                await NotifyVisualPopoutResultAsync(ok: false, filePath: null, message: requestErrorMessage).ConfigureAwait(true);
                                break;
                            }

                            var popoutResult = await OpenVisualPopoutAsync(normalizedTitle, normalizedFormat, payloadBytes).ConfigureAwait(true);
                            await NotifyVisualPopoutResultAsync(popoutResult.Ok, popoutResult.FilePath, popoutResult.Message).ConfigureAwait(true);
                        } catch (Exception ex) {
                            StartupLog.Write("open_visual_popout dispatch failed: " + ex);
                            await NotifyVisualPopoutResultAsync(ok: false, filePath: null, message: "Popout failed. Please try again.").ConfigureAwait(true);
                        }
                        break;
                    }
            }
        } catch (Exception ex) {
            AppendSystem(SystemNotice.UiMessageError(ex.Message));
        }
    }

}
