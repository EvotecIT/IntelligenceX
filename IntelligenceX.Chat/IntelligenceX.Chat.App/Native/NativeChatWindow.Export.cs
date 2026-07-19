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

        using var visualMaterialization = string.Equals(format, ExportPreferencesContract.FormatDocx, StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => NativeTranscriptVisualExportMaterializer.TryMaterialize(markdown)).ConfigureAwait(true)
            : null;
        var result = await Task.Run(() => visualMaterialization is null
            ? IntelligenceX.Chat.App.LocalExportArtifactWriter.ExportTranscript(
                format,
                title,
                markdown,
                outputPath)
            : IntelligenceX.Chat.App.LocalExportArtifactWriter.ExportDocxWithMaterializedVisualFallback(
                title,
                markdown,
                visualMaterialization.Markdown,
                outputPath,
                visualMaterialization.AllowedImageDirectories)).ConfigureAwait(true);

        _viewModel.SetHostStatus(result.Succeeded
            ? TranscriptExportNoticeFormatter.FormatSuccess(result)
            : TranscriptExportNoticeFormatter.FormatFailure(result));
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
