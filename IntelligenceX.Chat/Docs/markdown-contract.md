# IntelligenceX Markdown Contract

This document describes the intended markdown pipeline for `IntelligenceX.Chat`.

## Goals

- keep one explicit OfficeIMO-owned transcript markdown contract across render, export, and DOCX flows
- isolate OfficeIMO host/runtime capability checks from transcript normalization
- keep DOCX-only compatibility behavior explicit instead of mixing it into the general transcript path

## Pipeline

### 1. App history normalization

Raw assistant/user message text first enters the App normalization entrypoints in `TranscriptMarkdownPreparation`.

The App-owned part of this stage is for host-level transcript shaping such as:

- malformed strong delimiters
- collapsed list markers
- broken parenthetical spacing

The explicit IX transcript markdown contract now lives in OfficeIMO presets and renderer preprocessors. That OfficeIMO-owned layer covers IX transcript semantics such as:

- legacy tool heading/slug cleanup
- standalone separator cleanup before headings
- legacy IX visual fence upgrades
- post-parse document transforms such as semantic visual code-block upgrades

The App still owns when these entrypoints run during transcript load, preview, render, and export preparation.

The App entrypoint for this stage is `TranscriptMarkdownPreparation`, which now also owns:

- persisted transcript repair during conversation load
- streaming preview normalization for in-progress assistant deltas

Streaming preview now delegates conservative delta cleanup through the explicit OfficeIMO utility:

- `MarkdownStreamingPreviewNormalizer.NormalizeIntelligenceXTranscript(...)`

`TranscriptMarkdownNormalizer` now delegates explicit transcript-contract cleanup through the OfficeIMO runtime seams:

- `OfficeImoMarkdownInputNormalizationRuntimeContract`, which calls the explicit `OfficeIMO.Markdown` `IntelligenceXTranscript` normalization preset directly
- `OfficeImoMarkdownRuntimeContract`, which now calls `OfficeIMO.MarkdownRenderer.MarkdownRendererPreProcessorPipeline.Apply(...)` with the explicit `CreateIntelligenceXTranscriptMinimal` preprocessor chain

For this PR line, IX should run against the sibling OfficeIMO checkout by default whenever it exists. Package mode remains an explicit validation path, not the default development mode, until the new OfficeIMO package line is published.

OfficeIMO now also exposes a generic post-parse document-transform pipeline. IX should treat that as OfficeIMO implementation detail and consume it only through explicit OfficeIMO presets/contracts instead of composing transcript-specific transforms in App code.

### 2. Shared export and DOCX contract

`TranscriptMarkdownContract` in `IntelligenceX.Chat.ExportArtifacts` is the shared export/DOCX orchestration layer used by multiple consumers.

It owns:

- invoking the explicit `OfficeIMO.Markdown` transcript-preparation helpers in `MarkdownTranscriptPreparation`
- transcript-export cleanup like cached transport marker removal, now delegated to the explicit `OfficeIMO.Markdown` helper `MarkdownTranscriptTransportMarkers`
- DOCX-specific grouped-definition compatibility repair when required, delegated to the shared `OfficeIMO.Markdown` helper `MarkdownDefinitionLines`

This keeps `ExportArtifacts` focused on orchestration while OfficeIMO remains the canonical markdown-shaping contract.

### 3. Renderer/runtime contract

`OfficeImoMarkdownRuntimeContract` in the App project owns OfficeIMO renderer capability handling.

It owns:

- transcript renderer option creation through the explicit OfficeIMO desktop-shell preset (`CreateIntelligenceXTranscriptDesktopShell`)
- runtime/package diagnostics for loaded OfficeIMO assemblies

Until the new OfficeIMO package line is published, the runtime contract classes remain temporary compatibility shims around newer explicit OfficeIMO APIs. They are not the architectural owner of markdown semantics.

### 4. DOCX adaptation

`OfficeImoArtifactWriter` owns DOCX writer concerns only.

`OfficeImoWordMarkdownRuntimeContract` in `ExportArtifacts` now delegates to explicit OfficeIMO.Word.Markdown contracts for transcript DOCX conversion:

- `MarkdownToWordPresets.CreateIntelligenceXTranscript(...)`
- `MarkdownToWordCapabilities.PreservesNarrativeSingleLineDefinitionsAsSeparateParagraphs()`

It should be limited to:

- invoking OfficeIMO Word-markdown transcript presets
- image allow-listing and sizing
- converter capability probes exposed by OfficeIMO
- runtime/package diagnostics for loaded OfficeIMO Word markdown assemblies
- invoking the shared transcript markdown contract for DOCX preparation

It should not become the main place where transcript markdown semantics are decided.

After the OfficeIMO package bump, IX should delete:

- `OfficeImoMarkdownRuntimeContract`
- `OfficeImoMarkdownInputNormalizationRuntimeContract`
- `OfficeImoWordMarkdownRuntimeContract`

and switch the existing orchestration seams to direct OfficeIMO API calls.

The OfficeIMO markdown runtime diagnostics are currently surfaced through:

- startup renderer diagnostics in the App host
- transcript forensic export snapshots

## OfficeIMO Package Adoption Rules

Before adopting a new `OfficeIMO.Markdown` / `OfficeIMO.MarkdownRenderer` package line in `IntelligenceX`:

- render, markdown export, and DOCX export should all use the intended explicit OfficeIMO transcript contract with IX only orchestrating host-specific entrypoints
- package-mode validation should pass against the exact package versions chosen for adoption
- any remaining OfficeIMO-specific behavior should be documented and intentional
- host/runtime capability checks should stay isolated from transcript content normalization

The detailed adoption checklist lives in [markdown-package-readiness.md](markdown-package-readiness.md).
