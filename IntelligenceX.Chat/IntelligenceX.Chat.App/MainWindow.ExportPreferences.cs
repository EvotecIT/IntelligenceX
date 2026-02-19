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
    private async Task SetExportSaveModeAsync(string value) {
        var next = ExportPreferencesContract.NormalizeSaveMode(value);
        if (string.Equals(_exportSaveMode, next, StringComparison.Ordinal)) {
            return;
        }

        _exportSaveMode = next;
        _appState.ExportSaveMode = next;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetExportDefaultFormatAsync(string value) {
        var next = ExportPreferencesContract.NormalizeFormat(value);
        if (string.Equals(_exportDefaultFormat, next, StringComparison.Ordinal)) {
            return;
        }

        _exportDefaultFormat = next;
        _appState.ExportDefaultFormat = next;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetExportVisualThemeModeAsync(string value) {
        var next = ExportPreferencesContract.NormalizeVisualThemeMode(value);
        if (string.Equals(_exportVisualThemeMode, next, StringComparison.Ordinal)) {
            return;
        }

        _exportVisualThemeMode = next;
        _appState.ExportVisualThemeMode = next;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ClearExportLastDirectoryAsync() {
        if (string.IsNullOrWhiteSpace(_lastExportDirectory)) {
            return;
        }

        _lastExportDirectory = null;
        _appState.ExportLastDirectory = null;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task UpdateLastExportDirectoryFromFilePathAsync(string? exportedFilePath) {
        var nextDirectory = ExportPreferencesContract.NormalizeFromFilePath(exportedFilePath);
        if (string.Equals(_lastExportDirectory, nextDirectory, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        _lastExportDirectory = nextDirectory;
        _appState.ExportLastDirectory = nextDirectory;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }
}
