using System;
using System.IO;
using System.Threading.Tasks;
using IntelligenceX.Chat.ExportArtifacts;
using Windows.Storage.Pickers;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private async Task ExportNativeTranscriptAsync() {
        if (_viewModel.Transcript.Count == 0) {
            return;
        }

        var markdown = NativeTranscriptMarkdownFormatter.Format(_viewModel.Transcript);
        if (string.IsNullOrWhiteSpace(markdown)) {
            return;
        }

        var title = "native-chat-transcript";
        var pickedPath = await ShowNativeTranscriptSavePickerAsync(title).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pickedPath)) {
            return;
        }

        var format = ResolveTranscriptExportFormatFromPath(pickedPath);
        var outputPath = IntelligenceX.Chat.App.LocalExportArtifactWriter.ResolveOutputPath(
            format,
            title,
            pickedPath,
            defaultPrefix: "transcript");

        var result = await Task.Run(() =>
            IntelligenceX.Chat.App.LocalExportArtifactWriter.ExportTranscript(
                format,
                title,
                markdown,
                outputPath)).ConfigureAwait(true);

        _viewModel.Transcript.Add(new NativeChatTranscriptItem(
            "system",
            result.Succeeded
                ? "Exported transcript: " + result.OutputPath
                : "Transcript export failed: " + (result.Failure?.Message ?? "Unknown error."),
            DateTimeOffset.Now,
            result.Succeeded ? "Complete" : "Error"));
    }

    private async Task<string?> ShowNativeTranscriptSavePickerAsync(string title) {
        var picker = new FileSavePicker {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = title,
            DefaultFileExtension = ".md"
        };
        picker.FileTypeChoices.Add("Markdown Document", [".md"]);
        picker.FileTypeChoices.Add("Word Document", [".docx"]);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero) {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static string ResolveTranscriptExportFormatFromPath(string path) =>
        string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase)
            ? ExportPreferencesContract.FormatDocx
            : ExportPreferencesContract.FormatMarkdown;
}
