#if IXCHAT_NATIVE_MARKDOWN_ENGINES
using System;
using ChartForgeX;
using ChartForgeX.Core;
using ChartForgeX.Topology;
using ChartForgeX.VisualArtifacts;
using ChartForgeX.VisualBlocks;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Renders reusable ChartForgeX artifact models into native preview bytes.
/// </summary>
internal static class NativeVisualPreviewRenderer {
    public static NativeVisualPreview? TryRender(object? artifact) {
        var model = GetModel(artifact);
        if (model == null) {
            return null;
        }

        try {
            return model switch {
                Chart chart => new NativeVisualPreview(chart.ToSvg(), chart.ToPng()),
                TopologyChart topology => new NativeVisualPreview(topology.ToSvg(), topology.ToPng()),
                TableArtifact table => new NativeVisualPreview(table.ToSvg(), table.ToPng()),
                SequenceArtifact sequence => new NativeVisualPreview(sequence.ToSvg(), sequence.ToPng()),
                IVisualBlock visualBlock => new NativeVisualPreview(visualBlock.ToSvg(), visualBlock.ToPng()),
                _ => null
            };
        } catch {
            return null;
        }
    }

    private static object? GetModel(object? artifact) {
        if (artifact is VisualArtifact visualArtifact) {
            return visualArtifact.Model;
        }

        return artifact;
    }
}
#else
namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// No-op visual preview renderer used until ChartForgeX native engines are supplied.
/// </summary>
internal static class NativeVisualPreviewRenderer {
    public static NativeVisualPreview? TryRender(object? artifact) => null;
}
#endif
