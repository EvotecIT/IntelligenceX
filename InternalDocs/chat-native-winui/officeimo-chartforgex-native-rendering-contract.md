# OfficeIMO + ChartForgeX Native Rendering Contract

This brief defines the reusable rendering boundary for the native WinUI 3 chat rebuild. The goal is to avoid an IntelligenceX-only Markdown parser, avoid HTML-as-shell rendering, and keep the reusable engines in the right repositories.

## Current Repo Facts

- Published OfficeIMO packages now provide the native Markdown projection path: `OfficeIMO.Markdown` `0.6.30`, `OfficeIMO.MarkdownRenderer` `0.2.30`, and `OfficeIMO.MarkdownRenderer.IntelligenceX` `0.1.3`.
- `OfficeIMO.MarkdownRenderer` already has renderer presets and a parse result that carries a final `MarkdownDoc`, syntax trees, and diagnostics.
- `OfficeIMO.MarkdownRenderer.Wpf` exists, but it is a WPF/WebView2 host. It is not the native WinUI answer.
- `OfficeIMO.MarkdownRenderer.IntelligenceX` already carries IX visual aliases and transcript presets.
- Published ChartForgeX packages now provide the visual artifact path: `ChartForgeX` `0.1.8`, `ChartForgeX.Markup` `0.1.6`, `ChartForgeX.Mermaid` `0.1.1`, and `ChartForgeX.Markup.Mermaid` `0.1.1`.
- ChartForgeX currently has a Markdown fence scanner for standalone CFX usage. That scanner should remain useful for CFX CLI/docs, but IX should not use it as its Markdown parser once OfficeIMO has parsed the document.
- `IntelligenceX.Chat.App` now consumes the published OfficeIMO and ChartForgeX packages by default. `UseLocalNativeMarkdownEngines=true` remains an optional source-validation lane for unreleased upstream work.

## Ownership

### OfficeIMO Owns Markdown

OfficeIMO must be the canonical Markdown parser and transcript Markdown contract owner.

It should expose a renderer-neutral document projection for native hosts:

- block order
- inline structure
- source spans
- diagnostics
- fenced code blocks
- semantic fenced visual blocks
- table blocks from Markdown tables
- normalized transcript text after OfficeIMO-owned preprocessing

IX must not re-implement Markdown block splitting.

### ChartForgeX Owns Visual Artifacts

ChartForgeX must be the canonical visual artifact engine.

It should own:

- `VisualArtifact`
- `VisualArtifactKind`
- export capabilities
- table artifacts
- topology artifacts
- flow/Mermaid artifacts
- sequence/timeline artifacts
- SVG/PNG/static renderers
- product-neutral interaction metadata
- optional host adapters later, such as `ChartForgeX.WinUI`

IX must not own Mermaid parsing, topology parsing, reusable table artifact models, or reusable visual export behavior.

### IntelligenceX Owns The Host

IX owns:

- service process and named-pipe lifecycle
- chat message view models
- streaming/provisional/final turn state
- native shell layout
- result card chrome
- command routing
- profile/runtime/settings UI
- packaging

IX consumes OfficeIMO Markdown projections and ChartForgeX visual artifacts. It should not parse Markdown fences itself except for temporary diagnostic experiments that do not ship.

## Shared Contract

The missing piece was not another parser. It was a small bridge API between OfficeIMO semantic Markdown blocks and ChartForgeX artifacts.

Recommended CFX input shape:

```csharp
public sealed class VisualMarkupBlock {
    public VisualMarkupKind Kind { get; }
    public string FenceName { get; }
    public string FenceInfo { get; }
    public int SchemaVersion { get; }
    public string Payload { get; }
    public int FenceLine { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
}
```

CFX now exposes the important host-input API:

```csharp
public VisualMarkupParseResult ParseBlocks(IEnumerable<VisualMarkupBlock> blocks);
```

That lets CFX keep `VisualMarkupScanner.Scan(markdown)` for standalone Markdown input, while IX and OfficeIMO-based hosts can pass already-parsed semantic fenced blocks without rescanning Markdown.

## OfficeIMO Work Status

OfficeIMO has added the native-rendering friendly projection in `OfficeIMO.Markdown`:

- `MarkdownNativeDocument.Parse(...)`
- `MarkdownNativeBlock`
- `MarkdownNativeParagraphBlock`
- `MarkdownNativeCodeBlock`
- `MarkdownNativeVisualBlock`
- `MarkdownNativeTableBlock`
- `MarkdownNativeTableCell`
- source spans and structured fence metadata

OfficeIMO also provides the IX-oriented preset surface:

```csharp
IntelligenceXMarkdownRenderer.ParseTranscriptNativeDesktopShell(markdown);
```

Remaining OfficeIMO work:

- publish/release the new package versions
- keep diagnostics and source spans stable as public contract
- add/export examples showing Markdown table and visual fence projection

## ChartForgeX Work Status

ChartForgeX should keep the current standalone visual markup parser, but split scanning from artifact creation:

```text
Markdown text
  -> CFX VisualMarkupScanner
  -> VisualMarkupBlock[]
  -> VisualMarkupArtifactParser
  -> VisualArtifact[]
```

And also allow:

```text
OfficeIMO MarkdownNativeVisualBlock[]
  -> adapter to CFX VisualMarkupBlock[]
  -> VisualMarkupArtifactParser
  -> VisualArtifact[]
```

The CFX side should add or confirm:

- `ParseBlocks(IEnumerable<VisualMarkupBlock>)` - present
- table artifact static preview and native interaction metadata
- Mermaid flowchart/sequence/pie artifact conversion with diagnostics
- artifact source metadata: fence, source line, payload range, language
- non-HTML SVG/PNG export for artifact previews
- host-neutral table query/capability model

For interactive tables, CFX should describe capabilities and data contracts. It should not try to recreate DataTables UI in core.

## IX Native Flow

Target flow:

```text
Chat service message markdown
  -> OfficeIMO native transcript parse
  -> MarkdownNativeDocument
  -> text/code/table/visual block view models
  -> MarkdownNativeVisualBlock to ChartForgeX VisualArtifact
  -> WinUI ResultCardHost
```

Package-mode validation:

```powershell
dotnet test .\IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj `
  -c Release `
  --filter NativeRenderingProjectionTests
```

Local source validation remains available when testing unreleased OfficeIMO or ChartForgeX changes:

```powershell
dotnet test .\IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj `
  -c Release `
  --filter NativeRenderingProjectionTests `
  /p:UseLocalNativeMarkdownEngines=true `
  /p:OfficeImoRepoRoot=C:\Support\GitHub\OfficeIMO-markdown-native-projection `
  /p:ChartForgeXRepoRoot=C:\Support\GitHub\ChartForgeX-visual-artifact-table-foundation
```

The full Chat App suite should remain package-mode green. Local source references should be used only to validate upstream changes before their packages are published.

Tool outputs with structured rows should bypass Markdown when possible:

```text
Tool rows
  -> ChartForgeX TableArtifact
  -> WinUI table workspace
  -> OfficeIMO export
```

Markdown tables should go through OfficeIMO:

```text
Markdown table block
  -> MarkdownNativeTableBlock
  -> ChartForgeX TableArtifact
  -> native preview or workspace
```

## Acceptance Criteria

This is good enough to resume IX native rendering only when:

- OfficeIMO exposes AST-backed native block projection with source spans. Done in published packages.
- Visual fences are represented as semantic blocks, not string-scanned by IX. Done in package-mode native projection.
- ChartForgeX accepts already-parsed visual blocks, not only raw Markdown strings. Done in published packages.
- CFX table artifacts expose search/sort/filter/copy/export/virtualization capability metadata. Initial package support is available; WinUI parity proof is still needed.
- CFX renders static SVG/PNG previews without runtime JavaScript. Partly present; still needs native-host proof.
- IX native transcript can project prose, code, tables, and visual blocks from OfficeIMO blocks. Adapter present in package-mode builds.
- IX native result cards can host CFX artifacts without parsing HTML. Not done.
- Unsupported visual syntax produces diagnostics, not silent fallbacks. Adapter produces diagnostics for unsupported visual fences.

## Explicit Non-Goals

- No IX-only Markdown parser.
- No IX-only Mermaid parser.
- No parsing rendered HTML tables back into data.
- No WebView shell as the primary native app.
- No one-off table model that only fits IntelligenceX.
- No ChartForgeX dependency on WinUI/WPF in core.
- No OfficeIMO dependency on ChartForgeX core unless a narrow optional adapter package is intentionally created.
