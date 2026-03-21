using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private async Task ExportTranscriptForensicsAsync() {
        try {
            var conversation = GetActiveConversation();
            if (conversation.Messages.Count == 0) {
                AppendSystem("Transcript forensics export skipped: active conversation is empty.");
                return;
            }

            var baseName = string.IsNullOrWhiteSpace(conversation.ThreadId) ? conversation.Id : conversation.ThreadId!;
            var pickedPath = await ShowTranscriptForensicsSavePickerAsync(baseName).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pickedPath)) {
                return;
            }

            var persistedState = await _stateStore.GetAsync(_appProfileName, CancellationToken.None).ConfigureAwait(false);
            var persistedConversation = FindPersistedConversationState(
                persistedState,
                conversation.Id,
                conversation.ThreadId);
            var bundle = TranscriptForensicsExporter.Build(
                _appProfileName,
                _stateStore.DatabasePath,
                _timestampFormat,
                _markdownOptions,
                conversation.Id,
                conversation.Title,
                conversation.ThreadId,
                conversation.Messages,
                persistedConversation?.Messages,
                BuildRuntimeToolingSupportSnapshot());

            var outputPath = ResolveTranscriptForensicsOutputPath(pickedPath);
            TranscriptForensicsExporter.Export(outputPath, bundle);
            AppendSystem("Exported transcript forensics: " + outputPath);
        } catch (Exception ex) {
            StartupLog.Write("Transcript forensics export failed: " + ex);
            AppendSystem("Transcript forensics export failed: " + ex.Message);
        }
    }

    private async Task<string?> ShowTranscriptForensicsSavePickerAsync(string? title) {
        string? selectedPath = null;
        await RunOnUiThreadAsync(async () => {
            var picker = new FileSavePicker {
                SuggestedStartLocation = PickerLocationId.Downloads,
                SuggestedFileName = BuildSuggestedExportFileName((title ?? "transcript") + "-forensics", "json"),
                DefaultFileExtension = ".json"
            };

            picker.FileTypeChoices.Add("JSON File", new List<string> { ".json" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero) {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            selectedPath = file?.Path;
        }).ConfigureAwait(false);
        return selectedPath;
    }

    internal static string ResolveTranscriptForensicsOutputPath(string selectedPath) {
        var fullPath = Path.GetFullPath(selectedPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.ChangeExtension(fullPath, ".json");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    internal static ChatConversationState? FindPersistedConversationState(
        ChatAppState? state,
        string liveConversationId,
        string? liveThreadId) {
        if (state?.Conversations is not { Count: > 0 }) {
            return null;
        }

        for (var i = 0; i < state.Conversations.Count; i++) {
            var conversation = state.Conversations[i];
            if (string.Equals(conversation.Id, liveConversationId, StringComparison.Ordinal)) {
                return conversation;
            }
        }

        if (!string.IsNullOrWhiteSpace(liveThreadId)) {
            ChatConversationState? matchedConversation = null;
            for (var i = 0; i < state.Conversations.Count; i++) {
                var conversation = state.Conversations[i];
                if (string.Equals(conversation.ThreadId, liveThreadId, StringComparison.Ordinal)) {
                    if (matchedConversation is not null) {
                        return null;
                    }

                    matchedConversation = conversation;
                }
            }

            return matchedConversation;
        }

        return null;
    }

    internal RuntimeToolingSupportSnapshot? BuildRuntimeToolingSupportSnapshot() {
        return RuntimeToolingSupportSnapshotBuilder.Build(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogPlugins,
            _toolCatalogCapabilitySnapshot);
    }
}
