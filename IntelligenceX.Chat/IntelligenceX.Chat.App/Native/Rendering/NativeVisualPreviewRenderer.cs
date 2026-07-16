using ChartForgeX.VisualArtifacts;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Adapts the shared ChartForgeX artifact renderer to the byte shape consumed by WinUI.
/// </summary>
internal static class NativeVisualPreviewRenderer {
    public static NativeVisualPreview? TryRender(VisualArtifact? artifact) {
        if (artifact?.Model is null) {
            return null;
        }

        try {
            return new NativeVisualPreview(artifact.ToSvg(), artifact.ToPng());
        } catch (InvalidOperationException) {
            return null;
        }
    }
}
