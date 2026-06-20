# IntelligenceX Chat Native Visual Direction

This planning note captures the first visual target for replacing the WebView-first chat shell with a proper native WinUI 3 application. It is internal planning material, not public website content.

## Direction Boards

### Baseline Native Shell

![Native WinUI direction board](assets/native-winui-direction-board.png)

Use this as the broad product target: a native Windows app with a compact conversation rail, a central virtualized transcript, a bottom composer, a top runtime/status strip, and a right inspector for runtime, tools, and export.

### Operator Console Variant

![Operator console variant](assets/operator-console-variant.png)

Use this variant for the default daily operator workflow. It favors quick send/cancel, visible runtime state, native command bars on result cards, and a restrained right inspector.

### Artifact Workspace Variant

![Artifact workspace variant](assets/artifact-workspace-variant.png)

Use this variant for analysis-heavy turns. It treats assistant output as a set of reusable artifacts: table, timeline, flow, network map, metric strip, and export destinations.

## Visual Principles

- The app shell is native WinUI 3, not WebView-hosted HTML.
- The transcript is a native virtualized list with structured message and artifact items.
- Result cards are native hosts for typed artifacts, not string-built HTML.
- ChartForgeX owns reusable visual artifacts: charts, topology, flow diagrams, tables, timelines, metric strips, and raster/SVG export.
- OfficeIMO owns document export and document-object output such as DOCX, PPTX, XLSX, PDF, and package-ready generated reports.
- The app maps service protocol messages into view models and delegates rendering/export to reusable libraries.

## Chosen Default

Start from the operator console variant as the default shell. Pull the artifact tray behavior from the artifact workspace variant once the transcript and result-card host are stable.

The default app should feel like a fast operational tool:

- quiet colors
- dense but readable panels
- native command affordances
- no landing-page styling
- no decorative background treatment
- no nested card shell
- visible runtime honesty when tools, service, auth, or model state is degraded

## Red Lines

- Do not keep the WebView shell and call it native.
- Do not build Mermaid/network/table renderers inside IntelligenceX only.
- Do not make ChartForgeX depend on IntelligenceX concepts.
- Do not put host-specific data collection, AD calculations, provider auth, or chat-routing logic into ChartForgeX.
- Do not hide slow startup or failed renderer support behind optimistic status text.
