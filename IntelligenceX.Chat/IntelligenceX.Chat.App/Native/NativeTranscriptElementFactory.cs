using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Materializes and recycles native transcript controls for the virtualized chat surface.
/// </summary>
internal sealed class NativeTranscriptElementFactory : IElementFactory {
    private readonly Stack<NativeTranscriptMessageControl> _recycled = new();

    /// <inheritdoc />
    public UIElement GetElement(ElementFactoryGetArgs args) {
        var control = _recycled.TryPop(out var recycled)
            ? recycled
            : new NativeTranscriptMessageControl();
        control.DataContext = args.Data as NativeChatTranscriptItem;
        return control;
    }

    /// <inheritdoc />
    public void RecycleElement(ElementFactoryRecycleArgs args) {
        if (args.Element is NativeTranscriptMessageControl control) {
            control.DataContext = null;
            _recycled.Push(control);
        }
    }
}
