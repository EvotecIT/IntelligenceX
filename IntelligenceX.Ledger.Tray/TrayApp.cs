using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// The tray (Windows) or menu bar (macOS) icon and its menu. It reads the Ledger once a minute (and on Refresh now),
/// rebuilds the menu only when what it shows changed, and notifies about new changes. It never restarts or stops the
/// Ledger: deploys and restarts go through the Ledger's own supervisor and deploy procedure.
/// </summary>
internal sealed class TrayApp : Application {
    public static TrayPaths Paths { get; set; } = new();
    public static ThemeVariant? Theme { get; set; }
    public static bool OpenDetails { get; set; }
    /// <summary>A folder to render the details window and icons into after the first read, then quit. For screenshots.</summary>
    public static string? RenderTo { get; set; }
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource _life = new();
    private readonly ChangeNotifier _notifier = new();
    private TraySettings _settings = new();
    private LedgerPoller? _poller;
    private TrayIcon? _tray;
    private NativeMenu? _menu;
    private TrayView? _view;
    private Overview? _overview;
    private string? _built;
    private (TrayState, bool)? _drawn;
    private DetailsWindow? _details;
    private int _reading;
    private string? _machine;

    public override void Initialize() {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Theme ?? ThemeVariant.Default;
        Name = "IX Ledger";
    }

    public override void OnFrameworkInitializationCompleted() {
        _settings = TraySettings.Load(Paths.SettingsFile);
        _machine = Paths.MachineId();
        _poller = new LedgerPoller(Paths.ClientConfig, Paths.LedgerDirectory);
        _menu = new NativeMenu();
        _tray = new TrayIcon { ToolTipText = "IX Ledger · reading…", Menu = _menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowDetails();
        TrayIcon.SetIcons(this, [_tray]);
        Render(new TrayView(TrayState.Starting, "Reading the Ledger…", [], null, [], [], []));
        _ = PollAsync();
        if (OpenDetails) ShowDetails();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.ShutdownRequested += (_, _) => _life.Cancel();
        base.OnFrameworkInitializationCompleted();
    }

    private async Task PollAsync() {
        using var timer = new PeriodicTimer(PollInterval);
        if (RenderTo is { } folder) {
            await RefreshAsync();
            await Dispatcher.UIThread.InvokeAsync(() => {
                ShowDetails();
                // The whole page, scrolled-out parts included.
                _details!.SizeToContent = SizeToContent.Height;
                _details.MaxHeight = 4000;
            });
            await Task.Delay(1500);
            await Dispatcher.UIThread.InvokeAsync(() => { Snapshots.Save(folder, _details!, _view!, TrayArt.DarkBar(this)); Quit(); });
            return;
        }
        do { await RefreshAsync(); }
        while (await timer.WaitForNextTickAsync(_life.Token).ConfigureAwait(false));
    }

    private async Task RefreshAsync() {
        if (_poller is null || Interlocked.Exchange(ref _reading, 1) == 1) return;
        try {
            var overview = await Task.Run(() => _poller.ReadAsync(_life.Token), _life.Token).ConfigureAwait(false);
            var notes = _notifier.Next(overview, DateTimeOffset.UtcNow);
            await Dispatcher.UIThread.InvokeAsync(() => {
                _overview = overview;
                Render(TrayView.Build(overview, _machine, DateTimeOffset.UtcNow));
                if (_settings.Notify) foreach (var (title, body) in notes) Shell.Notify(title, body);
                foreach (var (title, body) in notes) TrayLog.Write("Notice: " + title + " · " + body.ReplaceLineEndings(" / "));
            });
        } catch (OperationCanceledException) when (_life.IsCancellationRequested) {
        } catch (Exception error) {
            TrayLog.Write("Refresh failed: " + error);
        } finally { Interlocked.Exchange(ref _reading, 0); }
    }

    private void Render(TrayView view) {
        _view = view;
        if (_tray is null || _menu is null) return;
        var dark = TrayArt.DarkBar(this);
        if (_drawn != (view.State, dark)) {
            _drawn = (view.State, dark);
            _tray.Icon = TrayArt.Draw(view.State, dark);
        }
        _tray.ToolTipText = view.Tooltip.Length <= 120 ? view.Tooltip : view.Tooltip[..119] + "…";
        var shape = view.Text() + "|" + _settings.Notify + "|" + LoginItem.IsEnabled;
        if (shape != _built) { _built = shape; Build(_menu, view); }
        if (_details is { IsVisible: true } && _overview is { } overview) _details.Show(view, overview.At);
    }

    private void Build(NativeMenu menu, TrayView view) {
        menu.Items.Clear();
        menu.Items.Add(Label(Mark(view.State) + " " + view.Headline));
        foreach (var line in view.LedgerLines) menu.Items.Add(Label("    " + line));
        foreach (var problem in view.Problems.Take(4)) menu.Items.Add(Label("    " + TrayView.Warn + " " + problem));

        if (view.ThisMachine is { } local) {
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Label("This machine (" + local.Machine + ")" + (local.Note is { } note ? " · " + note : "")));
            foreach (var provider in local.Providers) menu.Items.Add(ProviderItem(provider));
        }
        if (view.Others.Length > 0) {
            var others = new NativeMenuItem("Other machines") { Menu = new NativeMenu() };
            foreach (var machine in view.Others) {
                var marks = string.Concat(machine.Providers.Select(p => p.Mark));
                var entry = new NativeMenuItem(machine.Machine + "  " + marks + (machine.Reporter == "healthy" ? "" : " · reporter " + machine.Reporter)) { Menu = new NativeMenu() };
                if (machine.Note is { } note) entry.Menu.Items.Add(Label(note));
                foreach (var provider in machine.Providers) entry.Menu.Items.Add(ProviderItem(provider));
                others.Menu.Items.Add(entry);
            }
            menu.Items.Add(others);
        }
        var changes = new NativeMenuItem("Recent changes") { Menu = new NativeMenu() };
        if (view.Changes.Length == 0) changes.Menu.Items.Add(Label("None recorded yet"));
        foreach (var change in view.Changes) changes.Menu.Items.Add(Label(change));
        menu.Items.Add(changes);

        menu.Items.Add(new NativeMenuItemSeparator());
        var signIn = view.ThisMachine?.Providers.Where(p => p.NeedsSignIn).ToArray() ?? [];
        foreach (var provider in signIn) {
            var id = provider.Provider;
            var binary = provider.Details.FirstOrDefault(d => d.Label == "Binary").Value;
            menu.Items.Add(Item("Sign in to " + provider.Name + "…", () => Shell.SignIn(id, binary, Paths.Directory)));
        }
        menu.Items.Add(Item("Show details…", ShowDetails));
        menu.Items.Add(Item("Refresh now", () => _ = RefreshAsync()));
        menu.Items.Add(Item("Open Ledger folder", () => Shell.Reveal(Paths.LedgerDirectory)));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Toggle("Notify about changes", _settings.Notify, on => { _settings.Notify = on; _settings.Save(Paths.SettingsFile); Rerender(); }));
        if (LoginItem.IsSupported) menu.Items.Add(Toggle("Start at login", LoginItem.IsEnabled, on => { LoginItem.Set(on); Rerender(); }));
        menu.Items.Add(Item("Quit IX Ledger tray", Quit));
    }

    private void Rerender() { if (_view is { } view) { _built = null; Render(view); } }

    private static NativeMenuItem ProviderItem(ProviderLine provider) {
        var item = new NativeMenuItem("    " + provider.Mark + " " + provider.Name + "  " + provider.Summary) { Menu = new NativeMenu() };
        if (provider.Details.Length == 0) item.Menu.Items.Add(Label("No facts reported"));
        foreach (var (label, value) in provider.Details) item.Menu.Items.Add(Label(label + ": " + (value.Length <= 110 ? value : value[..109] + "…")));
        return item;
    }

    private static string Mark(TrayState state) => state switch {
        TrayState.Healthy => "●", TrayState.Degraded => "▲", TrayState.Down => "■", _ => "○"
    };

    private void ShowDetails() {
        if (_details is null) {
            _details = new DetailsWindow();
            _details.Closed += (_, _) => _details = null;
        }
        if (_view is { } view) _details.Show(view, _overview?.At ?? DateTimeOffset.UtcNow);
        _details.Show();
        _details.Activate();
    }

    private void Quit() {
        _life.Cancel();
        if (_tray is not null) _tray.IsVisible = false;
        _details?.Close();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private static NativeMenuItem Label(string text) => new(text) { IsEnabled = false };

    private static NativeMenuItem Item(string text, Action run) {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => run();
        return item;
    }

    private static NativeMenuItem Toggle(string text, bool on, Action<bool> set) {
        var item = new NativeMenuItem(text) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        item.Click += (_, _) => set(!on);
        return item;
    }
}
