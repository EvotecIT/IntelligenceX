using System.IO;
using System.Windows;
using System.Windows.Threading;
using IntelligenceX.Tray.ViewModels;
using Microsoft.Win32;

namespace IntelligenceX.Tray.Views;

public partial class LedgerWindow : Window {
    private readonly LedgerViewModel _model;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public LedgerWindow() {
        InitializeComponent();
        var configured = Environment.GetEnvironmentVariable("IX_LEDGER_READ_CONFIG");
        _model = new LedgerViewModel(configured ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelligenceX", "Ledger", "client.json"));
        DataContext = _model;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _timer.Tick += OnRefresh;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) {
        await _model.RefreshAsync();
        if (IsVisible) _timer.Start();
    }
    private async void OnRefresh(object? sender, EventArgs args) => await _model.RefreshAsync();
    private void OnClosed(object? sender, EventArgs args) {
        _timer.Stop();
        _timer.Tick -= OnRefresh;
        _model.Dispose();
    }
    private async void OnChooseSettings(object sender, RoutedEventArgs args) {
        var dialog = new OpenFileDialog { Title = "Ledger read settings", Filter = "JSON settings (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true) await _model.ConfigureAsync(dialog.FileName);
    }
}
