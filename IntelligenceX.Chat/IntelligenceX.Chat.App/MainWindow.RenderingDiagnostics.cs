using System;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static void StartupLogRendererDiagnostics() {
        try {
            StartupLog.Write("Renderer.MarkdownRenderer contract=" + OfficeImoMarkdownRuntimeContract.DescribeMarkdownRendererContract());
            StartupLog.Write("Renderer.OfficeIMOMarkdown contract=" + OfficeImoMarkdownRuntimeContract.DescribeMarkdownContract());
            StartupLog.Write("Renderer.OfficeIMOWordMarkdown contract=" + OfficeImoMarkdownRuntimeContract.DescribeWordMarkdownContract());
        } catch (Exception ex) {
            StartupLog.Write("Renderer diagnostics failed: " + ex.Message);
        }
    }
}
