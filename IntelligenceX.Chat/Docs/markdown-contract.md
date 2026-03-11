# IntelligenceX Markdown Contract

This document describes the intended markdown pipeline for `IntelligenceX.Chat`.

## Goals

- keep one shared transcript markdown contract across render and export flows
- isolate OfficeIMO runtime capability checks from transcript normalization
- keep DOCX-only compatibility behavior explicit instead of mixing it into the general transcript path

## Pipeline

### 1. App history normalization

Raw assistant/user message text is first repaired by `TranscriptMarkdownNormalizer` in the App project.

This stage is for LLM/history artifacts such as:

- malformed strong delimiters
- collapsed list markers
- legacy cached-evidence heading wrappers
- broken parenthetical spacing

This stage is App-owned because it repairs transcript history before render/export formatting.

The App entrypoint for this stage is `TranscriptMarkdownPreparation`, which now also owns:

- persisted transcript repair during conversation load
- streaming preview normalization for in-progress assistant deltas

`TranscriptMarkdownNormalizer` may also delegate to `OfficeImoMarkdownInputNormalizationRuntimeContract` when a compatible `OfficeIMO.Markdown` input normalizer is available at runtime.

### 2. Shared transcript markdown contract

`TranscriptMarkdownContract` in `IntelligenceX.Chat.ExportArtifacts` is the shared normalization layer used by multiple consumers.

It owns:

- shared typography normalization outside fenced code blocks
- adjacent ordered-list spacing repair
- transcript-export cleanup like cached transport marker removal
- DOCX-specific grouped-definition compatibility repair when required

This is the canonical cross-project transcript markdown contract.

### 3. Renderer/runtime contract

`OfficeImoMarkdownRuntimeContract` in the App project owns OfficeIMO renderer capability handling.

It owns:

- transcript renderer option creation
- optional runtime capability probing such as vis-network enablement
- runtime/package diagnostics for loaded OfficeIMO assemblies

### 4. DOCX adaptation

`OfficeImoArtifactWriter` owns DOCX writer concerns only.

`OfficeImoWordMarkdownRuntimeContract` in `ExportArtifacts` owns OfficeIMO Word converter capability probing and baseline transcript converter options.

It should be limited to:

- Word converter options
- image allow-listing and sizing
- converter capability probes
- runtime/package diagnostics for loaded OfficeIMO Word markdown assemblies
- invoking the shared transcript markdown contract for DOCX preparation

It should not become the main place where transcript markdown semantics are decided.

## Merge Criteria For OfficeIMO Packaging

Before merging package-dependent OfficeIMO updates into IntelligenceX:

- render, markdown export, and DOCX export should all use the intended shared contract
- package-mode validation should pass against the exact OfficeIMO versions we plan to publish
- any remaining OfficeIMO-specific behavior should be documented and intentional
- renderer/runtime capability probing should stay isolated from transcript content normalization
