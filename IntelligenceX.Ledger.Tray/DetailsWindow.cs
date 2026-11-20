using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// Marquee's colours: the dark set is Marquee's own tokens (near-black canvas, raised surfaces, violet accent, mint,
/// amber and coral states); the light set keeps the same hues at a contrast that reads on white.
/// </summary>
internal sealed record Palette(Color Canvas, Color Surface, Color Raised, Color Hairline, Color Text, Color Secondary, Color Muted,
    Color Accent, Color OnAccent, Color Good, Color Warn, Color Bad) {
    public static readonly Palette Dark = new(Hex("#08080B"), Hex("#121118"), Hex("#1A1922"), Hex("#24232E"), Hex("#F2F0F7"), Hex("#AEABBD"),
        Hex("#7E7A8F"), Hex("#A58BFF"), Hex("#0B0B0F"), Hex("#5EE0B5"), Hex("#FFC94D"), Hex("#FF7A7A"));
    public static readonly Palette Light = new(Hex("#F4F3F8"), Hex("#FFFFFF"), Hex("#F1EFF7"), Hex("#E3E1EB"), Hex("#16151C"), Hex("#55515F"),
        Hex("#7C788A"), Hex("#6A50D8"), Hex("#FFFFFF"), Hex("#16936D"), Hex("#B07607"), Hex("#CF4242"));

    public static Palette For(ThemeVariant? variant) => variant == ThemeVariant.Light ? Light : Dark;
    private static Color Hex(string value) => Color.Parse(value);

    public IBrush Brush(Color color, double opacity = 1) => new SolidColorBrush(color, opacity);

    /// <summary>The colour of a state, mark or tone.</summary>
    public Color Of(string tone) => tone switch {
        "good" or TrayView.Ok => Good, "warn" or TrayView.Warn => Warn, "bad" or TrayView.Bad => Bad, _ => Muted
    };

    public Color Of(TrayState state) => state switch { TrayState.Healthy => Good, TrayState.Degraded => Warn, TrayState.Down => Bad, _ => Muted };
}

/// <summary>
/// The details window: one answer at the top, what needs Przemek as cards that say what it means and what to do, each
/// machine as a compact card with its details on hover, the change history as a timeline and the Ledger's own health
/// in the footer. It follows the system's light or dark mode.
/// </summary>
internal sealed class DetailsWindow : Window {
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Action<string, string?> _signIn;
    private TrayView? _view;
    private DateTimeOffset _at;
    /// <summary>Which machine's facts are open, kept across refreshes until it is closed or the machine goes away.</summary>
    private readonly DetailsState _state = new();

    public DetailsWindow(Action<string, string?> signIn) {
        _signIn = signIn;
        Title = "IX Ledger";
        Width = DetailsView.Width;
        Height = 720;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (OperatingSystem.IsMacOS()) {
            // The title bar folds into the window, as in Marquee, so the traffic lights sit over the canvas.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 28;
        }
        Content = _scroll;
        ActualThemeVariantChanged += (_, _) => { if (_view is { } view) Show(view, _at); };
    }

    public void Show(TrayView view, DateTimeOffset at) {
        _view = view;
        _at = at;
        var palette = Palette.For(ActualThemeVariant);
        Background = palette.Brush(palette.Canvas);
        Styles.Clear();
        Styles.Add(DetailsView.ButtonStyles(palette));
        var offset = _scroll.Offset;
        _scroll.Content = DetailsView.Build(view, at, palette, _signIn, OperatingSystem.IsMacOS() ? 28 : 0, _state);
        _scroll.Offset = offset;
    }
}

/// <summary>What the reader opened on the page, kept while the page is rebuilt on each refresh.</summary>
internal sealed class DetailsState {
    public string? OpenMachine { get; set; }
}

/// <summary>Builds the details content from plain borders and text, so it looks the same in the window and when it is
/// rendered offscreen for screenshots.</summary>
internal static class DetailsView {
    public const double Width = 780;

    public static Control Build(TrayView view, DateTimeOffset at, Palette p, Action<string, string?>? signIn = null, double top = 0, DetailsState? state = null) {
        var page = new StackPanel { Spacing = 22, Margin = new Thickness(28, 24 + top, 28, 24) };
        page.Children.Add(Hero(view, at, p));
        if (view.Issues.Length > 0) {
            page.Children.Add(Section(p, "Needs you", view.Issues.Select(issue => IssueCard(issue, view, p, signIn)).ToArray()));
        }
        var machines = new[] { view.ThisMachine }.OfType<MachineView>().Concat(view.Others).ToArray();
        // With no machines there is nothing open; a machine that comes back later starts closed.
        if (machines.Length == 0 && state is not null) state.OpenMachine = null;
        if (machines.Length > 0) {
            var grid = new UniformGrid { Columns = Math.Min(3, machines.Length), ColumnSpacing = 12, RowSpacing = 12 };
            // One machine's facts at a time, under the cards; its Details button opens and closes it from the keyboard too.
            var facts = new ContentControl { IsVisible = false };
            state ??= new DetailsState();
            void Open(MachineView? machine) {
                state.OpenMachine = machine?.Machine;
                facts.Content = machine is null ? null : Facts(machine, p);
                facts.IsVisible = machine is not null;
            }
            void Toggle(MachineView machine) => Open(state.OpenMachine == machine.Machine ? null : machine);
            Open(machines.FirstOrDefault(machine => machine.Machine == state.OpenMachine));
            foreach (var machine in machines) grid.Children.Add(MachineCard(machine, machine == view.ThisMachine, p, signIn is null ? null : () => Toggle(machine)));
            page.Children.Add(Section(p, "Machines", signIn is null ? [grid] : [grid, facts]));
        }
        page.Children.Add(Section(p, "Recent changes", [Timeline(view, p)]));
        page.Children.Add(new TextBlock {
            Text = string.Join(" · ", new[] { view.Footer, "read " + at.ToLocalTime().ToString("HH:mm") }.Where(text => text.Length > 0)),
            Foreground = p.Brush(p.Muted), FontSize = 12, TextWrapping = TextWrapping.Wrap
        });
        return new Border { Background = p.Brush(p.Canvas), Child = page };
    }

    private static Control Hero(TrayView view, DateTimeOffset at, Palette p) {
        var color = p.Of(view.State);
        var badge = new Border {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = p.Brush(color, 0.16), VerticalAlignment = VerticalAlignment.Center,
            Child = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = p.Brush(color) }
        };
        var machines = (view.ThisMachine is null ? 0 : 1) + view.Others.Length;
        var sub = view.State switch {
            TrayState.Healthy => "Every machine is reporting and no sign-in problem is known. A grey dot is one the Ledger can't confirm.",
            TrayState.Down => "The Ledger isn't working, so nothing here is current.",
            TrayState.Starting => "Reading the Ledger…",
            _ => machines + (machines == 1 ? " machine" : " machines") + " watched. The cards below say what each one means and what to do."
        };
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = view.Headline, FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Text) });
        text.Children.Add(new TextBlock { Text = sub, FontSize = 14, Foreground = p.Brush(p.Secondary), TextWrapping = TextWrapping.Wrap });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18 };
        row.Children.Add(badge);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static Control Section(Palette p, string title, Control[] content) {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock {
            Text = title.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.2, Foreground = p.Brush(p.Muted)
        });
        foreach (var control in content) panel.Children.Add(control);
        return panel;
    }

    private static Border Card(Palette p, Control child, Thickness? padding = null) => new() {
        Background = p.Brush(p.Surface), CornerRadius = new CornerRadius(16), BorderBrush = p.Brush(p.Hairline), BorderThickness = new Thickness(1),
        Padding = padding ?? new Thickness(18, 16), Child = child
    };

    private static Control IssueCard(Issue issue, TrayView view, Palette p, Action<string, string?>? signIn) {
        var color = p.Of(issue.Severity);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = issue.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Text), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = issue.Meaning, FontSize = 14, Foreground = p.Brush(p.Secondary), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        var action = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        action.Children.Add(new TextBlock { Text = "→", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Accent), VerticalAlignment = VerticalAlignment.Top });
        // The action and the exact command or path on one line when they fit; the chip wraps under it when not.
        var what = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        what.Children.Add(new TextBlock { Text = issue.Action, FontSize = 14, Foreground = p.Brush(p.Text), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        if (issue.Command is { } command)
            what.Children.Add(new Border {
                Background = p.Brush(p.Raised), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 3), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new SelectableTextBlock { Text = command, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12.5,
                    Foreground = p.Brush(p.Text), TextWrapping = TextWrapping.Wrap }
            });
        Grid.SetColumn(what, 1);
        action.Children.Add(what);
        // Signing in starts here only for this machine; another machine's sign-in has to be done on it.
        var here = view.ThisMachine?.Machine == issue.Machine && issue.Provider is { } provider && issue.Severity == "bad" && issue.Command is not null;
        if (here && signIn is not null) {
            var binary = view.ThisMachine!.Providers.FirstOrDefault(line => line.Provider == issue.Provider)?.Details.FirstOrDefault(d => d.Label == "Binary").Value;
            var button = ActionButton(p, "Sign in…", primary: true);
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Click += (_, _) => signIn(issue.Provider!, binary);
            Grid.SetColumn(button, 2);
            action.Children.Add(button);
        }
        body.Children.Add(action);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*"), ColumnSpacing = 16 };
        row.Children.Add(new Border { Background = p.Brush(color), CornerRadius = new CornerRadius(2) });
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        return Card(p, row, new Thickness(14, 14, 18, 14));
    }

    private static Control MachineCard(MachineView machine, bool local, Palette p, Action? details) {
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(new TextBlock { Text = machine.Machine, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Text), VerticalAlignment = VerticalAlignment.Center });
        if (local)
            name.Children.Add(new Border {
                Background = p.Brush(p.Accent, 0.16), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "this one", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Accent) }
            });
        header.Children.Add(name);
        var reporter = Dot(p, machine.Reporter == "healthy" ? p.Good : p.Bad, 9);
        ToolTip.SetTip(reporter, machine.Reporter == "healthy" ? "Reporter is sending" : "Reporter: " + machine.Reporter);
        Grid.SetColumn(reporter, 1);
        header.Children.Add(reporter);
        panel.Children.Add(header);
        foreach (var provider in machine.Providers) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Background = Brushes.Transparent };
            row.Children.Add(Dot(p, p.Of(provider.Mark), 8));
            var label = new TextBlock { Text = provider.Name, FontSize = 14, Foreground = p.Brush(p.Text), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var state = new TextBlock {
                Text = provider.Short, FontSize = 13, Foreground = p.Brush(provider.Mark is TrayView.Bad or TrayView.Warn ? p.Of(provider.Mark) : p.Secondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            // The facts behind the label, on hover: account, scopes, versions, plan and use, binaries.
            ToolTip.SetTip(row, string.Join(Environment.NewLine, new[] { provider.Name + ": " + provider.Summary }
                .Concat(provider.Details.Select(detail => detail.Label + ": " + detail.Value.Replace(Environment.NewLine, Environment.NewLine + "    ")))));
            panel.Children.Add(row);
        }
        if (machine.Note is { } note) panel.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = p.Brush(p.Muted), TextWrapping = TextWrapping.Wrap });
        if (details is not null && machine.Providers.Any(provider => provider.Details.Length > 0)) {
            var button = ActionButton(p, "Details", primary: false);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => details();
            panel.Children.Add(button);
        }
        return Card(p, panel);
    }

    /// <summary>Every fact behind one machine's providers, selectable, for reading or copying a path.</summary>
    private static Control Facts(MachineView machine, Palette p) {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = machine.Machine, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Text) });
        foreach (var provider in machine.Providers.Where(provider => provider.Details.Length > 0)) {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 3 };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var title = new TextBlock { Text = provider.Name + " · " + provider.Summary, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Text),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumnSpan(title, 2);
            grid.Children.Add(title);
            for (var index = 0; index < provider.Details.Length; index++) {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = provider.Details[index].Label, FontSize = 13, Foreground = p.Brush(p.Muted) };
                var value = new SelectableTextBlock { Text = provider.Details[index].Value, FontSize = 13, Foreground = p.Brush(p.Text), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(label, index + 1);
                Grid.SetRow(value, index + 1);
                Grid.SetColumn(value, 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }
            panel.Children.Add(grid);
        }
        return Card(p, panel);
    }

    /// <summary>A flat rounded button in Marquee's look: the accent for the main action, a quiet outline otherwise.
    /// Its hover and pressed colours come from <see cref="ButtonStyles"/>.</summary>
    private static Button ActionButton(Palette p, string text, bool primary) {
        var button = new Button {
            Content = text, FontSize = primary ? 14 : 13, FontWeight = FontWeight.SemiBold, CornerRadius = new CornerRadius(9),
            Padding = primary ? new Thickness(16, 7) : new Thickness(12, 4), Cursor = new Cursor(StandardCursorType.Hand),
            Background = primary ? p.Brush(p.Accent) : Brushes.Transparent, Foreground = p.Brush(primary ? p.OnAccent : p.Secondary),
            BorderBrush = p.Brush(primary ? p.Accent : p.Hairline), BorderThickness = new Thickness(primary ? 0 : 1)
        };
        button.Classes.Add(primary ? "ledger-primary" : "ledger-quiet");
        return button;
    }

    /// <summary>Keeps the buttons' own colours on hover and press instead of the theme's grey.</summary>
    public static Styles ButtonStyles(Palette p) {
        Style Look(string name, string state, IBrush background, IBrush foreground) =>
            new(x => x.OfType<Button>().Class(name).Class(state).Template().OfType<ContentPresenter>()) {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, background), new Setter(ContentPresenter.ForegroundProperty, foreground) }
            };
        var hover = p.Brush(p.Accent, 0.85);
        return [
            Look("ledger-primary", ":pointerover", hover, p.Brush(p.OnAccent)),
            Look("ledger-primary", ":pressed", hover, p.Brush(p.OnAccent)),
            Look("ledger-quiet", ":pointerover", p.Brush(p.Raised), p.Brush(p.Text)),
            Look("ledger-quiet", ":pressed", p.Brush(p.Raised), p.Brush(p.Text)),
        ];
    }

    private static Control Timeline(TrayView view, Palette p) {
        if (view.Timeline.Length == 0)
            return Card(p, new TextBlock { Text = "Nothing has changed recently. Sign-ins, scopes, versions and config rewrites show up here.",
                FontSize = 14, Foreground = p.Brush(p.Secondary), TextWrapping = TextWrapping.Wrap });
        var list = new StackPanel { Spacing = 9 };
        foreach (var entry in view.Timeline) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock { Text = entry.When, FontSize = 13, Foreground = p.Brush(p.Muted), VerticalAlignment = VerticalAlignment.Center, MinWidth = 44 });
            var dot = Dot(p, p.Of(entry.Tone), 8);
            Grid.SetColumn(dot, 1);
            row.Children.Add(dot);
            var machine = new TextBlock { Text = entry.Machine, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = p.Brush(p.Secondary),
                VerticalAlignment = VerticalAlignment.Center, MinWidth = 76 };
            Grid.SetColumn(machine, 2);
            row.Children.Add(machine);
            var text = new TextBlock { Text = entry.Text, FontSize = 14, Foreground = p.Brush(p.Text), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 3);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return Card(p, list);
    }

    private static Border Dot(Palette p, Color color, double size) => new() {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = p.Brush(color), VerticalAlignment = VerticalAlignment.Center
    };
}
