using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildSidebar() {
        var shell = new Border {
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = NativeControlBrushes.Border,
            Background = NativeControlBrushes.SurfaceMuted,
            Padding = new Thickness(14, 16, 14, 14)
        };
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Child = grid;

        var header = BuildSidebarHeader();
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _sidebarSearchBox = BuildSearchBox();
        _sidebarSearchBox.TextChanged += (_, _) => RenderSidebarItems();
        Grid.SetRow(_sidebarSearchBox, 1);
        grid.Children.Add(_sidebarSearchBox);

        _queuedTurnsPanel = BuildQueuedTurnsPanel();
        Grid.SetRow(_queuedTurnsPanel, 2);
        grid.Children.Add(_queuedTurnsPanel);

        _sidebarItemsPanel = new ListView {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollMode(_sidebarItemsPanel, ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_sidebarItemsPanel, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(_sidebarItemsPanel, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(_sidebarItemsPanel, ScrollBarVisibility.Disabled);
        Grid.SetRow(_sidebarItemsPanel, 3);
        grid.Children.Add(_sidebarItemsPanel);
        RenderSidebarItems();
        return shell;
    }

    private Border BuildQueuedTurnsPanel() {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _queuedTurnsText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = NativeControlBrushes.WarningText
        };
        grid.Children.Add(_queuedTurnsText);

        _runQueuedTurnButton = new Button {
            Content = "Run next",
            MinHeight = 30,
            Padding = new Thickness(8, 4, 8, 4)
        };
        _runQueuedTurnButton.Click += async (_, _) => {
            var queuedTask = _viewModel.RunNextQueuedTurnAsync();
            _activeSendTask = queuedTask;
            _ = await queuedTask.ConfigureAwait(true);
            if (_lifetimeCts.IsCancellationRequested) {
                return;
            }
            RefreshConversationChrome();
        };
        Grid.SetColumn(_runQueuedTurnButton, 1);
        grid.Children.Add(_runQueuedTurnButton);

        _clearQueuedTurnsButton = new Button {
            Content = "Clear",
            MinHeight = 30,
            Padding = new Thickness(8, 4, 8, 4)
        };
        _clearQueuedTurnsButton.Click += async (_, _) => await ClearQueuedTurnsFromNativeAsync().ConfigureAwait(true);
        Grid.SetColumn(_clearQueuedTurnsButton, 2);
        grid.Children.Add(_clearQueuedTurnsButton);

        var panel = new Border {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.WarningSoft,
            BorderBrush = NativeControlBrushes.Rgb(242, 211, 143),
            BorderThickness = new Thickness(1),
            Child = grid,
            Visibility = Visibility.Collapsed
        };
        return panel;
    }

    private void RenderQueuedTurnsState() {
        if (_queuedTurnsPanel is null || _queuedTurnsText is null) {
            return;
        }

        var count = _viewModel.QueuedTurns.Count;
        _queuedTurnsPanel.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _queuedTurnsText.Text = count == 1 ? "1 queued turn" : count + " queued turns";
        UpdateCommandState();
    }

    private async Task ClearQueuedTurnsFromNativeAsync() {
        var dialog = new ContentDialog {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Clear queued turns?",
            Content = "Remove all prompts waiting in this profile?",
            PrimaryButtonText = "Clear queue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) {
            if (_lifetimeCts.IsCancellationRequested) {
                return;
            }

            _ = await TrackPersistenceTaskAsync(_viewModel.ClearQueuedTurnsAsync()).ConfigureAwait(true);
        }
    }

    private FrameworkElement BuildSidebarHeader() {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 1 };
        title.Children.Add(new TextBlock {
            Text = "Chats",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        _selectedContextText = new TextBlock {
            Text = _viewModel.ActiveConversation.Title,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextMuted
        };
        title.Children.Add(_selectedContextText);
        grid.Children.Add(title);

        var addButton = new Button {
            Content = "New",
            MinHeight = 30,
            Padding = new Thickness(10, 4, 10, 4)
        };
        addButton.Click += async (_, _) => {
            _ = await _viewModel.CreateConversationAsync().ConfigureAwait(true);
            RefreshConversationChrome();
            _composer.Focus(FocusState.Programmatic);
        };
        Grid.SetColumn(addButton, 1);
        grid.Children.Add(addButton);
        return grid;
    }

    private static TextBox BuildSearchBox() =>
        new() {
            PlaceholderText = "Search chat history",
            MinHeight = 36,
            FontSize = 13,
            Padding = new Thickness(10, 5, 10, 5),
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.BorderStrong,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };

    private void RenderSidebarItems() {
        if (_sidebarItemsPanel is null || _sidebarSearchBox is null) {
            return;
        }

        _sidebarItemsPanel.Items.Clear();
        var query = _sidebarSearchBox.Text;
        for (var i = 0; i < _viewModel.Conversations.Count; i++) {
            var conversation = _viewModel.Conversations[i];
            if (conversation.Matches(query)) {
                _sidebarItemsPanel.Items.Add(BuildNavigationRow(conversation));
            }
        }

        if (_sidebarItemsPanel.Items.Count == 0) {
            _sidebarItemsPanel.Items.Add(new TextBlock {
                Text = "No chat matches this search.",
                Margin = new Thickness(8, 16, 8, 8),
                TextWrapping = TextWrapping.Wrap,
                Foreground = NativeControlBrushes.TextMuted
            });
        }
    }

    private FrameworkElement BuildNavigationRow(NativeConversation conversation) {
        var active = string.Equals(
            _viewModel.ActiveConversation.Id,
            conversation.Id,
            StringComparison.OrdinalIgnoreCase);
        var grid = new Grid { ColumnSpacing = 9 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new Border {
            Width = 3,
            MinHeight = 34,
            CornerRadius = new CornerRadius(2),
            Background = active ? NativeControlBrushes.Accent : NativeControlBrushes.BorderStrong,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        grid.Children.Add(indicator);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock {
            Text = conversation.Title,
            FontSize = 13,
            FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextPrimary
        });
        text.Children.Add(new TextBlock {
            Text = conversation.Subtitle,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextMuted
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var badge = new Border {
            Padding = new Thickness(7, 3, 7, 3),
            CornerRadius = new CornerRadius(9),
            Background = active ? NativeControlBrushes.AccentSoft : NativeControlBrushes.NeutralSoft,
            Child = new TextBlock {
                Text = conversation.Badge,
                FontSize = 11,
                Foreground = active ? NativeControlBrushes.Accent : NativeControlBrushes.TextMuted
            }
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var button = new Button {
            Padding = new Thickness(9, 8, 9, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? NativeControlBrushes.Surface : NativeControlBrushes.SurfaceMuted,
            BorderBrush = active ? NativeControlBrushes.BorderStrong : NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Content = grid,
            IsEnabled = !_viewModel.IsSending
        };
        button.Click += async (_, _) => {
            _ = await _viewModel.SelectConversationAsync(conversation.Id).ConfigureAwait(true);
            RefreshConversationChrome();
            _composer.Focus(FocusState.Programmatic);
        };

        var row = new Grid {
            ColumnSpacing = 4,
            Margin = new Thickness(0, 0, 0, 7)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(button);

        var deleteButton = new Button {
            Content = new SymbolIcon(Symbol.Delete),
            MinWidth = 34,
            MinHeight = 34,
            Padding = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !_viewModel.IsSending
        };
        ToolTipService.SetToolTip(deleteButton, "Delete this conversation");
        deleteButton.Click += async (_, _) => await DeleteConversationFromNativeAsync(conversation).ConfigureAwait(true);
        Grid.SetColumn(deleteButton, 1);
        row.Children.Add(deleteButton);
        return row;
    }

    private async Task DeleteConversationFromNativeAsync(NativeConversation conversation) {
        var dialog = new ContentDialog {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Delete conversation?",
            Content = "Delete '" + conversation.Title + "' from local history?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) {
            return;
        }

        if (_lifetimeCts.IsCancellationRequested) {
            return;
        }

        _ = await TrackPersistenceTaskAsync(_viewModel.DeleteConversationAsync(conversation.Id)).ConfigureAwait(true);
        if (_lifetimeCts.IsCancellationRequested) {
            return;
        }
        RefreshConversationChrome();
        _composer.Focus(FocusState.Programmatic);
    }

    private void RefreshConversationChrome() {
        if (_selectedContextText is null || _workspaceTitleText is null || _workspaceSubtitleText is null) {
            return;
        }

        var conversation = _viewModel.ActiveConversation;
        _selectedContextText.Text = conversation.Title;
        _workspaceTitleText.Text = conversation.Title;
        _workspaceSubtitleText.Text = conversation.Messages.Count == 0
            ? "New conversation"
            : conversation.Messages.Count + " messages / persisted locally";
        RenderSidebarItems();
    }
}
