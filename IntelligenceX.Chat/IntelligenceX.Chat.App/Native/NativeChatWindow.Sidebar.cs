using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildSidebar() {
        var shell = new Border {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = NativeControlBrushes.Border,
            Background = NativeControlBrushes.Rgb(249, 251, 253),
            Padding = new Thickness(14, 16, 14, 14)
        };
        var stack = new StackPanel {
            Spacing = 10
        };
        shell.Child = stack;

        stack.Children.Add(BuildSidebarHeader());
        _sidebarSearchBox = BuildSearchBox();
        _sidebarSearchBox.TextChanged += (_, _) => RenderSidebarItems();
        stack.Children.Add(_sidebarSearchBox);

        _sidebarItemsPanel = new StackPanel {
            Spacing = 9
        };
        stack.Children.Add(_sidebarItemsPanel);
        RenderSidebarItems();

        return shell;
    }

    private FrameworkElement BuildSidebarHeader() {
        var grid = new Grid {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel {
            Spacing = 1
        };
        title.Children.Add(new TextBlock {
            Text = "Workspaces",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        _selectedContextText = new TextBlock {
            Text = _selectedSidebarItem.Title,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextMuted
        };
        title.Children.Add(_selectedContextText);
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var native = new Border {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.AccentSoft,
            Child = new TextBlock {
                Text = "Native",
                FontSize = 11,
                Foreground = NativeControlBrushes.Accent
            }
        };
        Grid.SetColumn(native, 1);
        grid.Children.Add(native);
        return grid;
    }

    private static TextBlock BuildSectionHeader(string text) =>
        new() {
            Text = text.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextMuted,
            Margin = new Thickness(2, 8, 0, 0)
        };

    private static TextBox BuildSearchBox() =>
        new() {
            PlaceholderText = "Search projects and chats",
            MinHeight = 36,
            FontSize = 13,
            Padding = new Thickness(10, 5, 10, 5),
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.BorderStrong,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };

    private static Border BuildDivider() =>
        new() {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 2),
            Background = NativeControlBrushes.Border
        };

    private void RenderSidebarItems() {
        _sidebarItemsPanel.Children.Clear();
        var query = _sidebarSearchBox.Text;
        var lastCategory = string.Empty;
        var added = 0;
        foreach (var item in NativeSidebarItem.All) {
            if (!item.Matches(query)) {
                continue;
            }

            if (!string.Equals(lastCategory, item.Category, System.StringComparison.Ordinal)) {
                if (added > 0) {
                    _sidebarItemsPanel.Children.Add(BuildDivider());
                }

                _sidebarItemsPanel.Children.Add(BuildSectionHeader(item.Category));
                lastCategory = item.Category;
            }

            _sidebarItemsPanel.Children.Add(BuildNavigationRow(item));
            added++;
        }

        if (added == 0) {
            _sidebarItemsPanel.Children.Add(BuildSectionHeader("No matches"));
            _sidebarItemsPanel.Children.Add(BuildEmptySearchState());
        }
    }

    private Button BuildNavigationRow(NativeSidebarItem item) {
        var active = string.Equals(_selectedSidebarItem.Id, item.Id, System.StringComparison.Ordinal);
        var grid = new Grid {
            ColumnSpacing = 9
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new Border {
            Width = 3,
            MinHeight = 34,
            CornerRadius = new CornerRadius(2),
            Background = active ? GetCategoryAccent(item.Category) : NativeControlBrushes.Rgb(229, 234, 241),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(indicator, 0);
        grid.Children.Add(indicator);

        var textStack = new StackPanel {
            Spacing = 2
        };
        textStack.Children.Add(new TextBlock {
            Text = item.Title,
            FontSize = 13,
            FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = active ? NativeControlBrushes.TextPrimary : NativeControlBrushes.Rgb(45, 56, 72)
        });
        textStack.Children.Add(new TextBlock {
            Text = item.Subtitle,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = active ? NativeControlBrushes.TextSecondary : NativeControlBrushes.TextMuted
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var badge = BuildNavigationBadge(item.Badge, active, item.Category);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var button = new Button {
            Name = item.Title,
            Padding = new Thickness(9, 8, 9, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? NativeControlBrushes.Surface : NativeControlBrushes.Rgb(249, 251, 253),
            BorderBrush = active ? NativeControlBrushes.BorderStrong : NativeControlBrushes.Rgb(239, 243, 248),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Content = grid
        };
        button.Resources["ButtonBackgroundPointerOver"] = active
            ? NativeControlBrushes.Surface
            : NativeControlBrushes.Rgb(244, 247, 251);
        button.Resources["ButtonBackgroundPressed"] = active
            ? NativeControlBrushes.Rgb(248, 250, 252)
            : NativeControlBrushes.Rgb(238, 243, 249);
        button.Resources["ButtonBorderBrushPointerOver"] = active
            ? NativeControlBrushes.BorderStrong
            : NativeControlBrushes.Rgb(220, 228, 238);
        button.Resources["ButtonBorderBrushPressed"] = active
            ? NativeControlBrushes.BorderStrong
            : NativeControlBrushes.Rgb(209, 219, 232);
        button.Click += (_, _) => SelectSidebarItem(item);
        return button;
    }

    private void SelectSidebarItem(NativeSidebarItem item) {
        _selectedSidebarItem = item;
        _selectedContextText.Text = item.Title;
        _workspaceTitleText.Text = item.WorkspaceTitle;
        _workspaceSubtitleText.Text = item.WorkspaceSubtitle;
        if (_sampleDataRequested) {
            LoadSampleTranscript(item);
        }

        RenderSidebarItems();
    }

    private static Border BuildEmptySearchState() =>
        new() {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = new TextBlock {
                Text = "No project, chat, or artifact matches this search.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = NativeControlBrushes.TextMuted
            }
        };

    private static Border BuildNavigationBadge(string text, bool active, string category) =>
        new() {
            Padding = new Thickness(7, 3, 7, 3),
            CornerRadius = new CornerRadius(9),
            Background = active ? NativeControlBrushes.AccentSoft : NativeControlBrushes.Rgb(238, 242, 247),
            Child = new TextBlock {
                Text = text,
                FontSize = 11,
                Foreground = active ? GetCategoryAccent(category) : NativeControlBrushes.TextMuted
            }
        };

    private static Microsoft.UI.Xaml.Media.Brush GetCategoryAccent(string category) =>
        category switch {
            "Projects" => NativeControlBrushes.Rgb(51, 100, 214),
            "Chats" => NativeControlBrushes.Rgb(14, 132, 115),
            "Pinned" => NativeControlBrushes.Rgb(132, 77, 192),
            _ => NativeControlBrushes.Accent
        };
}
