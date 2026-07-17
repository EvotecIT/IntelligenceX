using System;
using ChartForgeX.VisualArtifacts;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Interactive pan, zoom, and fit viewport for ChartForgeX visual artifacts.
/// </summary>
internal sealed class NativeVisualWorkspaceControl : UserControl {
    private const float MinimumZoom = 0.2f;
    private const float MaximumZoom = 5f;
    private readonly NativeTranscriptVisual _visual;
    private readonly Image _image;
    private readonly ScrollViewer _viewport;
    private readonly TextBlock _status;
    private readonly double _contentWidth;
    private readonly double _contentHeight;
    private bool _fitPending = true;
    private bool _isPanning;
    private uint _panPointerId;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    public NativeVisualWorkspaceControl(NativeTranscriptVisual visual) {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        var naturalSize = visual.Artifact?.NaturalSize;
        _contentWidth = naturalSize?.Width ?? 1200;
        _contentHeight = naturalSize?.Height ?? 700;
        _image = new Image {
            Width = _contentWidth,
            Height = _contentHeight,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _image.PointerPressed += OnPointerPressed;
        _image.PointerMoved += OnPointerMoved;
        _image.PointerReleased += OnPointerReleased;
        _image.PointerCanceled += OnPointerReleased;
        _image.PointerCaptureLost += OnPointerCaptureLost;

        _viewport = new ScrollViewer {
            Content = _image,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = MinimumZoom,
            MaxZoomFactor = MaximumZoom,
            Background = NativeControlBrushes.SurfaceMuted
        };
        _viewport.PointerWheelChanged += OnPointerWheelChanged;
        _viewport.ViewChanged += (_, _) => UpdateStatus();
        _viewport.SizeChanged += (_, _) => {
            if (!_fitPending) return;
            _fitPending = false;
            FitToViewport();
        };
        _status = new TextBlock {
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        };
        Content = Build();
        if (visual.Preview != null) {
            _ = NativeVisualImageLoader.LoadAsync(
                _image,
                visual.Preview,
                rasterWidth: _contentWidth * 2,
                rasterHeight: _contentHeight * 2);
        }
        UpdateStatus();
    }

    private FrameworkElement Build() {
        var toolbar = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = {
                BuildButton("Fit", FitToViewport),
                BuildButton("100%", () => SetZoom(1)),
                BuildButton("−", () => SetZoom(_viewport.ZoomFactor / 1.2f)),
                BuildButton("+", () => SetZoom(_viewport.ZoomFactor * 1.2f)),
                _status
            }
        };
        var root = new Grid {
            RowSpacing = 10,
            RowDefinitions = {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            },
            Children = { toolbar, _viewport }
        };
        Grid.SetRow(_viewport, 1);
        return root;
    }

    private static Button BuildButton(string label, Action action) {
        var button = new Button { Content = label, MinHeight = 34, MinWidth = 48 };
        button.Click += (_, _) => action();
        return button;
    }

    private void FitToViewport() {
        if (_viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
        SetZoom(NativeVisualViewportBehavior.CalculateFitZoom(
            _viewport.ActualWidth,
            _viewport.ActualHeight,
            _contentWidth,
            _contentHeight,
            MinimumZoom,
            MaximumZoom), resetOffsets: true);
    }

    private void SetZoom(float zoom, bool resetOffsets = false) {
        var bounded = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        _viewport.ChangeView(
            resetOffsets ? 0 : null,
            resetOffsets ? 0 : null,
            bounded,
            disableAnimation: true);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args) {
        if ((args.KeyModifiers & VirtualKeyModifiers.Control) == 0) return;
        var delta = args.GetCurrentPoint(_viewport).Properties.MouseWheelDelta;
        if (delta == 0) return;
        SetZoom(NativeVisualViewportBehavior.CalculateWheelZoom(
            _viewport.ZoomFactor,
            delta,
            MinimumZoom,
            MaximumZoom));
        args.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) {
        var point = args.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;
        _isPanning = _image.CapturePointer(args.Pointer);
        if (!_isPanning) return;
        _panPointerId = args.Pointer.PointerId;
        _panStart = point.Position;
        _panHorizontalOffset = _viewport.HorizontalOffset;
        _panVerticalOffset = _viewport.VerticalOffset;
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args) {
        if (!_isPanning || args.Pointer.PointerId != _panPointerId) return;
        var current = args.GetCurrentPoint(_viewport).Position;
        _viewport.ChangeView(
            NativeVisualViewportBehavior.CalculatePanOffset(
                _panHorizontalOffset,
                current.X - _panStart.X,
                _viewport.ScrollableWidth),
            NativeVisualViewportBehavior.CalculatePanOffset(
                _panVerticalOffset,
                current.Y - _panStart.Y,
                _viewport.ScrollableHeight),
            null,
            disableAnimation: true);
        args.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args) {
        if (!_isPanning || args.Pointer.PointerId != _panPointerId) return;
        _image.ReleasePointerCapture(args.Pointer);
        _isPanning = false;
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        _isPanning = false;

    private void UpdateStatus() {
        if (_status == null) return;
        _status.Text = Math.Round(_viewport.ZoomFactor * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "% · drag to pan · Ctrl+wheel to zoom";
    }
}
