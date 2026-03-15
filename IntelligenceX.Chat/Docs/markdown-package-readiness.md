# IntelligenceX OfficeIMO Markdown Adoption Checklist

This document is the contributor checklist for adopting a new `OfficeIMO.Markdown` / `OfficeIMO.MarkdownRenderer` package line in `IntelligenceX.Chat`.

## Current Baseline

The currently pinned package versions in [Directory.Build.props](../Directory.Build.props) are:

- `OfficeIMO.Markdown` = `0.6.0`
- `OfficeIMO.MarkdownRenderer` = `0.2.0`
- `OfficeIMO.Word.Markdown` = `1.0.7`
- `OfficeIMO.Excel` = `0.6.13`

Those pins represent the last published package adoption baseline. The explicit `IntelligenceXTranscript` OfficeIMO API line should be developed and validated against the local OfficeIMO checkout until the next matching package versions are published and the pins are updated in the same adoption PR.

When a sibling OfficeIMO checkout is present, `IntelligenceX.Chat` now prefers it by default. Force package mode explicitly with `/p:UseLocalOfficeImoCheckout=false` when validating the current published package line.

## What Is Already Cleaned Up

- App transcript normalization entrypoints are centralized in [TranscriptMarkdownPreparation.cs](../IntelligenceX.Chat.App/Markdown/TranscriptMarkdownPreparation.cs).
- Shared transcript export and DOCX preparation is centralized in [TranscriptMarkdownContract.cs](../IntelligenceX.Chat.ExportArtifacts/TranscriptMarkdownContract.cs), which now composes the explicit OfficeIMO.Markdown helpers in `MarkdownTranscriptPreparation` and `MarkdownTranscriptTransportMarkers`.
- OfficeIMO renderer/runtime probing is centralized in [OfficeImoMarkdownRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownRuntimeContract.cs).
- OfficeIMO Word transcript preset/capability invocation is centralized in [OfficeImoWordMarkdownRuntimeContract.cs](../IntelligenceX.Chat.ExportArtifacts/OfficeImoWordMarkdownRuntimeContract.cs).
- OfficeIMO input normalization is centralized in [OfficeImoMarkdownInputNormalizationRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownInputNormalizationRuntimeContract.cs).
- When a sibling OfficeIMO checkout is present, those seams now compile against OfficeIMO directly instead of using reflection. The compatibility fallback remains only for explicit package-mode validation until the new package line is published.
- OfficeIMO now owns the generic post-parse markdown document-transform pipeline; IX should continue to consume that only via explicit OfficeIMO contracts/presets.

## Adoption Gate

Before updating the pinned package versions, all of the following should be true:

- App render, markdown export, and DOCX export still go through the intended shared markdown contract.
- Package-mode validation passes against the exact versions selected for adoption.
- The new package-adoption PR does not reintroduce mixed ownership between transcript cleanup, renderer probing, and DOCX adaptation.
- Any intentional OfficeIMO-specific behavior used by `IntelligenceX` is documented in [markdown-contract.md](markdown-contract.md).
- If the adoption moves IX to newer explicit OfficeIMO transcript APIs, including `OfficeIMO.Word.Markdown` transcript presets/capabilities, those package versions must be published first and the pinned package line updated in the same PR.
- The package-adoption PR should delete the three temporary reflection/runtime bridges instead of carrying them forward.

## Validation Expectations

The package line should be validated in package mode, not only against a local OfficeIMO checkout.

The final adoption PR should also be checked on the three user-visible paths:

- UI render
- markdown export
- DOCX export

## Adoption Sequence

Use this sequence for the actual package update:

1. publish the intended `OfficeIMO.Markdown` and `OfficeIMO.MarkdownRenderer` package versions
2. update [Directory.Build.props](../Directory.Build.props) to those exact versions
3. delete the three temporary OfficeIMO compatibility bridges and switch existing seams to direct OfficeIMO APIs
4. run package-mode validation against the published package line
5. re-check UI render, markdown export, and DOCX export
6. merge only after the package-mode gate and those user-visible checks are green

## Notes

This page is intentionally checklist-oriented. Implementation details for the current markdown pipeline live in [markdown-contract.md](markdown-contract.md).
