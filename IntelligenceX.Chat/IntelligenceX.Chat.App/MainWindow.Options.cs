using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    /// <summary>
    /// Opens the shared runtime options panel now or after the WebView finishes navigation.
    /// Repeated calls intentionally reopen the panel instead of only foregrounding its window.
    /// </summary>
    internal async Task OpenOptionsAsync() {
        Interlocked.Exchange(ref _optionsPanelOpenRequested, 1);
        await FlushRequestedOptionsPanelAsync().ConfigureAwait(false);
    }

    private async Task FlushRequestedOptionsPanelAsync() {
        if (!Volatile.Read(ref _webViewReady)
            || Volatile.Read(ref _optionsPanelOpenRequested) == 0) {
            return;
        }

        await _optionsPanelGate.WaitAsync().ConfigureAwait(false);
        try {
            if (!Volatile.Read(ref _webViewReady)
                || Volatile.Read(ref _optionsPanelOpenRequested) == 0) {
                return;
            }

            var opened = false;
            await RunOnUiThreadAsync(async () => {
                if (_webView.CoreWebView2 is null) {
                    return;
                }

                var result = await _webView.ExecuteScriptAsync(
                        "(() => { const button = document.getElementById('menuOptions'); if (!button) return false; button.click(); return true; })();")
                    .AsTask()
                    .ConfigureAwait(false);
                opened = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            }).ConfigureAwait(false);

            if (opened) {
                Interlocked.Exchange(ref _optionsPanelOpenRequested, 0);
            }
        } catch (Exception ex) {
            StartupLog.Write("Opening the shared runtime options panel failed; request remains queued: " + ex.Message);
        } finally {
            _optionsPanelGate.Release();
        }
    }
}
