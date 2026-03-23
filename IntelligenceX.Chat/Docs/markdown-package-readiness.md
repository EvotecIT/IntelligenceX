# IntelligenceX OfficeIMO Markdown Adoption Checklist

This document is the contributor checklist for adopting a new `OfficeIMO.Markdown` / `OfficeIMO.MarkdownRenderer` package line in `IntelligenceX.Chat`.

## Current Baseline

The currently pinned package versions in [Directory.Build.props](../Directory.Build.props) are:

- `OfficeIMO.Markdown` = `0.6.2`
- `OfficeIMO.MarkdownRenderer` = `0.2.2`
- `OfficeIMO.Word.Markdown` = `1.0.9`
- `OfficeIMO.Excel` = `0.6.15`

Those pins represent the last published package adoption baseline. The explicit `IntelligenceXTranscript` OfficeIMO API line should be developed and validated against the local OfficeIMO checkout until the next matching package versions are published and the pins are updated in the same adoption PR.

When a sibling OfficeIMO checkout is present, `IntelligenceX.Chat` now prefers it by default. This end-state branch intentionally targets the new unpublished OfficeIMO API line directly, so package mode is expected to fail until those package versions are published.

## What Is Already Cleaned Up

- App transcript normalization entrypoints are centralized in [TranscriptMarkdownPreparation.cs](../IntelligenceX.Chat.App/Markdown/TranscriptMarkdownPreparation.cs).
- Shared transcript export and DOCX preparation is centralized in [TranscriptMarkdownContract.cs](../IntelligenceX.Chat.ExportArtifacts/TranscriptMarkdownContract.cs), which now composes the explicit OfficeIMO.Markdown helpers in `MarkdownTranscriptPreparation` and `MarkdownTranscriptTransportMarkers`.
- App transcript rendering now calls `MarkdownRendererPresets.CreateIntelligenceXTranscriptDesktopShell()` directly.
- App transcript preprocessing now calls `MarkdownRendererPreProcessorPipeline.Apply(...)` and `MarkdownInputNormalizer.Normalize(...)` directly.
- DOCX export now calls `MarkdownToWordPresets.CreateIntelligenceXTranscript(...)` and `MarkdownToWordCapabilities.PreservesNarrativeSingleLineDefinitionsAsSeparateParagraphs()` directly.
- Runtime assembly diagnostics are centralized in [OfficeImoAssemblyContractDiagnostics.cs](../IntelligenceX.Chat.App/OfficeImoAssemblyContractDiagnostics.cs).
- OfficeIMO now owns the generic post-parse markdown document-transform pipeline; IX should continue to consume that only via explicit OfficeIMO contracts/presets.
- Markdown artifact export now has an explicit portable lane that prefers generic semantic visual fences (`chart`, `network`, `dataview`), while runtime/chat and DOCX lanes intentionally remain IX-alias-first for compatibility.

## Adoption Gate

Before updating the pinned package versions, all of the following should be true:

- App render, markdown export, and DOCX export still go through the intended shared markdown contract.
- Package-mode validation passes against the exact versions selected for adoption after those versions are published.
- The new package-adoption PR does not reintroduce mixed ownership between transcript cleanup, renderer probing, and DOCX adaptation.
- Any intentional OfficeIMO-specific behavior used by `IntelligenceX` is documented in [markdown-contract.md](markdown-contract.md).
- If the adoption moves IX to newer explicit OfficeIMO transcript APIs, including `OfficeIMO.Word.Markdown` transcript presets/capabilities, those package versions must be published first and the pinned package line updated in the same PR.
- The package-adoption PR should only need to bump the pinned package versions, because this branch already deletes the temporary reflection/runtime bridges.

## Validation Expectations

Before publish, validate locally against the sibling OfficeIMO checkout. After publish, validate again in package mode against the exact package line.

The final adoption PR should also be checked on the three user-visible paths:

- UI render
- markdown export
- DOCX export

Make sure those checks preserve the intended split:

- UI render and runtime preprocessing should still validate the explicit IX alias contract
- markdown export should validate the portable generic fence contract
- DOCX export should validate the IX-compatible transcript-to-word contract

For corpus-based validation, prefer:

- `ix-compat-transcript-*` fixtures for IX runtime/render compatibility checks
- source-derived legacy JSON visual fixtures for portable markdown export checks

## Adoption Sequence

Use this sequence for the actual package update:

1. publish the intended `OfficeIMO.Markdown` and `OfficeIMO.MarkdownRenderer` package versions
2. update [Directory.Build.props](../Directory.Build.props) to those exact versions
3. run package-mode validation against the published package line
4. re-check UI render, markdown export, and DOCX export
5. merge only after the package-mode gate and those user-visible checks are green

## Notes

This page is intentionally checklist-oriented. Implementation details for the current markdown pipeline live in [markdown-contract.md](markdown-contract.md).
