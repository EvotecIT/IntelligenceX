using System;
using ChartForgeX.VisualArtifacts;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Adapts the shared ChartForgeX artifact renderer to the byte shape consumed by WinUI.
/// </summary>
internal static class NativeVisualPreviewRenderer {
    public static NativeVisualPreview? TryRender(VisualArtifact? artifact, out string? error) {
        error = null;
        if (artifact?.Model is null) {
            return null;
        }

        try {
            return new NativeVisualPreview(artifact.ToSvg(), artifact.ToPng());
        } catch (Exception ex) {
            error = ex.Message;
            return null;
        }
    }
}
