# IntelligenceX OfficeIMO Markdown Package Readiness

This document tracks whether `IntelligenceX.Chat` is ready to adopt and freeze a new `OfficeIMO.Markdown` / `OfficeIMO.MarkdownRenderer` package line.

## Current Baseline

The currently pinned package versions in [Directory.Build.props](../Directory.Build.props) are:

- `OfficeIMO.Markdown` = `0.5.12`
- `OfficeIMO.MarkdownRenderer` = `0.1.9`
- `OfficeIMO.Word.Markdown` = `1.0.6`
- `OfficeIMO.Excel` = `0.6.12`

## What Is Already Cleaned Up

- App transcript normalization entrypoints are centralized in [TranscriptMarkdownPreparation.cs](../IntelligenceX.Chat.App/Markdown/TranscriptMarkdownPreparation.cs).
- Shared transcript content normalization is centralized in [TranscriptMarkdownContract.cs](../IntelligenceX.Chat.ExportArtifacts/TranscriptMarkdownContract.cs).
- OfficeIMO renderer/runtime probing is centralized in [OfficeImoMarkdownRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownRuntimeContract.cs).
- OfficeIMO Word converter/runtime probing is centralized in [OfficeImoWordMarkdownRuntimeContract.cs](../IntelligenceX.Chat.ExportArtifacts/OfficeImoWordMarkdownRuntimeContract.cs).
- OfficeIMO input-normalizer probing is isolated in [OfficeImoMarkdownInputNormalizationRuntimeContract.cs](../IntelligenceX.Chat.App/OfficeImoMarkdownInputNormalizationRuntimeContract.cs).

## Package-Mode Validation

Validated on March 11, 2026 with `UseLocalOfficeImoCheckout=false`.

Successful commands:

```powershell
dotnet restore IntelligenceX.Chat/IntelligenceX.Chat.ExportArtifacts/IntelligenceX.Chat.ExportArtifacts.csproj -p:UseLocalOfficeImoCheckout=false
dotnet restore IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/IntelligenceX.Chat.App.Tests.csproj -p:UseLocalOfficeImoCheckout=false
dotnet build IntelligenceX.Chat/IntelligenceX.Chat.ExportArtifacts/IntelligenceX.Chat.ExportArtifacts.csproj -p:UseLocalOfficeImoCheckout=false
dotnet test IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/IntelligenceX.Chat.App.Tests.csproj -p:UseLocalOfficeImoCheckout=false --filter "FullyQualifiedName~TranscriptMarkdownContractTests|FullyQualifiedName~TranscriptMarkdownContractIntegrationTests|FullyQualifiedName~OfficeImoMarkdownRuntimeContractTests|FullyQualifiedName~OfficeImoMarkdownInputNormalizationRuntimeContractTests|FullyQualifiedName~LocalExportArtifactWriterTests"
```

Observed result:

- `IntelligenceX.Chat.ExportArtifacts` built successfully in package mode.
- The focused package-mode markdown/export contract suite passed: `40` passed, `0` failed.

## Merge / Publish Gate

Before publishing and then merging a package-adoption PR, all of the following should stay true:

- App render, markdown export, and DOCX export still go through the intended shared markdown contract.
- Package-mode validation passes against the exact versions we plan to publish.
- The new package-adoption PR does not reintroduce mixed ownership between transcript cleanup, renderer probing, and DOCX adaptation.
- Any intentional OfficeIMO-specific behavior used by `IntelligenceX` is documented in [markdown-contract.md](markdown-contract.md).

## What Still Needs To Happen

- Publish the intended `OfficeIMO.Markdown` and `OfficeIMO.MarkdownRenderer` package versions.
- Open a fresh package-adoption PR from the cleaned baseline instead of reviving old pre-cleanup integration branches.
- Re-run the package-mode validation against the exact published versions chosen for adoption.
- Verify the final adoption PR on the three user-visible paths:
  - UI render
  - markdown export
  - DOCX export

## Current Recommendation

Do not merge a package-adoption PR yet.

The cleanup work is now in good shape and package-mode validation is green, but the safer sequence is:

1. publish the intended OfficeIMO markdown package line
2. open a fresh adoption PR against those exact versions
3. rerun package-mode validation and UI/export checks
4. merge the adoption PR only after that final confirmation
