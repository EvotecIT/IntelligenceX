using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// The full picture behind the menu: the Ledger's health, each machine's GitHub, Codex and Claude state, every fact
/// behind this machine's state, and the change history. Read-only; it follows the system's light or dark theme.
/// </summary>
internal sealed class DetailsWindow : Window {
    private readonly StackPanel _content = new() { Spacing = 14, Margin = new Thickness(20) };

    /// <summary>The whole content, including what is scrolled out of view.</summary>
    internal StackPanel Body => _content;

    public DetailsWindow() {
        Title = "IX Ledger";
        Width = 820;
        Height = 700;
        MinWidth = 520;
        MinHeight = 360;
        Content = new ScrollViewer { Content = _content };
    }

    public void Show(TrayView view, DateTimeOffset at) {
        _content.Children.Clear();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(TrayArt.StateColor(view.State)),
            VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = view.Headline, FontSize = 22, FontWeight = FontWeight.SemiBold });
        _content.Children.Add(header);
        foreach (var line in view.LedgerLines) _content.Children.Add(Muted(line));
        _content.Children.Add(Muted("Read " + at.ToLocalTime().ToString("HH:mm:ss")));
        if (view.Problems.Length > 0) {
            _content.Children.Add(Heading("Needs a look"));
            foreach (var problem in view.Problems) _content.Children.Add(new TextBlock { Text = "• " + problem, TextWrapping = TextWrapping.Wrap });
        }

        var machines = new[] { view.ThisMachine }.OfType<MachineView>().Concat(view.Others).ToArray();
        if (machines.Length > 0) {
            _content.Children.Add(Heading("Machines"));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,*,*"), ColumnSpacing = 18, RowSpacing = 6 };
            var titles = new[] { "Machine", "Reporter", "GitHub", "Codex", "Claude" };
            for (var column = 0; column < titles.Length; column++) Place(grid, Bold(titles[column]), 0, column);
            for (var row = 0; row < machines.Length; row++) {
                var machine = machines[row];
                Place(grid, new TextBlock { Text = machine.Machine + (machine == view.ThisMachine ? " (this)" : "") }, row + 1, 0);
                Place(grid, Marked(machine.Reporter == "healthy" ? TrayView.Ok : TrayView.Bad, machine.Reporter), row + 1, 1);
                for (var index = 0; index < machine.Providers.Length && index < 3; index++)
                    Place(grid, Marked(machine.Providers[index].Mark, machine.Providers[index].Summary), row + 1, index + 2);
            }
            _content.Children.Add(grid);
            foreach (var machine in machines.Where(m => m.Note is not null)) _content.Children.Add(Muted(machine.Machine + ": " + machine.Note));
        }

        if (view.ThisMachine is { } local) {
            _content.Children.Add(Heading("On " + local.Machine));
            foreach (var provider in local.Providers) {
                _content.Children.Add(Marked(provider.Mark, provider.Name, bold: true));
                var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, RowSpacing = 2, Margin = new Thickness(24, 0, 0, 6) };
                for (var row = 0; row < provider.Details.Length; row++) {
                    Place(facts, Muted(provider.Details[row].Label), row, 0);
                    Place(facts, new SelectableTextBlock { Text = provider.Details[row].Value, TextWrapping = TextWrapping.Wrap }, row, 1);
                }
                _content.Children.Add(facts);
            }
        }

        _content.Children.Add(Heading("Changes"));
        if (view.Changes.Length == 0) _content.Children.Add(Muted("No changes recorded yet. Each change in sign-in, scopes, binaries or files shows here."));
        foreach (var change in view.Changes) _content.Children.Add(new SelectableTextBlock { Text = change, TextWrapping = TextWrapping.Wrap });
    }

    private static void Place(Grid grid, Control control, int row, int column) {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

    private static TextBlock Bold(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private static TextBlock Muted(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Marked(string mark, string text, bool bold = false) {
        var color = mark switch {
            TrayView.Ok => TrayArt.StateColor(TrayState.Healthy), TrayView.Bad => TrayArt.StateColor(TrayState.Down),
            TrayView.Warn => TrayArt.StateColor(TrayState.Degraded), _ => TrayArt.StateColor(TrayState.Starting)
        };
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal };
        block.Inlines!.Add(new Run(mark + " ") { Foreground = new SolidColorBrush(color), FontWeight = FontWeight.Bold });
        block.Inlines.Add(new Run(text));
        return block;
    }
}
