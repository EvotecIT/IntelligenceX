# IntelligenceX Chat Native UI Contract

This is the first native UI contract for the WinUI 3 rebuild. It describes what the app should own, what reusable libraries should own, and what must be proven before replacing the current WebView shell.

## Product Goal

Build a proper `.NET 10` WinUI 3 desktop chat app that uses native controls for the shell, transcript, composer, settings, and artifact hosting. Keep the existing chat service/protocol where it is useful. Remove the HTML shell as the primary UI path.

## Application Shape

Project name options:

- `IntelligenceX.Chat.App.Native`
- `IntelligenceX.Chat.WinUI`

The project should live beside the existing app until it proves itself. Do not mutate the existing WebView shell into a half-native hybrid.

## Owned By IntelligenceX

IntelligenceX should own:

- app startup and service lifecycle
- named-pipe client connection using `IntelligenceX.Chat.Client`
- mapping `ChatServiceMessage` and app state into native view models
- conversations, active turn state, queued prompts, and cancel state
- native shell layout
- user preferences and profile/runtime settings
- local persistence of UI state
- app packaging via existing PowerForge flow

IntelligenceX should not own reusable chart, topology, Mermaid, table-rendering, raster, or document-export engines.

## Core Screens

### Shell

Native root window with:

- app title and account/runtime status
- compact conversation rail
- transcript workspace
- right inspector
- bottom composer
- native window/titlebar handling

No WebView root content.

### Conversation Rail

Native list of recent conversations:

- new chat button
- rename/delete actions
- active model/runtime hint
- unread/running indicator
- bounded persisted history

Use virtualization when the list grows.

### Transcript

Native virtualized transcript list:

- user turns
- assistant final turns
- assistant provisional/draft turns
- tool activity turns
- system notices
- export notices
- structured artifact cards

Transcript items should be immutable snapshots where possible. Streaming should update a small active-turn model, not rebuild the whole transcript tree for every delta.

### Composer

Native multiline input:

- send button
- cancel/stop button during active turns
- paste/send clipboard action
- queued prompt indicator
- runtime unavailable warning state

Keyboard behavior must be predictable: Enter sends, Shift+Enter inserts a new line, unless user preferences later choose otherwise.

### Inspector

Right panel with tabs:

- `Runtime`
- `Tools`
- `Export`
- `Evidence`
- `Debug` only when enabled

Use native tabs, toggles, combo boxes, number boxes, progress rings, and command buttons. Avoid giant form pages copied from the old HTML options panel.

## Result Card Contract

The native transcript should host result cards through a single app-owned control:

`ResultCardHost`

The host receives typed artifact descriptors and chooses the right native control:

- text/markdown summary
- table
- chart
- topology/network
- Mermaid-style flow
- timeline
- metric strip
- image/raster preview
- exported document link

The app can own the card chrome: title, subtitle, confidence/status, command bar, copy/save/pop-out/export actions. The card body should come from reusable rendering models.

## Markdown And Markup Contract

The app must still support Markdown as the assistant/user-facing content format, but Markdown should not force the app back into an HTML shell.

Native transcript rendering should split content into:

- normal Markdown prose and code blocks rendered by the app/Markdown layer
- ChartForgeX fenced visual blocks parsed into typed artifacts
- Mermaid fenced blocks parsed into typed ChartForgeX flow artifacts where supported
- unsupported visual fences shown as safe code blocks with diagnostics

The desired flow is:

```text
assistant markdown
  -> OfficeIMO Markdown native document projection
  -> prose/code/table/native text items
  -> OfficeIMO semantic visual fenced blocks
  -> ChartForgeX typed visual artifacts
  -> ResultCardHost
```

Published OfficeIMO packages now provide an AST-backed native block projection for paragraphs, code, tables, visuals, and source spans: `OfficeIMO.Markdown` `0.6.30`, `OfficeIMO.MarkdownRenderer` `0.2.30`, and `OfficeIMO.MarkdownRenderer.IntelligenceX` `0.1.3`. Published ChartForgeX packages now provide `VisualMarkupBlock` plus `ParseBlocks(...)` so already-discovered visual fences can become artifacts without rescanning Markdown: `ChartForgeX` `0.1.8`, `ChartForgeX.Markup` `0.1.6`, `ChartForgeX.Mermaid` `0.1.1`, and `ChartForgeX.Markup.Mermaid` `0.1.1`. The native app should not add a third Markdown parser.

Minimum fence support for the native app:

- `chartforgex topology`
- `cfx topology`
- `chartforgex flow`
- `cfx flow`
- `mermaid` for the supported flowchart subset
- `chartforgex table` or `cfx table` once the reusable table artifact exists

IntelligenceX can keep a thin adapter that maps OfficeIMO blocks and ChartForgeX parser results to app view models, but Markdown parsing belongs in OfficeIMO and visual models belong in ChartForgeX.

The current IX bridge is enabled in package-mode builds. Local project references remain available only for validating unreleased OfficeIMO or ChartForgeX changes:

```powershell
/p:UseLocalNativeMarkdownEngines=true
/p:OfficeImoRepoRoot=C:\Support\GitHub\OfficeIMO-markdown-native-projection
/p:ChartForgeXRepoRoot=C:\Support\GitHub\ChartForgeX-visual-artifact-table-foundation
```

Default IX builds intentionally stay package-based. They should not depend on sibling source checkouts.

## Artifact Descriptor Shape

The app needs a small internal contract that can later align with ChartForgeX:

```csharp
internal sealed record ChatVisualArtifact(
    string Id,
    string Kind,
    string Title,
    string? Subtitle,
    string Source,
    object Model,
    ChatVisualArtifactActions Actions);
```

This should remain an adapter-side shape until ChartForgeX exposes its own product-neutral descriptor. Do not bake IntelligenceX names into ChartForgeX.

## Interactive Table Contract

The current WebView shell uses DataTables for more than visual table rendering. It provides global search, sort state, ordered/filtered row extraction, quick export, CSV/XLSX/DOCX export, transcript table copy, and a larger data-view workspace. The native app must preserve those workflows as native behavior, not as embedded HTML.

Treat tables as structured artifacts:

```text
assistant markdown or tool rows
  -> table artifact descriptor
  -> transcript preview card
  -> native data workspace for full interaction
  -> OfficeIMO/CSV/XLSX/DOCX export path
```

ChartForgeX should describe table data and capabilities. IntelligenceX should own the app-specific workspace, commands, persistence, and export routing.

Minimum native table capabilities:

- virtualized rows and columns for large tool outputs
- stable typed columns with labels, ids, formatting, alignment, width hints, and null handling
- global search and per-column filter hooks
- single-column and multi-column sort state
- row, cell, and range selection
- copy cell, copy row, copy selected rows, copy visible/filtered rows, and copy all rows
- quick export using user preferences
- CSV, XLSX, and DOCX export commands
- column hide/show and reset layout
- details panel for selected row when cells are too wide for the grid
- keyboard navigation and accessible names
- explicit empty, loading, error, and truncated-data states

Large tables should not be rendered inline as thousands of transcript elements. A transcript item should show a compact preview and open a native data workspace for full search/sort/filter/export behavior.

The native app should not parse rendered HTML tables to recover data. If Markdown contains a table, the Markdown/table parser should emit a typed table artifact. If a tool returns structured rows, those rows should bypass Markdown and become a table artifact directly.

## ChartForgeX Integration

Preferred long-term flow:

```text
ChatServiceMessage
  -> NativeViewModel
  -> ResultCardHost
  -> ChartForgeX visual model or visual artifact descriptor
  -> WinUI adapter control, SVG, PNG, or OfficeIMO export path
```

Short-term flow may use SVG/PNG from ChartForgeX inside a native image surface, but the target is reusable native artifact descriptors and optional WinUI adapter controls.

## OfficeIMO Integration

OfficeIMO owns document output:

- DOCX transcript
- PPTX artifact deck
- XLSX table/export workbook
- PDF report output

The native app should pass structured artifacts and transcript content into OfficeIMO-oriented exporters. It should not recreate document layout engines.

## What We Are Missing In IntelligenceX

- A native app project that does not use WebView as the shell. The native shell is the default launch path; the legacy WebView shell remains available only through `IXCHAT_LEGACY_WEBVIEW=1`.
- View-model boundaries that can be tested without constructing a WinUI window. First native chat and launch-mode view models exist, including typed authentication readiness for live empty-state UI.
- A native transcript item model separate from HTML fragments.
- A Markdown block pipeline that emits native text/code items plus typed visual artifacts. First adapter is enabled in package-mode builds.
- A native result-card host.
- A native table workspace model with search, per-column filters, sort, windowed rendering, column hide/show reset, visible/selected/all row projections, row selection, and TSV/CSV copy. First in-memory model and dialog surface exist for Markdown table previews.
- Native inspector pages for runtime/tools/export/evidence.
- A renderer abstraction that lets ChartForgeX and OfficeIMO own artifacts.
- Startup metrics that measure native UI readiness separately from service readiness.
- Screenshot-based visual proof for the native app.

## What We Are Missing In ChartForgeX

- A product-neutral visual artifact descriptor suitable for native hosts. First `VisualArtifact` exists in published ChartForgeX packages.
- A general Markdown visual-block scanner/dispatcher beyond the current topology-only fence extraction. First `VisualMarkupParser` and `ParseBlocks(...)` exist in published ChartForgeX packages.
- A native-host adapter package or guidance for WinUI consumption.
- A Mermaid-compatible flow parser/renderer path for common flowchart syntax. First Mermaid markup adapter exists in published ChartForgeX packages.
- A reusable data-table artifact model with native interaction capabilities and export-friendly metadata. First table artifact contract exists in published ChartForgeX packages; WinUI control parity is still missing.
- A timeline/activity artifact model for tool and turn evidence.
- Hit-test/selection metadata that native hosts can use without parsing SVG/HTML.
- A reusable sample app or smoke harness proving native consumption outside IntelligenceX.

## Proof Gates

Before replacing the current app:

- `dotnet build` native app in Release.
- Native app unit tests for view-model mapping and state persistence.
- Screenshot proof of the shell at desktop and narrow widths.
- Native transcript stress test with hundreds of turns.
- Streaming turn test that does not rebuild all visible items for every delta.
- Native table parity proof for search, sort, selection, copy, quick export, CSV/XLSX/DOCX export, and large-row virtualization.
- ChartForgeX artifact render proof for table, flow, topology, metric card, and PNG/SVG export.
- OfficeIMO export proof for transcript plus at least one table and one visual artifact.
- PowerForge portable/MSI packaging proof.
