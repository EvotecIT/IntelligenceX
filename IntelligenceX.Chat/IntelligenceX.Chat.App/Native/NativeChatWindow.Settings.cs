using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private bool _settingsOpening;

    /// <summary>
    /// Opens the existing shared configuration workspace instead of introducing a second provider/profile settings brain.
    /// </summary>
    private async Task OpenSharedSettingsWorkspaceAsync() {
        try {
            await _settingsReloadTask.WaitAsync(_lifetimeCts.Token).ConfigureAwait(true);
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            return;
        }

        if (_viewModel.HasActiveTurn) {
            _viewModel.SetHostStatus("Finish or stop the active turn before changing runtime settings.");
            return;
        }

        if (_viewModel.IsCheckingSignIn || !_interactiveSignInTask.IsCompleted) {
            _viewModel.SetHostStatus("Finish sign-in before changing runtime settings.");
            return;
        }

        if (_settingsWindow is not null) {
            await _settingsWindow.OpenOptionsAsync().ConfigureAwait(true);
            WindowForegroundActivator.EnsureWindowForeground(_settingsWindow);
            return;
        }

        if (_settingsOpening) return;

        _settingsOpening = true;
        UpdateCommandState();
        try {
            await _runtimeReadinessTask.WaitAsync(_lifetimeCts.Token).ConfigureAwait(true);
            if (_viewModel.HasActiveTurn) {
                _viewModel.SetHostStatus("Finish or stop the active turn before changing runtime settings.");
                return;
            }
            await _runtime.ReleaseClientForSettingsAsync(_lifetimeCts.Token).ConfigureAwait(true);
            _viewModel.InvalidateAuthenticationState();
            var settingsWindow = new MainWindow(
                openOptionsOnLaunch: true,
                pipeName: _runtime.PipeName,
                profileName: _conversationStore.ActiveProfileName) {
                Title = "IntelligenceX Chat - Runtime settings"
            };
            _settingsWindow = settingsWindow;
            UpdateCommandState();
            settingsWindow.Closed += (_, _) => {
                _settingsReloadTask = ReloadAfterSettingsCloseAsync(settingsWindow);
            };
            settingsWindow.Activate();
            WindowForegroundActivator.EnsureWindowForeground(settingsWindow);
            StartupLog.Write("Shared runtime settings workspace opened from native chat.");
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            if (_settingsWindow is null) _runtime.ResumeAfterSettings();
        } catch (Exception ex) {
            StartupLog.Write("Shared runtime settings failed to open: " + ex);
            if (_settingsWindow is null) _runtime.ResumeAfterSettings();
            if (!_lifetimeCts.IsCancellationRequested)
                _viewModel.SetHostStatus("Settings could not be opened: " + ex.Message);
        } finally {
            _settingsOpening = false;
            if (!_lifetimeCts.IsCancellationRequested) UpdateCommandState();
        }
    }

    private async Task ReloadAfterSettingsCloseAsync(MainWindow settingsWindow) {
        try {
            await settingsWindow.CloseCompletion.ConfigureAwait(true);
            _lifetimeCts.Token.ThrowIfCancellationRequested();
            _runtime.ResumeAfterSettings();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var profileChanged = _conversationStore.SelectProfile(settingsWindow.ActiveProfileName);
            if (profileChanged) {
                await _viewModel.InitializeConversationsAsync(timeout.Token).ConfigureAwait(true);
            } else {
                await _conversationStore.ReloadProfileStateAsync(timeout.Token).ConfigureAwait(true);
            }
            await RefreshRuntimeReadinessAsync(force: true, synchronizeSelectedProfile: true)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(true);
            _ = await _viewModel.CheckSignInAsync(timeout.Token).ConfigureAwait(true);
            StartupLog.Write("Native runtime state reloaded after shared settings closed.");
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            StartupLog.Write("Native runtime state reload canceled during window shutdown.");
        } catch (Exception ex) {
            StartupLog.Write("Native runtime state reload after settings failed: " + ex);
            if (!_lifetimeCts.IsCancellationRequested) {
                _viewModel.SetHostStatus("Settings were saved, but live chat state could not be refreshed: " + ex.Message);
            }
        } finally {
            _runtime.ResumeAfterSettings();
            if (ReferenceEquals(_settingsWindow, settingsWindow)) {
                _settingsWindow = null;
                if (!_lifetimeCts.IsCancellationRequested) UpdateCommandState();
            }
        }
    }
}
