# IntelligenceX OfficeIMO Markdown Adoption Checklist

This document is the contributor checklist for adopting a new `OfficeIMO.Markdown` / `OfficeIMO.MarkdownRenderer` package line in `IntelligenceX.Chat`.

## Current Baseline

The currently pinned package versions in [Directory.Build.props](../Directory.Build.props) are:

- `OfficeIMO.Markdown` = `0.6.0`
- `OfficeIMO.MarkdownRenderer` = `0.2.0`
- `OfficeIMO.Word.Markdown` = `1.0.7`
- `OfficeIMO.Excel` = `0.6.13`

## What Is Already Cleaned Up

- App transcript normalization entrypoints are centralized in [TranscriptMarkdownPreparation.cs](../IntelligenceX.Chat.App/Markdown/TranscriptMarkdownPreparation.cs).
- Shared transcript content normalization is centralized in [TranscriptMarkdownContract.cs](../IntelligenceX.Chat.ExportArtifacts/TranscriptMarkdownContract.cs).
- OfficeIMO renderer/runtime probing is centralized in [OfficeImoMarkdownRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownRuntimeContract.cs).
- OfficeIMO Word converter/runtime probing is centralized in [OfficeImoWordMarkdownRuntimeContract.cs](../IntelligenceX.Chat.ExportArtifacts/OfficeImoWordMarkdownRuntimeContract.cs).
- OfficeIMO input-normalizer probing is isolated in [OfficeImoMarkdownInputNormalizationRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownInputNormalizationRuntimeContract.cs).

## Adoption Gate

Before updating the pinned package versions, all of the following should be true:

- App render, markdown export, and DOCX export still go through the intended shared markdown contract.
- Package-mode validation passes against the exact versions selected for adoption.
- The new package-adoption PR does not reintroduce mixed ownership between transcript cleanup, renderer probing, and DOCX adaptation.
- Any intentional OfficeIMO-specific behavior used by `IntelligenceX` is documented in [markdown-contract.md](markdown-contract.md).

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
3. run package-mode validation against the published package line
4. re-check UI render, markdown export, and DOCX export
5. merge only after the package-mode gate and those user-visible checks are green

## Notes

This page is intentionally checklist-oriented. Implementation details for the current markdown pipeline live in [markdown-contract.md](markdown-contract.md).
