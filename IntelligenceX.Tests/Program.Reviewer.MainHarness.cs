namespace IntelligenceX.Tests;

internal static partial class Program {
    private static int RunReviewerTests() {
#if INTELLIGENCEX_REVIEWER
        var failed = 0;
        failed += Run("Cleanup normalize allowed edits", TestCleanupNormalizeAllowedEdits);
        failed += Run("Cleanup clamp confidence", TestCleanupClampConfidence);
        failed += Run("Cleanup result parse fenced", TestCleanupResultParseFenced);
        failed += Run("Cleanup result parse embedded", TestCleanupResultParseEmbedded);
        failed += Run("Cleanup template path guard", TestCleanupTemplatePathGuard);
        failed += Run("Inline comments extract", TestInlineCommentsExtract);
        failed += Run("Inline comments backticks", TestInlineCommentsBackticks);
        failed += Run("Inline comments snippet header", TestInlineCommentsSnippetHeader);
        failed += Run("Review thread inline key", TestReviewThreadInlineKey);
        failed += Run("Review thread inline key bots only", TestReviewThreadInlineKeyBotsOnly);
        failed += Run("GitHub event fork parsing", TestGitHubEventForkParsing);
        failed += Run("GitHub event missing head repo fails closed", TestGitHubEventMissingHeadRepoFailsClosed);
        failed += Run("Owned summary comment requires trusted author", TestOwnedSummaryCommentRequiresTrustedAuthor);
        failed += Run("Thread assessment evidence parse", TestThreadAssessmentEvidenceParse);
        failed += Run("Thread triage fallback summary", TestThreadTriageFallbackSummary);
        failed += Run("Thread assessment candidates skip static analysis inline threads",
            TestThreadAssessmentCandidatesSkipStaticAnalysisInlineThreads);
        failed += Run("Thread assessment uses original inline comment for static analysis classification",
            TestThreadAssessmentCandidatesUseOriginalInlineCommentForStaticAnalysisClassification);
        failed += Run("Thread assessment legacy static analysis requires trusted author",
            TestThreadAssessmentCandidatesLegacyStaticAnalysisRequiresTrustedAuthor);
        failed += Run("Thread assessment legacy static analysis uses shared trusted author detection",
            TestThreadAssessmentCandidatesLegacyStaticAnalysisUsesSharedTrustedAuthorDetection);
        failed += Run("Reply-to-kept-threads skips static analysis inline threads",
            TestReplyToKeptThreadsSkipsStaticAnalysisInlineThreads);
        failed += Run("Static analysis inline signature stays stable with analysis marker",
            TestStaticAnalysisInlineSignatureStaysStableWithAnalysisMarker);
        failed += Run("Review thread inline key allowlist", TestReviewThreadInlineKeyAllowlist);
        failed += Run("Review thread inline key codex connector default", TestReviewThreadInlineKeyCodexConnectorDefault);
        failed += Run("Thread auto-resolve summary comment", TestThreadAutoResolveSummaryComment);
        failed += Run("Thread resolve evidence cross-file fallback", TestThreadResolveEvidenceCrossFileFallback);
        failed += Run("Thread resolve evidence prefers thread context", TestThreadResolveEvidenceUsesThreadContextWhenAvailable);
        failed += Run("Thread resolve evidence cross-file fallback stale-only", TestThreadResolveEvidenceCrossFileFallbackOnlyForStaleThreads);
        failed += Run("Thread resolve evidence normalize single wrapper", TestThreadResolveEvidenceNormalizeSingleWrapperOnly);
        failed += Run("Thread resolve evidence normalize preserves unbalanced delimiters", TestThreadResolveEvidenceNormalizePreservesUnbalancedDelimiters);
        failed += Run("Thread resolve evidence deduplicates patch path scans", TestThreadResolveEvidenceDeduplicatesPatchPathScans);
        failed += Run("Thread triage embed placement", TestThreadTriageEmbedPlacement);
        failed += Run("Thread assessment prompt smoke", TestThreadAssessmentPromptSmoke);
        failed += Run("Auto-resolve stale threads smoke", TestAutoResolveStaleThreadsSmoke);
        failed += Run("Auto-resolve stale threads fallback on insufficient scopes",
            TestAutoResolveStaleThreadsFallbackOnInsufficientScopes);
        failed += Run("Auto-resolve stale threads treats already-resolved as success",
            TestAutoResolveStaleThreadsTreatsAlreadyResolvedAsSuccess);
        failed += Run("Auto-resolve missing inline empty keys", TestAutoResolveMissingInlineEmptyKeys);
        failed += Run("Auto-resolve missing inline bots-only skips hydrated non-bot thread",
            TestAutoResolveMissingInlineBotsOnlySkipsHydratedNonBotThread);
        failed += Run("Auto-resolve missing inline shifted line window", TestAutoResolveMissingInlineSkipsShiftedLineWithinWindow);
        failed += Run("Auto-resolve missing inline signature match", TestAutoResolveMissingInlineSkipsSignatureMatchForRewordedBody);
        failed += Run("No-blockers sweep respects resolve budget", TestNoBlockersSweepRespectsResolveBudget);
        failed += Run("Resolve thread payload parser rejects invalid JSON", TestResolveThreadPayloadParserRejectsInvalidJson);
        failed += Run("Thread resolve integration forbidden detection", TestThreadResolveIntegrationForbiddenDetection);
        failed += Run("Thread resolve error formatting includes fallback", TestThreadResolveErrorFormattingIncludesFallback);
        failed += Run("Auto-resolve permission note mentions workflow permissions",
            TestAutoResolvePermissionNoteMentionsWorkflowPermissions);
        failed += Run("Conversation resolution permission blocker section",
            TestConversationResolutionPermissionBlockerSection);
        failed += Run("Auto-resolve missing inline gate empty set", TestAutoResolveMissingInlineGateAllowsEmptySet);
        failed += Run("Auto-resolve missing inline gate null set", TestAutoResolveMissingInlineGateRejectsNull);
        failed += Run("Auto-resolve missing inline gate empty mapped keys", TestAutoResolveMissingInlineGateRejectsEmptyWhenInlineCommentsPresent);
        failed += Run("Review retry transient", TestReviewRetryTransient);
        failed += Run("Review retry non-transient", TestReviewRetryNonTransient);
        failed += Run("Review retry rethrows", TestReviewRetryRethrows);
        failed += Run("Review retry extra attempt", TestReviewRetryExtraAttempt);
        failed += Run("Review failure marker", TestReviewFailureMarker);
        failed += Run("Review failure body redacts errors", TestReviewFailureBodyRedactsErrors);
        failed += Run("Review failure body uses Copilot transport", TestReviewFailureBodyUsesCopilotTransport);
        failed += Run("Review failure body uses provider-specific transport labels",
            TestReviewFailureBodyUsesProviderSpecificTransportLabels);
        failed += Run("Review failure body classifies Copilot unauthorized", TestReviewFailureBodyClassifiesCopilotUnauthorized);
        failed += Run("Review failure body prefers timeout over inner cancellation",
            TestReviewFailureBodyPrefersTimeoutOverInnerCancellation);
        failed += Run("Review failure body includes safe auth refresh detail", TestReviewFailureBodyIncludesSafeAuthRefreshDetail);
        failed += Run("Build auth remediation command quotes repo when needed", TestBuildAuthRemediationCommandQuotesRepoWhenNeeded);
        failed += Run("Build auth remediation command escapes embedded quotes", TestBuildAuthRemediationCommandEscapesEmbeddedQuotes);
        failed += Run("Workflow fail-open log classification uses auth refresh label", TestWorkflowFailOpenLogClassificationUsesAuthRefreshLabel);
        failed += Run("Workflow fail-open log classification prefers usage budget guard", TestWorkflowFailOpenLogClassificationPrefersUsageBudgetGuard);
        failed += Run("Workflow fail-open summary body uses runtime guidance", TestWorkflowFailOpenSummaryBodyUsesRuntimeGuidance);
        failed += Run("Failure summary comment update", TestFailureSummaryCommentUpdate);
        failed += Run("Review fail-open only transient", TestReviewFailOpenTransientOnly);
        failed += Run("Review fail-open decision", TestReviewFailOpenDecision);
        failed += Run("Preflight timeout", TestPreflightTimeout);
        failed += Run("Preflight socket failure", TestPreflightSocketFailure);
        failed += Run("Preflight auth statuses are reachable", TestPreflightAuthStatusesAreReachable);
        failed += Run("Preflight non-2xx", TestPreflightNonSuccessStatus);
        failed += Run("Preflight DNS failure mapping", TestPreflightDnsFailureMapping);
        failed += Run("Preflight socket failure mapping", TestPreflightSocketFailureMapping);
        failed += Run("Preflight HTTP status mapping bypass", TestPreflightHttpStatusMappingBypass);
        failed += Run("Preflight cancellation mapping bypass", TestPreflightCancellationRequestedMappingBypass);
        failed += Run("Review config validator allows additional", TestReviewConfigValidatorAllowsAdditionalProperties);
        failed += Run("Review config validator invalid enum", TestReviewConfigValidatorInvalidEnum);
        failed += Run("Analysis severity critical", TestAnalysisSeverityCritical);
        failed += Run("Analysis config export tool ids", TestAnalysisConfigExportToolIds);
        failed += Run("Analysis catalog rule docs path", TestAnalysisCatalogRuleDocsPath);
        failed += Run("Analysis policy resolves overrides", TestAnalysisPolicyResolvesOverrides);
        failed += Run("Analysis policy resolves included packs", TestAnalysisPolicyResolvesIncludedPacks);
        failed += Run("Analysis policy included pack cycle warning", TestAnalysisPolicyIncludedPackCycleWarning);
        failed += Run("Analysis policy disable tool rule id", TestAnalysisPolicyDisableToolRuleId);
        failed += Run("Analysis catalog validator passes built-in catalog", TestAnalysisCatalogValidatorPassesBuiltInCatalog);
        failed += Run("Analysis catalog loader root check rejects sibling prefix path",
            TestAnalysisCatalogLoaderUnderRootRejectsSiblingPrefixPath);
        failed += Run("Analysis catalog loader root check follows platform case sensitivity",
            TestAnalysisCatalogLoaderUnderRootCaseSensitivityByPlatform);
        failed += Run("Analysis catalog loader root check accepts mixed separators",
            TestAnalysisCatalogLoaderUnderRootAcceptsMixedSeparators);
        failed += Run("Analysis catalog loader trim preserves filesystem root",
            TestAnalysisCatalogLoaderTrimPreservesFilesystemRoot);
        failed += Run("Analysis catalog validator detects invalid catalog", TestAnalysisCatalogValidatorDetectsInvalidCatalog);
        failed += Run("Analysis catalog validator detects missing rule metadata", TestAnalysisCatalogValidatorDetectsMissingRuleMetadata);
        failed += Run("Analysis packs: all-security includes language security packs", TestAnalysisPacksAllSecurityIncludesPowerShell);
        failed += Run("Analysis packs: all-security tiers resolve", TestAnalysisPacksAllSecurityTiersResolve);
        failed += Run("Analysis packs: all-multilang tiers resolve", TestAnalysisPacksAllMultilangTiersResolve);
        failed += Run("Analysis packs: powershell-default resolves", TestAnalysisPacksPowerShellDefaultResolves);
        failed += Run("Analysis packs: external defaults resolve", TestAnalysisPacksExternalDefaultsResolve);
        failed += Run("Analysis packs: external language tiers resolve", TestAnalysisPacksExternalLanguageTiersResolve);
        failed += Run("Analysis packs: powershell-50 resolves to 50 rules", TestAnalysisPacksPowerShell50ResolvesTo50Rules);
        failed += Run("Analysis catalog rule overrides apply", TestAnalysisCatalogRuleOverridesApply);
        failed += Run("Analysis catalog PowerShell overrides apply", () => TestAnalysisCatalogPowerShellOverridesApply());
        failed += Run("Analysis catalog PowerShell docs links", TestAnalysisCatalogPowerShellDocsLinksMatchLearnPattern);
        failed += Run("Static analysis docs include-ext defaults stay in sync",
            TestStaticAnalysisDocsIncludeExtDefaultsStayInSync);
        failed += Run("Static analysis docs duplication aliases stay in sync",
            TestStaticAnalysisDocsDuplicationLanguageAliasesStayInSync);
        failed += Run("PowerShell docs snippets use exported cmdlets", TestPowerShellDocsSnippetsUseExportedCmdlets);
        failed += Run("PowerShell example scripts use exported cmdlets", TestPowerShellExampleScriptsUseExportedCmdlets);
        failed += Run("PowerShell cmdlet source XML docs are rich", TestPowerShellCmdletSourceXmlDocsAreRich);
        failed += Run("PowerShell help XML covers all cmdlets with rich docs", TestPowerShellHelpXmlCoversAllCmdletsWithRichDocs);
        failed += Run("OpenAI client C# XML docs are complete", TestOpenAiClientCSharpXmlDocsAreComplete);
        failed += Run("Analysis catalog override invalid type falls back", TestAnalysisCatalogOverrideInvalidTypeFallsBack);
        failed += Run("Analysis catalog validator rejects dangling override", TestAnalysisCatalogValidatorRejectsDanglingOverride);
        failed += Run("Analysis catalog built-in rules have type classification", TestAnalysisCatalogBuiltInRulesHaveTypes);
        failed += Run("Analysis hotspots render and state snippet", TestAnalysisHotspotsRenderAndStateSnippet);
        failed += Run("Hotspots redact absolute state path", TestAnalysisHotspotsRedactsAbsoluteStatePath);
        failed += Run("Hotspots reviewer state path is workspace-bound", TestAnalysisHotspotsReviewerStatePathIsWorkspaceBound);
        failed += Run("Hotspots maxItems semantics", TestAnalysisHotspotsMaxItemsSemantics);
        failed += Run("Hotspots suppressed count semantics", TestAnalysisHotspotsSuppressedCountSemantics);
        failed += Run("Hotspots key hashing uses UTF-8 bytes", TestAnalysisHotspotsKeyHashingUsesUtf8Bytes);
        failed += Run("Hotspots output escapes markdown injection", TestAnalysisHotspotsOutputEscapesMarkdownInjection);
        failed += Run("Analysis loader includes hotspots below minSeverity", TestAnalysisLoaderIncludesHotspotsBelowMinSeverity);
        failed += Run("Analysis loader ignores inputs outside workspace", TestAnalysisFindingsLoaderIgnoresInputsOutsideWorkspace);
        failed += Run("Analysis loader rejects sibling prefix paths", TestAnalysisFindingsLoaderWorkspaceBoundRejectsSiblingPrefix);
        failed += Run("Analysis loader does not relativize sibling-prefix absolute path",
            TestAnalysisFindingsLoaderDoesNotRelativizeSiblingPrefixAbsoluteFindingPath);
        failed += Run("Analysis loader normalize path follows platform case semantics",
            TestAnalysisFindingsLoaderNormalizePathCaseSensitivityByPlatform);
        failed += Run("Analysis loader normalize path accepts mixed separators",
            TestAnalysisFindingsLoaderNormalizePathAcceptsMixedSeparatorsWithinWorkspace);
        failed += Run("Analyze hotspots sync-state writes state file", TestAnalyzeHotspotsSyncStateWritesStateFile);
        failed += Run("Analyze hotspots help has no side effects", TestAnalyzeHotspotsHelpHasNoSideEffects);
        failed += Run("Analyze hotspots state path is workspace-bound", TestAnalyzeHotspotsStatePathIsWorkspaceBound);
        failed += Run("Analyze validate-catalog command", TestAnalyzeValidateCatalogCommand);
        failed += Run("Analyze list-packs --ids", TestAnalyzeListPacksIds);
        failed += Run("Analyze list-packs help", TestAnalyzeListPacksHelp);
        failed += Run("Analyze list-rules markdown format", TestAnalyzeListRulesMarkdownFormat);
        failed += Run("Analyze list-rules json with pack filter", TestAnalyzeListRulesJsonWithPackFilter);
        failed += Run("Analyze list-rules tier counts", TestAnalyzeListRulesTierCounts);
        failed += Run("Analyze list-rules security tier counts", TestAnalyzeListRulesSecurityTierCounts);
        failed += Run("Analyze list-rules all-multilang tier counts", TestAnalyzeListRulesAllMultilangTierCounts);
        failed += Run("Analyze list-rules invalid format", TestAnalyzeListRulesInvalidFormat);
        failed += Run("Analyze list-rules help", TestAnalyzeListRulesHelp);
        failed += Run("Analyze list-rules json warnings to stderr", TestAnalyzeListRulesJsonWarningsToStderr);
        failed += Run("Analyze list-rules json empty outputs array", TestAnalyzeListRulesJsonEmptyOutputsArray);
        failed += Run("Analyze gate disabled skips", TestAnalyzeGateDisabledSkips);
        failed += Run("Analyze gate fails on violations", TestAnalyzeGateFailsOnViolations);
        failed += Run("Analyze gate passes on clean", TestAnalyzeGatePassesOnClean);
        failed += Run("Analyze gate fails on no enabled rules", TestAnalyzeGateFailsOnNoEnabledRules);
        failed += Run("Analyze gate minSeverity filters", TestAnalyzeGateMinSeverityFilters);
        failed += Run("Analyze gate ruleIds-only filter narrows scope",
            TestAnalyzeGateRuleIdsFilterCanNarrowScopeWithoutTypes);
        failed += Run("Analyze gate ruleIds include outside-pack findings",
            TestAnalyzeGateRuleIdsFilterIncludesOutsidePackFindings);
        failed += Run("Analyze gate outside-pack summary counts explicit ruleIds with type filter",
            TestAnalyzeGateOutsidePackSummaryCountsRuleIdIncludesWithTypeFilter);
        failed += Run("Analyze gate ruleIds filter adds to type filtering",
            TestAnalyzeGateRuleIdsFilterAddsToTypeFiltering);
        failed += Run("Analyze gate filters normalize whitespace and case",
            TestAnalyzeGateFiltersNormalizeWhitespaceAndCase);
        failed += Run("Analyze gate filters missing type does not match type-only filter",
            TestAnalyzeGateFiltersMissingTypeDoesNotMatchTypeOnlyFilter);
        failed += Run("Analyze gate new-only suppresses baseline findings", TestAnalyzeGateNewIssuesOnlySuppressesBaselineFindings);
        failed += Run("Analyze gate new-only fails for new findings", TestAnalyzeGateNewIssuesOnlyFailsForNewFindings);
        failed += Run("Analyze gate new-only missing baseline schema logs inference", TestAnalyzeGateNewIssuesOnlyMissingSchemaLogsInference);
        failed += Run("Analyze gate new-only large legacy line does not wrap to zero",
            TestAnalyzeGateNewIssuesOnlyLargeLegacyLineDoesNotWrapToZero);
        failed += Run("Analyze gate new-only legacy line int-max matches int-max finding",
            TestAnalyzeGateNewIssuesOnlyLegacyLineIntMaxMatchesIntMaxFinding);
        failed += Run("Analyze gate new-only missing baseline unavailable", TestAnalyzeGateNewIssuesOnlyMissingBaselineIsUnavailable);
        failed += Run("Analyze gate new-only missing baseline can pass when unavailable allowed",
            TestAnalyzeGateNewIssuesOnlyMissingBaselineCanPassWhenUnavailableAllowed);
        failed += Run("Analyze gate new-only suppresses legacy baseline key path normalization",
            TestAnalyzeGateNewIssuesOnlySuppressesLegacyBaselineKeyPathNormalization);
        failed += Run("Analyze gate new-only suppresses legacy baseline key dot-relative prefix",
            TestAnalyzeGateNewIssuesOnlySuppressesLegacyBaselineKeyDotRelativePrefix);
        failed += Run("Analyze gate write baseline contract schema", TestAnalyzeGateWriteBaselineCreatesContractSchema);
        failed += Run("Reviewer schema includes analysis gate baseline properties", TestReviewerSchemaIncludesAnalysisGateBaselineProperties);
        failed += Run("Reviewer schema includes OpenAI-compatible provider and config", TestReviewerSchemaIncludesOpenAiCompatibleProviderAndConfig);
        failed += Run("Analyze gate duplication per-file threshold blocks", TestAnalyzeGateDuplicationFailsOnPerFileThreshold);
        failed += Run("Analyze gate duplication per-file threshold passes", TestAnalyzeGateDuplicationPassesWhenWithinThreshold);
        failed += Run("Analyze gate duplication uses per-file configured threshold",
            TestAnalyzeGateDuplicationUsesPerFileConfiguredThreshold);
        failed += Run("Analyze gate duplication unavailable can pass when allowed", TestAnalyzeGateDuplicationUnavailableCanPassWhenAllowed);
        failed += Run("Analyze gate duplication unavailable fails when configured", TestAnalyzeGateDuplicationUnavailableFailsWhenConfigured);
        failed += Run("Analyze gate duplication overall threshold blocks", TestAnalyzeGateDuplicationFailsOnOverallThreshold);
        failed += Run("Analyze gate duplication overall new-only suppresses baseline finding",
            TestAnalyzeGateDuplicationOverallNewOnlySuppressesBaselineFinding);
        failed += Run("Analyze gate duplication changed-files scope ignores untouched file",
            TestAnalyzeGateDuplicationScopeChangedFilesIgnoresUnchangedFiles);
        failed += Run("Analyze gate duplication changed-files scope blocks changed file",
            TestAnalyzeGateDuplicationScopeChangedFilesBlocksChangedFiles);
        failed += Run("Analyze gate duplication changed-files scope without changed-files is unavailable",
            TestAnalyzeGateDuplicationScopeChangedFilesWithoutChangedFilesIsUnavailable);
        failed += Run("Analyze gate duplication new-only suppresses baseline finding", TestAnalyzeGateDuplicationNewOnlySuppressesBaselineFindings);
        failed += Run("Analyze gate duplication overall delta blocks when increase exceeds allowed",
            TestAnalyzeGateDuplicationOverallDeltaBlocksWhenIncreaseExceedsAllowed);
        failed += Run("Analyze gate duplication overall delta window mismatch unavailable",
            TestAnalyzeGateDuplicationOverallDeltaWindowMismatchIsUnavailable);
        failed += Run("Analyze gate duplication overall delta uses baseline written by write-baseline",
            TestAnalyzeGateDuplicationOverallDeltaUsesBaselineWrittenByWriteBaseline);
        failed += Run("Analyze gate duplication overall delta missing baseline unavailable",
            TestAnalyzeGateDuplicationOverallDeltaMissingBaselineIsUnavailable);
        failed += Run("Analyze gate duplication file delta blocks when increase exceeds allowed",
            TestAnalyzeGateDuplicationFileDeltaBlocksWhenIncreaseExceedsAllowed);
        failed += Run("Analyze gate duplication file delta normalizes ./ paths",
            TestAnalyzeGateDuplicationFileDeltaNormalizesDotRelativePaths);
        failed += Run("Analyze gate duplication file delta normalizes ../ paths",
            TestAnalyzeGateDuplicationFileDeltaNormalizesParentRelativePaths);
        failed += Run("Analyze gate duplication file delta normalizes // paths",
            TestAnalyzeGateDuplicationFileDeltaNormalizesDoubleSlashes);
        failed += Run("Analyze gate duplication file delta window mismatch unavailable",
            TestAnalyzeGateDuplicationFileDeltaWindowMismatchIsUnavailable);
        failed += Run("Analyze gate duplication overall baseline skips null items", TestAnalyzeGateDuplicationOverallBaselineSkipsNullItems);
        failed += Run("Analyze gate duplication overall baseline rejects malformed fingerprints",
            TestAnalyzeGateDuplicationOverallBaselineRejectsMalformedFingerprints);
        failed += Run("Analyze gate duplication file baseline skips null items", TestAnalyzeGateDuplicationFileBaselineSkipsNullItems);
        failed += Run("Analyze gate duplication file baseline loads paths with colon",
            TestAnalyzeGateDuplicationFileBaselineLoadsPathsWithColon);
        failed += Run("Analyze gate duplication file baseline loads paths containing scope suffix tokens",
            TestAnalyzeGateDuplicationFileBaselineLoadsPathsContainingScopeSuffixTokens);
        failed += Run("Analyze gate duplication file baseline missing path returns not provided",
            TestAnalyzeGateDuplicationFileBaselineMissingPathReturnsNotProvided);
        failed += Run("Analyze gate write baseline includes duplication file snapshots when configured",
            TestAnalyzeGateWriteBaselineIncludesDuplicationFileSnapshotsWhenConfigured);
        failed += Run("Analyze gate write baseline includes duplication overall snapshot",
            TestAnalyzeGateWriteBaselineIncludesDuplicationOverallSnapshot);
        failed += Run("Analysis config reader normalizes duplication ruleIds",
            TestAnalysisConfigReaderNormalizesDuplicationRuleIds);
        failed += Run("Analysis config reader normalizes gate ruleIds",
            TestAnalysisConfigReaderNormalizesGateRuleIds);
        failed += Run("Analysis config reader keeps default duplication ruleIds on empty input",
            TestAnalysisConfigReaderKeepsDefaultDuplicationRuleIdsWhenConfiguredListEmpty);
        failed += Run("Analysis config reader reads run strict",
            TestAnalysisConfigReaderReadsRunStrict);
        failed += Run("Analysis config reader reads duplication maxOverallPercentIncrease",
            TestAnalysisConfigReaderReadsDuplicationMaxOverallPercentIncrease);
        failed += Run("Analysis config reader reads duplication maxFilePercentIncrease",
            TestAnalysisConfigReaderReadsDuplicationMaxFilePercentIncrease);
        failed += Run("Analysis config reader marks duplication scope explicit when provided",
            TestAnalysisConfigReaderMarksDuplicationScopeAsExplicitWhenProvided);
        failed += Run("Analysis config reader keeps duplication scope implicit when omitted",
            TestAnalysisConfigReaderKeepsDuplicationScopeImplicitWhenOmitted);
        failed += Run("Analyze gate changed-files accepts absolute in-workspace path",
            TestAnalyzeGateChangedFilesAcceptsAbsoluteInWorkspace);
        failed += Run("Analyze gate changed-files rejects absolute outside-workspace path",
            TestAnalyzeGateChangedFilesRejectsAbsoluteOutsideWorkspace);
        failed += Run("Analyze gate changed-files rejects relative traversal outside workspace",
            TestAnalyzeGateChangedFilesRejectsRelativeTraversalOutsideWorkspace);
        failed += Run("Analyze gate hotspots to-review blocks when threshold exceeded",
            TestAnalyzeGateHotspotsToReviewBlocksWhenAboveThreshold);
        failed += Run("Analyze gate hotspots honor ruleIds with baseline suppression",
            TestAnalyzeGateHotspotsHonorRuleIdFiltersWithBaselineSuppression);
        failed += Run("Analyze gate hotspot state path bound", TestAnalyzeGateHotspotsStatePathIsWorkspaceBound);
        failed += Run("Analyze gate help token", TestAnalyzeGateHelpToken);
        failed += Run("Doctor help", TestDoctorHelp);
        failed += Run("Doctor missing auth store fails", TestDoctorMissingAuthStoreFails);
        failed += Run("Doctor multiple bundles warns", TestDoctorMultipleBundlesWarns);
        failed += Run("Todo help", TestTodoHelp);
        failed += Run("GitHub ci signals summarize check runs", TestGitHubCiSignalsSummarizeCheckRunsCountsAndFailures);
        failed += Run("GitHub ci signals parse failed workflow runs", TestGitHubCiSignalsParseFailedWorkflowRunsFiltersHeadShaAndSorts);
        failed += Run("GitHub ci signals summarize workflow failure evidence",
            TestGitHubCiSignalsSummarizeWorkflowFailureEvidenceClassifiesKinds);
        failed += Run("Todo pr-watch ready-to-merge stop", TestPrWatchRecommendActionsReadyToMergeStops);
        failed += Run("Todo pr-watch prioritizes review before retry", TestPrWatchRecommendActionsPrioritizesReviewBeforeRetry);
        failed += Run("Todo pr-watch suppresses retry for actionable failures",
            TestPrWatchRecommendActionsSuppressesRetryForActionableFailures);
        failed += Run("Todo pr-watch legacy retry policy keeps actionable retry",
            TestPrWatchRecommendActionsLegacyPolicyKeepsRetryForActionableFailures);
        failed += Run("Todo pr-watch non-actionable retry policy allows operational or unknown failures",
            TestPrWatchShouldSuggestRetryForNonActionablePolicy);
        failed += Run("Todo pr-watch source precedence", TestPrWatchDetermineReviewSourceTypeUsesPrecedence);
        failed += Run("Todo pr-watch retry dedupe key stability", TestPrWatchRetryDedupeKeyIsStableAcrossRunOrdering);
        failed += Run("Todo pr-watch retry suppression by matching dedupe key", TestPrWatchRetrySuppressionByMatchingDedupeKey);
        failed += Run("Todo pr-watch retry suppression by cooldown", TestPrWatchRetrySuppressionByCooldown);
        failed += Run("Todo pr-watch retry suppression window expiry allows retry", TestPrWatchRetrySuppressionAllowsRetryWhenWindowExpired);
        failed += Run("Todo pr-watch normalize phase fallback", TestPrWatchNormalizePhaseFallback);
        failed += Run("Todo pr-watch normalize source sanitizes unsafe chars", TestPrWatchNormalizeSourceSanitizesUnsafeChars);
        failed += Run("Todo pr-watch planned audit includes dedupe and reason", TestPrWatchBuildPlannedAuditRecordsIncludesDedupeAndReason);
        failed += Run("Todo pr-watch authenticated login fallback uses actor env", TestPrWatchResolveAuthenticatedLoginFallbackUsesActorEnv);
        failed += Run("Todo pr-watch authenticated login fallback prefers actor over triggering actor", TestPrWatchResolveAuthenticatedLoginFallbackPrefersActorOverTriggeringActor);
        failed += Run("Todo pr-watch authenticated login fallback empty when unset", TestPrWatchResolveAuthenticatedLoginFallbackReturnsEmptyWhenUnset);
        failed += Run("Todo pr-watch consolidation source default keeps trimmed explicit source", TestPrWatchConsolidationResolveSourceWithDefaultKeepsTrimmedExplicitSource);
        failed += Run("Todo pr-watch consolidation source default uses event name when source empty", TestPrWatchConsolidationResolveSourceWithDefaultUsesEventNameWhenSourceEmpty);
        failed += Run("Todo pr-watch consolidation source default uses manual_cli when source and event name empty", TestPrWatchConsolidationResolveSourceWithDefaultUsesManualCliWhenSourceAndEventEmpty);
        failed += Run("Todo pr-watch consolidation tracker skipped when clean", TestPrWatchConsolidationTrackerIssueSkippedWhenRollupClean);
        failed += Run("Todo pr-watch consolidation tracker publishes when ratios or buckets non-zero",
            TestPrWatchConsolidationTrackerIssuePublishesWhenRatiosOrBucketsNonZero);
        failed += Run("Todo pr-watch consolidation tracker signals handle missing ratios",
            TestPrWatchConsolidationTrackerSignalsHandleMissingRatios);
        failed += Run("Todo pr-watch consolidation tracker publishes when retry policy guidance requests change",
            TestPrWatchConsolidationTrackerPublishesWhenRetryPolicyGuidanceRequestsChange);
        failed += Run("Todo pr-watch consolidation tracker label plan adds governance label when opted in",
            TestPrWatchConsolidationTrackerLabelPlanAddsGovernanceLabelWhenOptedIn);
        failed += Run("Todo pr-watch consolidation tracker label plan removes governance label when signal clears",
            TestPrWatchConsolidationTrackerLabelPlanRemovesGovernanceLabelWhenSignalClears);
        failed += Run("Todo pr-watch monitor rollup includes failed run kinds",
            TestPrWatchMonitorRollupIncludesFailedRunKinds);
        failed += Run("Todo pr-watch consolidation failure kinds flow into rollup metrics and summary",
            TestPrWatchConsolidationFailureKindsFlowIntoRollupMetricsAndSummary);
        failed += Run("Todo pr-watch retry policy guidance suggests non-actionable-only after operational streak",
            TestPrWatchRetryPolicyGuidanceSuggestsNonActionableOnlyAfterOperationalStreak);
        failed += Run("Todo pr-watch retry policy guidance keeps any when actionable trend is not stable",
            TestPrWatchRetryPolicyGuidanceKeepsAnyWhenActionableTrendIsNotStable);
        failed += Run("Todo pr-watch monitor compose source tag appends action", TestPrWatchMonitorComposeSourceTagAppendsActionWhenPresent);
        failed += Run("Todo pr-watch monitor compose source tag skips empty action", TestPrWatchMonitorComposeSourceTagSkipsEmptyAction);
        failed += Run("Todo pr-watch monitor resolves event action from payload", TestPrWatchMonitorResolveEventActionFromPayload);
        failed += Run("Todo pr-watch monitor resolves PR spec from payload", TestPrWatchMonitorResolvePrSpecFromPayload);
        failed += Run("Todo pr-watch monitor resolves source defaults keeps explicit source", TestPrWatchMonitorResolveSourceWithEventDefaultsKeepsExplicitSource);
        failed += Run("Todo pr-watch monitor resolves source defaults normalizes whitespace explicit source", TestPrWatchMonitorResolveSourceWithEventDefaultsNormalizesWhitespaceExplicitSource);
        failed += Run("Todo pr-watch monitor resolves source defaults uses event name when source empty", TestPrWatchMonitorResolveSourceWithEventDefaultsUsesEventNameWhenSourceEmpty);
        failed += Run("Todo pr-watch monitor resolves source defaults uses manual_cli when source and event name empty", TestPrWatchMonitorResolveSourceWithEventDefaultsUsesManualCliWhenSourceAndEventNameEmpty);
        failed += Run("Todo pr-watch monitor resolves PR defaults keeps explicit PR", TestPrWatchMonitorResolvePrSpecWithEventDefaultsKeepsExplicitPr);
        failed += Run("Todo pr-watch monitor parse failure warns and defaults safely", TestPrWatchMonitorLoadGitHubEventPayloadParseFailureWarnsAndDefaultsSafely);
        failed += Run("Todo pr-watch monitor workflow review triggers include submitted and edited",
            TestPrWatchMonitorWorkflowReviewTriggersIncludeSubmittedAndEdited);
        failed += Run("Todo pr-watch monitor workflow excludes review comment trigger",
            TestPrWatchMonitorWorkflowExcludesReviewCommentTrigger);
        failed += Run("Todo pr-watch nightly workflow uses direct CLI invocation",
            TestPrWatchNightlyConsolidationWorkflowUsesDirectCliInvocation);
        failed += Run("Todo pr-watch weekly governance workflow exposes optional overrides",
            TestPrWatchWeeklyGovernanceWorkflowExposesOptionalOverrides);
        failed += Run("Todo pr-watch assist workflow uses direct CLI invocation",
            TestPrWatchAssistRetryWorkflowUsesDirectCliInvocation);
        failed += Run("Todo pr-watch workflow parser supports scalar types value",
            TestPrWatchWorkflowParserSupportsScalarTypesValue);
        failed += Run("Todo pr-watch workflow parser supports flow sequence types value",
            TestPrWatchWorkflowParserSupportsFlowSequenceTypesValue);
        failed += Run("Todo pr-watch workflow parser missing file has clear error",
            TestPrWatchWorkflowParserMissingFileHasClearError);
        failed += Run("Todo unknown command", TestTodoUnknownCommandShowsMessage);
        failed += Run("Todo bot feedback render LF", TestBotFeedbackRenderHonorsLfNewlines);
        failed += Run("Todo bot feedback parse existing", TestBotFeedbackParseExistingPrBlockExtractsTasks);
        failed += Run("Todo bot feedback merge", TestBotFeedbackMergePreservesManualCheckedStateAndDropsStaleTasks);
        failed += Run("Todo bot feedback update section", TestBotFeedbackUpdateSectionIsDeterministicAndNoDuplicates);
        failed += Run("Todo bot feedback update section removes closed PR blocks", TestBotFeedbackUpdateSectionRemovesClosedPrBlocks);
        failed += Run("Todo bot feedback update section pruning preserves neighboring details blocks",
            TestBotFeedbackUpdateSectionPruningDoesNotDeleteNeighboringDetailsBlocks);
        failed += Run("Todo bot feedback update section clears when no open PR tasks", TestBotFeedbackUpdateSectionWithNoOpenPrsClearsBlocks);
        failed += Run("Todo bot feedback update section keeps non-PR details blocks when no open PR tasks",
            TestBotFeedbackUpdateSectionWithNoOpenPrsPreservesNonPrDetailsBlocks);
        failed += Run("Todo bot feedback update section keeps nested non-PR details blocks when no open PR tasks",
            TestBotFeedbackUpdateSectionWithNoOpenPrsPreservesNestedNonPrDetailsBlocks);
        failed += Run("Todo bot feedback parse tasks uses merge-blocker sections", TestBotFeedbackParseTasksUsesMergeBlockerSections);
        failed += Run("Todo bot feedback parse tasks legacy fallback", TestBotFeedbackParseTasksLegacyFallbackWithoutHeaders);
        failed += Run("Todo bot feedback task id uses lowercase fixed-length hex prefix",
            TestBotFeedbackTaskIdUsesLowercaseFixedLengthHexPrefix);
        failed += Run("Todo bot feedback issue exists query scopes open issues",
            TestBotFeedbackIssueExistsQueryScopesOpenIssues);
        failed += Run("Todo bot feedback issue lookup interpretation handles unknown state",
            TestBotFeedbackIssueLookupInterpretationHandlesUnknownState);
        failed += Run("Todo bot feedback issue title truncates by text elements",
            TestBotFeedbackIssueTitleTruncatesByTextElements);
        failed += Run("Todo bot feedback label lookup api path escapes label name",
            TestBotFeedbackLabelLookupApiPathEscapesLabelName);
        failed += Run("Todo bot feedback label lookup interpretation handles known states",
            TestBotFeedbackLabelLookupInterpretationHandlesKnownStates);
        failed += Run("Todo triage index tokenization", TestTriageIndexTokenizeNormalizesAndDropsStopWords);
        failed += Run("Todo triage index duplicate clusters", TestTriageIndexDuplicateClustersGroupNearMatches);
        failed += Run("Todo triage index PR scoring", TestTriageIndexScoreRewardsMergeableApprovedPrs);
        failed += Run("Todo triage index PR-issue explicit match", TestTriageIndexMatchPullRequestToIssuesSupportsExplicitReference);
        failed += Run("Todo triage index PR-issue direct match", TestTriageIndexMatchPullRequestToIssuesSupportsDirectIssueReference);
        failed += Run("Todo triage index issue-PR direct match", TestTriageIndexMatchIssueToPullRequestsSupportsDirectPullRequestReference);
        failed += Run("Todo triage index issue-PR URL match", TestTriageIndexMatchIssueToPullRequestsSupportsPullRequestUrlReference);
        failed += Run("Todo triage index category/tag inference", TestTriageIndexInferCategoryAndTagsDetectsSecurity);
        failed += Run("Todo triage index category/tag confidence inference", TestTriageIndexInferCategoryAndTagsWithConfidenceUsesEvidenceStrength);
        failed += Run("Todo triage index signal-quality inference", TestTriageIndexAssessSignalQualityDistinguishesStrongAndWeakContext);
        failed += Run("Todo triage index operational signals", TestTriageIndexAssessPullRequestOperationalSignalsClassifiesSizeRiskAndReadiness);
        failed += Run("Todo triage index operational signal calibration boundaries", TestTriageIndexAssessPullRequestOperationalSignalsCalibrationBoundaries);
        failed += Run("Todo PR signal backtest bucket stats", TestPullRequestSignalBacktestBuildBucketStatsCalculatesMergeRates);
        failed += Run("Todo issue review pull-request reference parsing", TestIssueReviewExtractPullRequestReferencesParsesMultipleForms);
        failed += Run("Todo issue review no-longer-applicable classification", TestIssueReviewAssessIssueForApplicabilityMarksResolvedInfraBlockerAsNoLongerApplicable);
        failed += Run("Todo issue review consecutive candidate gate", TestIssueReviewAssessIssueForApplicabilityRequiresConsecutiveCandidatesForAutoClose);
        failed += Run("Todo issue review allow/deny label policy", TestIssueReviewAssessIssueForApplicabilityRespectsAllowAndDenyLabelPolicy);
        failed += Run("Todo issue review action signals downgrade close proposal", TestIssueReviewActionSignalsDowngradeCloseProposalWhenReopenedOrTooFresh);
        failed += Run("Todo issue review action signals keep close proposal", TestIssueReviewActionSignalsKeepCloseProposalWhenSignalsStrong);
        failed += Run("Todo vision check out-of-scope classification", TestVisionCheckClassifiesOutOfScopeWhenOutTokensDominate);
        failed += Run("Todo vision check aligned classification", TestVisionCheckClassifiesAlignedWhenInTokensMatch);
        failed += Run("Todo vision check explicit reject policy precedence", TestVisionCheckExplicitRejectPolicyTakesPrecedence);
        failed += Run("Todo vision check explicit policy prefix parsing", TestVisionCheckParseSignalsSupportsExplicitPolicyPrefixes);
        failed += Run("Todo vision check strict contract parsing", TestVisionCheckParseDocumentSupportsStrictContract);
        failed += Run("Todo vision check heading variants satisfy strict contract", TestVisionCheckParseDocumentSupportsHeadingVariants);
        failed += Run("Todo vision check backticked policy prefixes satisfy strict contract", TestVisionCheckParseDocumentSupportsBacktickedPolicyPrefixes);
        failed += Run("Todo vision check enforce contract accepts backticked policy prefixes", TestVisionCheckRunEnforceContractSupportsBacktickedPolicyPrefixes);
        failed += Run("Todo vision check legacy decision heading satisfies strict contract", TestVisionCheckParseDocumentSupportsLegacyDecisionHeading);
        failed += Run("Todo vision check missing required section", TestVisionCheckParseDocumentReportsMissingRequiredSection);
        failed += Run("Todo vision check enforce contract exits non-zero", TestVisionCheckRunFailsOnContractWhenEnforced);
        failed += Run("Todo vision check fail on drift exits non-zero", TestVisionCheckRunFailsOnHighConfidenceDrift);
        failed += Run("Todo vision check rejects malformed drift thresholds", TestVisionCheckRejectsMalformedDriftThresholds);
        failed += Run("Todo project fields defaults", TestProjectFieldCatalogDefaultsIncludeVisionAndDecisionFields);
        failed += Run("Todo project fields optional governance profile", TestProjectFieldCatalogBuildEnsureFieldCatalogIncludesOptionalPrWatchGovernanceFields);
        failed += Run("Todo project labels include decision taxonomy", TestProjectLabelCatalogDefaultsIncludeDecisionLabels);
        failed += Run("Todo project labels include dynamic category and tag taxonomy", TestProjectLabelCatalogBuildEnsureCatalogIncludesDynamicCategoryAndTags);
        failed += Run("Todo project views include queue and merge views", TestProjectViewCatalogDefaultsIncludeQueueAndMergeViews);
        failed += Run("Todo project views optional governance profile", TestProjectViewCatalogBuildRecommendedViewsIncludesOptionalGovernanceView);
        failed += Run("Todo project views missing detection", TestProjectViewCatalogFindMissingDefaultViewsReturnsMissingOnly);
        failed += Run("Todo project views full coverage has no missing", TestProjectViewCatalogFindMissingDefaultViewsReturnsEmptyWhenComplete);
        failed += Run("Todo project views governance profile missing detection", TestProjectViewCatalogFindMissingRecommendedViewsIncludesGovernanceViewWhenEnabled);
        failed += Run("Todo project config reader parses governance features", TestProjectConfigReaderReadsPrWatchGovernanceFeatures);
        failed += Run("Todo project config reader falls back to legacy governance view flag", TestProjectConfigReaderFallsBackToLegacyGovernanceViewFlag);
        failed += Run("Todo project view checklist markdown includes marker and coverage", TestProjectViewChecklistMarkdownIncludesMarkerAndCoverage);
        failed += Run("Todo project view checklist markdown includes apply instructions", TestProjectViewChecklistMarkdownIncludesApplyInstructions);
        failed += Run("Todo project view checklist markdown includes governance view when enabled", TestProjectViewChecklistMarkdownIncludesOptionalGovernanceViewWhenEnabled);
        failed += Run("Todo project view apply markdown includes missing views and platform note", TestProjectViewApplyMarkdownIncludesMissingViewsAndPlatformNote);
        failed += Run("Todo project view apply markdown all views present shows completed checklist", TestProjectViewApplyMarkdownAllViewsPresentShowsCompletedChecklist);
        failed += Run("Todo project view apply markdown includes governance view when enabled", TestProjectViewApplyMarkdownIncludesOptionalGovernanceViewWhenEnabled);
        failed += Run("Todo project sync merges vision and canonical", TestProjectSyncBuildEntriesMergesVisionAndCanonical);
        failed += Run("Todo project sync parses category/tag confidence fields", TestProjectSyncBuildEntriesParsesCategoryAndTagConfidences);
        failed += Run("Todo project sync merges issue review action signals", TestProjectSyncBuildEntriesMergesIssueReviewSignals);
        failed += Run("Todo project sync parses governance no-flags", TestProjectSyncParseOptionsSupportsGovernanceNoFlags);
        failed += Run("Todo project sync config defaults enable governance apply modes", TestProjectSyncApplyProjectConfigFeatureDefaultsUsesConfigWhenUnspecified);
        failed += Run("Todo project sync config defaults respect explicit governance overrides", TestProjectSyncApplyProjectConfigFeatureDefaultsRespectsExplicitOverrides);
        failed += Run("Todo project sync labels include tags and high-confidence match", TestProjectSyncBuildLabelsIncludesTagsAndHighConfidenceIssueMatch);
        failed += Run("Todo project sync governance context prefers weekly tracker and parses signal", TestProjectSyncGovernanceContextPrefersWeeklyTrackerAndParsesSignal);
        failed += Run("Todo project sync governance context falls back to schedule tracker", TestProjectSyncGovernanceContextFallsBackToScheduleTracker);
        failed += Run("Todo project sync governance field values use suggested signal", TestProjectSyncBuildPrWatchGovernanceFieldValuesUseSuggestedSignal);
        failed += Run("Todo project sync governance field values clear when inactive", TestProjectSyncBuildPrWatchGovernanceFieldValuesClearWhenInactive);
        failed += Run("Todo project sync labels normalize dynamic category and tags", TestProjectSyncBuildLabelsNormalizesDynamicCategoryAndTags);
        failed += Run("Todo project sync labels skip low-confidence category/tags", TestProjectSyncBuildLabelsSkipsLowConfidenceCategoryAndTags);
        failed += Run("Todo project sync labels mark low-confidence match for review", TestProjectSyncBuildLabelsUsesNeedsReviewForLowConfidenceIssueMatch);
        failed += Run("Todo project sync labels use related-issue fallback", TestProjectSyncBuildLabelsUsesRelatedIssueFallbackWhenMatchedIssueMissing);
        failed += Run("Todo project sync labels issue pull-request matches", TestProjectSyncBuildLabelsUsesIssuePullRequestMatchSignals);
        failed += Run("Todo project sync labels use issue related pull-request fallback", TestProjectSyncBuildLabelsUsesIssueRelatedPullRequestFallbackWhenMatchedPullRequestMissing);
        failed += Run("Todo project sync decision suggests merge-candidate", TestProjectSyncBuildEntriesSuggestsMergeCandidateForBestReadyPr);
        failed += Run("Todo project sync decision suggests defer for blocked PR", TestProjectSyncBuildEntriesSuggestsDeferForBlockedPr);
        failed += Run("Todo project sync decision suggests defer for low-signal PR", TestProjectSyncBuildEntriesSuggestsDeferForLowSignalPr);
        failed += Run("Todo project sync derives issue pull-request matches", TestProjectSyncBuildEntriesDerivesIssuePullRequestMatches);
        failed += Run("Todo project sync preserves higher-confidence issue-side pull-request matches", TestProjectSyncBuildEntriesPreservesHigherConfidenceIssueSidePullRequestMatch);
        failed += Run("Todo project sync uses issue related pull-request fallback", TestProjectSyncBuildEntriesUsesIssueRelatedPullRequestFallback);
        failed += Run("Todo project sync uses pull-request related issue fallback", TestProjectSyncBuildEntriesUsesPullRequestRelatedIssueFallback);
        failed += Run("Todo project sync derives missing matched issue confidence from related issues", TestProjectSyncBuildEntriesDerivesMissingMatchedIssueConfidenceFromRelatedIssues);
        failed += Run("Todo project sync comment filters by confidence", TestProjectSyncBuildIssueMatchSuggestionCommentFiltersByConfidence);
        failed += Run("Todo project sync PR comments include issue-side candidates", TestProjectSyncBuildPullRequestIssueSuggestionCommentsIncludesIssueSideCandidates);
        failed += Run("Todo project sync stale comment targets for pull requests", TestProjectSyncBuildStaleSuggestionCommentTargetsForPullRequests);
        failed += Run("Todo project sync stale comment targets for issues", TestProjectSyncBuildStaleSuggestionCommentTargetsForIssuesDedupesAndSorts);
        failed += Run("Todo project sync comment omitted for weak candidates", TestProjectSyncBuildIssueMatchSuggestionCommentReturnsNullWithoutQualifiedCandidates);
        failed += Run("Todo project sync issue backlink comments aggregate PRs", TestProjectSyncBuildIssueBacklinkSuggestionCommentsAggregatesPullRequests);
        failed += Run("Todo project sync issue backlink comments include issue-side candidates", TestProjectSyncBuildIssueBacklinkSuggestionCommentsIncludesIssueSideCandidates);
        failed += Run("Todo project sync issue backlink comment threshold and limit", TestProjectSyncBuildIssueBacklinkSuggestionCommentRespectsThresholdAndLimit);
        failed += Run("Todo project sync related issues field value", TestProjectSyncBuildRelatedIssuesFieldValueOrdersAndLimitsCandidates);
        failed += Run("Todo project sync related pull requests field value", TestProjectSyncBuildRelatedPullRequestsFieldValueOrdersAndLimitsCandidates);
        failed += Run("Todo project sync match reason field value", TestProjectSyncBuildMatchReasonFieldValueNormalizesReasonText);
        failed += Run("Todo project sync tag confidence summary field value",
            TestProjectSyncBuildTagConfidenceSummaryFieldValueOrdersAndLimitsCandidates);
        failed += Run("Todo project sync tag confidence summary fallback",
            TestProjectSyncBuildTagConfidenceSummaryFieldValueFallsBackToConfidenceMap);
        failed += Run("Todo repository label sync plan add/remove managed labels", TestRepositoryLabelManagerBuildManagedLabelSyncPlanAddsAndRemovesManagedOnly);
        failed += Run("Todo repository label sync plan no-op when aligned", TestRepositoryLabelManagerBuildManagedLabelSyncPlanNoChangesWhenAligned);
        failed += Run("Todo project bootstrap renders project target", TestProjectBootstrapRenderWorkflowTemplateInjectsProjectTarget);
        failed += Run("Todo project bootstrap clamps max items", TestProjectBootstrapRenderWorkflowTemplateClampsMaxItems);
        failed += Run("Todo project bootstrap renders vision template", TestProjectBootstrapRenderVisionTemplateInjectsContext);
        failed += Run("Todo project bootstrap vision template strict contract sections", TestProjectBootstrapVisionTemplateIncludesStrictContractSections);
        failed += Run("Todo project bootstrap workflow enables label apply", TestProjectBootstrapWorkflowTemplateEnablesApplyLabels);
        failed += Run("Todo project bootstrap workflow upserts control issue summary comment",
            TestProjectBootstrapWorkflowTemplateUpsertsControlIssueSummaryComment);
        failed += Run("Todo triage index workflow upserts control issue summary comment",
            TestTriageIndexWorkflowTemplateUpsertsControlIssueSummaryComment);
        failed += Run("Todo project bootstrap control issue body includes context", TestProjectBootstrapBuildControlIssueBodyIncludesProjectContext);
        failed += Run("Todo project bootstrap parses issue url output", TestProjectBootstrapParseIssueNumberFromGhOutputParsesIssueUrl);
        failed += Run("Todo project bootstrap parses trailing issue number", TestProjectBootstrapParseIssueNumberFromGhOutputParsesTrailingInteger);
        failed += Run("Todo project bootstrap rejects conflicting control issue options", TestProjectBootstrapRejectsConflictingControlIssueOptions);
        failed += RunAnalyzeToolRunnerTests();
        failed += RunAnalyzeRunStrictBehaviorTests();
        failed += Run("Analyze run internal file size rule", TestAnalyzeRunInternalFileSizeRule);
        failed += Run("Analyze run internal findings use catalog tool metadata",
            TestAnalyzeRunInternalFindingsUseCatalogToolMetadata);
        failed += Run("Analyze run internal file size severity none", TestAnalyzeRunInternalFileSizeRuleDisabledBySeverity);
        failed += Run("Analyze run internal file size skips generated and excluded paths",
            TestAnalyzeRunInternalFileSizeRuleSkipsGeneratedAndExcluded);
        failed += Run("Analyze run internal custom generated suffix case-insensitive",
            TestAnalyzeRunInternalFileSizeRuleCustomGeneratedSuffixCaseInsensitive);
        failed += Run("Analyze run internal generated header scan override",
            TestAnalyzeRunInternalFileSizeRuleGeneratedHeaderLineOverride);
        failed += Run("Analyze run internal custom excluded directory",
            TestAnalyzeRunInternalFileSizeRuleCustomExcludedDirectory);
        failed += Run("Analyze run internal custom excluded path",
            TestAnalyzeRunInternalFileSizeRuleCustomExcludedPath);
        failed += Run("Analyze run internal excluded path normalizes repeated separators",
            TestAnalyzeRunInternalFileSizeRuleExcludePathNormalizesRepeatedSeparators);
        failed += Run("Analyze run internal file size newline variants",
            TestAnalyzeRunInternalFileSizeRuleHandlesLineEndings);
        failed += Run("Analyze run internal malformed tags warn and fallback",
            TestAnalyzeRunInternalFileSizeRuleWarnsOnMalformedTags);
        failed += Run("Analyze run internal unknown tag prefixes warn",
            TestAnalyzeRunInternalFileSizeRuleWarnsOnUnknownTagPrefixes);
        failed += Run("Analyze run internal generated header scan can be disabled",
            TestAnalyzeRunInternalFileSizeRuleGeneratedHeaderScanCanBeDisabled);
        failed += Run("Analyze run internal generated header marker supports hash comments",
            TestAnalyzeRunInternalFileSizeRuleGeneratedHeaderMarkerSupportsHashComments);
        failed += Run("Analyze run internal maintainability supports multiple rules",
            TestAnalyzeRunInternalMaintainabilitySupportsMultipleRules);
        failed += Run("Analyze run internal maintainability helper positive paths",
            TestAnalyzeRunInternalMaintainabilityHelpersPositivePaths);
        failed += Run("Analyze run internal maintainability helper failure includes match count",
            TestAnalyzeRunInternalMaintainabilityHelpersFailureIncludesMatchCount);
        failed += Run("Analyze run internal maintainability helper path and suffix failure include match count",
            TestAnalyzeRunInternalMaintainabilityHelpersFailureIncludesMatchCountForPathAndSuffix);
        failed += Run("Analyze run internal maintainability helper rejects empty rule id",
            TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyRuleId);
        failed += Run("Analyze run internal maintainability helper rejects empty path suffix",
            TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyPathSuffix);
        failed += Run("Analyze run internal maintainability helper rejects empty assertion message",
            TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyAssertionMessage);
        failed += Run("Analyze run internal maintainability helper rejects null findings",
            TestAnalyzeRunInternalMaintainabilityHelpersRejectNullFindings);
        failed += Run("Analyze run internal write-tool schema rule flags missing helpers",
            TestAnalyzeRunInternalWriteToolSchemaRuleFlagsMissingHelpers);
        failed += Run("Analyze run internal write-tool schema rule accepts canonical helpers",
            TestAnalyzeRunInternalWriteToolSchemaRuleAcceptsCanonicalHelpers);
        failed += Run("Analyze run internal write-tool schema rule ignores read-only tools",
            TestAnalyzeRunInternalWriteToolSchemaRuleIgnoresReadOnlyTools);
        failed += Run("Analyze run internal write-tool schema rule ignores auth-only definitions",
            TestAnalyzeRunInternalWriteToolSchemaRuleIgnoresAuthenticationOnlyToolDefinitions);
        failed += Run("Analyze run internal AD required-domain helper rule flags missing canonical helpers",
            TestAnalyzeRunInternalAdRequiredDomainHelperRuleFlagsMissingCanonicalHelpers);
        failed += Run("Analyze run internal AD required-domain helper rule accepts canonical helpers",
            TestAnalyzeRunInternalAdRequiredDomainHelperRuleAcceptsCanonicalHelpers);
        failed += Run("Analyze run internal max-results metadata helper rule flags direct meta add",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsDirectMetaAdd);
        failed += Run("Analyze run internal max-results metadata helper rule accepts canonical helper",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleAcceptsCanonicalHelper);
        failed += Run("Analyze run internal max-results metadata helper rule ignores non-tool files",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleIgnoresNonToolFiles);
        failed += Run("Analyze run internal max-results metadata helper rule ignores near-miss keys",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleIgnoresNearMissMetadataKeys);
        failed += Run("Analyze run internal max-results metadata helper rule accepts qualified canonical helper call",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleAcceptsQualifiedCanonicalHelperCall);
        failed += Run("Analyze run internal max-results metadata helper rule flags indexer assignment",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsIndexerAssignment);
        failed += Run("Analyze run internal max-results metadata helper rule flags case-variant metadata key",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsCaseVariantMetadataKey);
        failed += Run("Analyze run internal max-results metadata helper rule flags only max_results in mixed adds",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsOnlyMaxResultsInMixedMetaAdds);
        failed += Run("Analyze run internal max-results metadata helper rule deduplicates same-line matches",
            TestAnalyzeRunInternalMaxResultsMetaHelperRuleDeduplicatesSameLineMatches);
        failed += Run("Analyze run internal canonical bounded-int helper rule flags legacy helper usage",
            TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleFlagsLegacyHelperUsage);
        failed += Run("Analyze run internal canonical bounded-int helper rule accepts canonical helper usage",
            TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleAcceptsCanonicalHelperUsage);
        failed += Run("Analyze run internal canonical bounded-int helper rule ignores ToolArgs implementation file",
            TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleIgnoresToolArgsImplementationFile);
        failed += Run("Analyze run internal canonical bounded-int helper rule ignores IntelligenceX.Tools.Tests project",
            TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleIgnoresToolsTestsProject);
        failed += Run("Analyze run internal EventLog max-results helper rule flags bounded max_results path",
            TestAnalyzeRunInternalEventLogMaxResultsHelperRuleFlagsBoundedMaxResultsPath);
        failed += Run("Analyze run internal EventLog max-results helper rule flags legacy ResolveMaxResults path",
            TestAnalyzeRunInternalEventLogMaxResultsHelperRuleFlagsLegacyResolveMaxResults);
        failed += Run("Analyze run internal EventLog max-results helper rule accepts explicit EventLog helpers",
            TestAnalyzeRunInternalEventLogMaxResultsHelperRuleAcceptsExplicitEventLogHelpers);
        failed += Run("Analyze run internal EventLog max-results helper rule allows bounded helper for non-max_results args",
            TestAnalyzeRunInternalEventLogMaxResultsHelperRuleAllowsBoundedOptionForNonMaxResultsArgs);
        failed += Run("Analyze run internal EventLog max-results helper rule ignores non-EventLog tools",
            TestAnalyzeRunInternalEventLogMaxResultsHelperRuleIgnoresNonEventLogTools);
        failed += Run("Analyze run internal maintainability resolves canonical rule-id registration",
            TestAnalyzeRunInternalMaintainabilityResolvesCanonicalRuleIdRegistration);
        failed += Run("Analyze run internal maintainability warns on unmapped internal rule",
            TestAnalyzeRunInternalMaintainabilityWarnsOnUnmappedInternalRule);
        failed += Run("Analyze run internal maintainability warns on ambiguous internal rule match",
            TestAnalyzeRunInternalMaintainabilityWarnsOnAmbiguousInternalRuleMatch);
        failed += RunAnalysisMaintainabilityDuplicationTests();
        failed += RunAnalysisPolicyReportingTests();
        failed += Run("Structured findings block", TestStructuredFindingsBlock);
        failed += Run("Trim patch hunk boundary", TestTrimPatchStopsAtHunkBoundary);
        failed += Run("Trim patch tail hunk", TestTrimPatchKeepsTailHunk);
        failed += Run("Trim patch tail hunk (two hunks)", TestTrimPatchKeepsTailHunkTwoHunks);
        failed += Run("Trim patch CRLF", TestTrimPatchPreservesCrlf);
        failed += Run("Trim patch keeps last hunk", TestTrimPatchKeepsLastHunk);
        failed += Run("Review intent applies focus", TestReviewIntentAppliesFocus);
        failed += Run("Review intent respects focus", TestReviewIntentRespectsFocus);
        failed += Run("Review provider alias parsing", TestReviewProviderAliasParsing);
        failed += Run("Review provider contract capabilities", TestReviewProviderContractCapabilities);
        failed += Run("Review provider config alias", TestReviewProviderConfigAlias);
        failed += Run("Review Claude provider config alias", TestReviewClaudeProviderConfigAlias);
        failed += Run("Review provider invalid config throws", TestReviewProviderConfigInvalidThrows);
        failed += Run("Review Claude API provider runs and records telemetry", TestReviewClaudeApiProviderRunsAndRecordsTelemetry);
        failed += Run("OpenAI-compatible rejects http non-loopback by default", TestReviewOpenAiCompatibleRejectsHttpNonLoopbackByDefault);
        failed += Run("OpenAI-compatible preflight treats 405 as reachable", TestReviewOpenAiCompatiblePreflightTreats405AsReachable);
        failed += Run("OpenAI-compatible follows redirects", TestReviewOpenAiCompatibleFollowsRedirects);
        failed += Run("OpenAI-compatible preserves POST body across redirects", TestReviewOpenAiCompatiblePreservesPostBodyAcrossRedirects);
        failed += Run("OpenAI-compatible 303 redirect switches POST to GET", TestReviewOpenAiCompatibleRedirect303SwitchesToGet);
        failed += Run("OpenAI-compatible non-diagnostics omits remote error body",
            TestReviewOpenAiCompatibleDoesNotLeakErrorBodyWhenDiagnosticsFalse);
        failed += Run("OpenAI-compatible rejects cross-host redirects", TestReviewOpenAiCompatibleRejectsCrossHostRedirects);
        failed += Run("Review config loader reads openaiAccountRotation camelCase",
            TestReviewConfigLoaderReadsOpenAiAccountRotationCamelCase);
        failed += Run("Review config loader reads legacy includeRelatedPullRequests alias",
            TestReviewConfigLoaderReadsLegacyIncludeRelatedPullRequestsAlias);
        failed += Run("Review config loader prefers canonical includeRelatedPrs when both keys exist",
            TestReviewConfigLoaderPrefersCanonicalIncludeRelatedPrsWhenBothKeysPresent);
        failed += Run("Review provider fallback env", TestReviewProviderFallbackEnv);
        failed += Run("Review provider invalid env throws", TestReviewProviderEnvInvalidThrows);
        failed += Run("Review provider fallback config", TestReviewProviderFallbackConfig);
        failed += Run("Review provider fallback invalid config throws", TestReviewProviderFallbackConfigInvalidThrows);
        failed += Run("Review provider fallback plan", TestReviewProviderFallbackPlan);
        failed += Run("Review provider health env", TestReviewProviderHealthEnv);
        failed += Run("Review provider health config", TestReviewProviderHealthConfig);
        failed += Run("Review provider circuit breaker", TestReviewProviderCircuitBreaker);
        failed += Run("Review intent applies defaults", TestReviewIntentAppliesDefaults);
        failed += Run("Review intent respects settings", TestReviewIntentRespectsSettings);
        failed += Run("Review intent perf alias", TestReviewIntentPerfAlias);
        failed += Run("Review intent null settings", TestReviewIntentNullSettings);
        failed += Run("Triage-only loads threads", TestTriageOnlyLoadsThreads);
        failed += Run("Build extras captures stale auto-resolve permission failures",
            TestBuildExtrasCapturesStaleAutoResolvePermissionFailures);
        failed += Run("Build extras includes ci context section", TestBuildExtrasIncludesCiContextSection);
        failed += Run("Build extras ci context auto skips operational-only snippets",
            TestBuildExtrasCiContextAutoSkipsOperationalOnlySnippets);
        failed += Run("Build extras ci context failure is supplemental", TestBuildExtrasCiContextFailureIsSupplemental);
        failed += Run("Build extras loads issue comments for external history",
            TestBuildExtrasLoadsIssueCommentsForExternalHistory);
        failed += Run("Build extras keeps issue comment prompt cap with history",
            TestBuildExtrasKeepsIssueCommentPromptCapWithHistory);
        failed += Run("Build extras ci failure evidence failure is supplemental",
            TestBuildExtrasCiFailureEvidenceFailureIsSupplemental);
        failed += Run("Triage thread hydration uses fallback client when provided",
            TestTriageThreadHydrationUsesFallbackClientWhenProvided);
        failed += Run("Review code host env", TestReviewCodeHostEnv);
        failed += Run("Reviewer untrusted PR skips auth store write from env", TestReviewerUntrustedPrSkipsAuthStoreWriteFromEnv);
        failed += Run("Reviewer validate auth rejects expired bundle for non-native transport with refresh guidance",
            TestReviewerValidateAuthRejectsExpiredBundleForNonNativeTransportWithRefreshGuidance);
        failed += Run("Reviewer GitHub token resolver uses GH_TOKEN fallback", TestReviewerGitHubTokenResolverUsesGhTokenFallback);
        failed += Run("Reviewer GitHub token resolver prefers GITHUB_TOKEN", TestReviewerGitHubTokenResolverPrefersGithubTokenOverGhToken);
        failed += Run("GitHub context cache", TestGitHubContextCache);
        failed += Run("GitHub concurrency env", TestGitHubConcurrencyEnv);
        failed += Run("GitHub client concurrency", TestGitHubClientConcurrency);
        failed += Run("GitHub required conversation resolution lookup", TestGitHubRequiredConversationResolutionLookup);
        failed += Run("GitHub code host reader smoke", TestGitHubCodeHostReaderSmoke);
        failed += Run("GitHub compare truncation", TestGitHubCompareTruncation);
        failed += Run("Diff range compare truncation", TestDiffRangeCompareTruncation);
        failed += Run("Azure auth scheme env", TestAzureAuthSchemeEnv);
        failed += Run("Azure auth scheme invalid env", TestAzureAuthSchemeInvalidEnv);
        failed += Run("Review settings defaults and env merge", TestReviewSettingsDefaultsAndEnvMerge);
        failed += Run("Review threads auto-resolve sweep no-blockers config",
            TestReviewThreadsAutoResolveSweepNoBlockersConfig);
        failed += Run("Review threads auto-resolve sweep no-blockers env",
            TestReviewThreadsAutoResolveSweepNoBlockersEnv);
        failed += Run("Review merge-blocker policy config", TestReviewMergeBlockerPolicyConfig);
        failed += Run("Review merge-blocker policy env", TestReviewMergeBlockerPolicyEnv);
        failed += Run("Review merge-blocker policy env normalizes whitespace",
            TestReviewMergeBlockerPolicyEnvNormalizesWhitespace);
        failed += Run("Review settings load config then env precedence", TestReviewSettingsLoadConfigThenEnvPrecedence);
        failed += Run("Review settings load config then env precedence for ciContext and swarm",
            TestReviewSettingsLoadConfigThenEnvPrecedenceForCiContextAndSwarm);
        failed += Run("Review settings empty input falls back to env override",
            TestReviewSettingsEmptyInputFallsBackToEnvOverride);
        failed += Run("Review settings load swarm reviewer objects", TestReviewSettingsLoadSwarmReviewerObjects);
        failed += Run("Review settings agent profile selects authenticator and model",
            TestReviewSettingsAgentProfileSelectsAuthenticatorAndModel);
        failed += Run("Review settings agent profile rejects unknown authenticator and transport",
            TestReviewSettingsAgentProfileRejectsUnknownAuthenticatorAndTransport);
        failed += Run("Review settings model-only agent profile updates copilot model",
            TestReviewSettingsModelOnlyAgentProfileUpdatesCopilotModel);
        failed += Run("Review settings env swarm reviewers JSON", TestReviewSettingsEnvSwarmReviewersJson);
        failed += Run("Review settings load config allows zero for non-negative limits",
            TestReviewSettingsLoadConfigAllowsZeroForNonNegativeLimits);
        failed += Run("Review settings env allows zero for non-negative limits",
            TestReviewSettingsFromEnvironmentAllowsZeroForNonNegativeLimits);
        failed += Run("Setup-generated reviewer config validates and loads canonical related PRs",
            TestSetupGeneratedReviewerConfigValidatesAndLoadsWithCanonicalRelatedPrs);
        failed += Run("Review settings policy preview clamp range", TestReviewSettingsPolicyRulePreviewConfigClampRange);
        failed += Run("Azure code host reader smoke", TestAzureDevOpsCodeHostReaderSmoke);
        failed += Run("Review threads diff range normalize", TestReviewThreadsDiffRangeNormalize);
        failed += Run("Copilot env allowlist config", TestCopilotEnvAllowlistConfig);
        failed += Run("Copilot agent profile config preserves explicit empty maps",
            TestCopilotAgentProfileConfigPreservesExplicitEmptyMaps);
        failed += Run("Non-copilot agent profile ignores root Copilot aliases",
            TestNonCopilotAgentProfileIgnoresRootCopilotAliases);
        failed += Run("Copilot agent profile allows root Copilot aliases",
            TestCopilotAgentProfileAllowsRootCopilotAliases);
        failed += Run("Review config loader apply materializes selected agent profile",
            TestReviewConfigLoaderApplyMaterializesSelectedAgentProfile);
        failed += Run("Copilot launcher env", TestCopilotLauncherEnv);
        failed += Run("Copilot model env overrides generic model", TestCopilotModelEnvOverridesGenericModel);
        failed += Run("Copilot default OpenAI model uses CLI default", TestCopilotDefaultOpenAiModelUsesCliDefault);
        failed += Run("Copilot prompt runner parses JSON output", TestCopilotPromptRunnerParsesJsonOutput);
        failed += Run("Copilot prompt runner parses concatenated JSON output",
            TestCopilotPromptRunnerParsesConcatenatedJsonOutput);
        failed += Run("Copilot prompt runner falls back to stdout when JSON shares a line with noise",
            TestCopilotPromptRunnerFallsBackToStdoutWhenJsonSharesLineWithNoise);
        failed += Run("Copilot prompt runner falls back to stdout when JSON has trailing noise",
            TestCopilotPromptRunnerFallsBackToStdoutWhenJsonHasTrailingNoise);
        failed += Run("Copilot prompt runner falls back to stdout when JSON is valid but no assistant message was parsed",
            TestCopilotPromptRunnerFallsBackToStdoutWhenJsonIsValidButNoAssistantMessageWasParsed);
        failed += Run("Copilot prompt runner rejects malformed JSON without assistant message",
            TestCopilotPromptRunnerRejectsMalformedJsonWithoutAssistantMessage);
        failed += Run("Copilot prompt runner falls back to stdout when brace noise prevents JSON parsing",
            TestCopilotPromptRunnerFallsBackToStdoutWhenBraceNoisePreventsJsonParsing);
        failed += Run("Copilot prompt runner ignores brace noise before a valid JSON line",
            TestCopilotPromptRunnerIgnoresBraceNoiseBeforeValidJsonLine);
        failed += Run("Copilot prompt runner falls back to stdout when a brace line starts with non-JSON text",
            TestCopilotPromptRunnerFallsBackToStdoutWhenBraceLineStartsWithNonJsonToken);
        failed += Run("Copilot prompt runner does not treat JSON warnings as review content",
            TestCopilotPromptRunnerDoesNotTreatJsonWarningsAsReviewContent);
        failed += Run("Copilot prompt runner builds MCP-disabled args",
            TestCopilotPromptRunnerBuildsMcpDisabledArgs);
        failed += Run("Copilot prompt runner retries prompt argument when stdin produces no review",
            TestCopilotPromptRunnerRetriesPromptArgumentWhenStdinProducesNoReview);
        failed += Run("Copilot prompt runner prefers transport retry before compatibility fallback",
            TestCopilotPromptRunnerPrefersTransportRetryBeforeCompatibilityFallback);
        failed += Run("Copilot prompt runner wraps rooted Windows cmd paths",
            TestCopilotPromptRunnerWrapsRootedWindowsCmdPaths);
        failed += Run("Copilot prompt runner detects unsupported MCP flag",
            TestCopilotPromptRunnerDetectsUnsupportedMcpFlag);
        failed += Run("Copilot prompt runner accepts successful warnings without retry",
            TestCopilotPromptRunnerAcceptsSuccessfulWarningsWithoutRetry);
        failed += Run("Copilot gh launcher builds wrapper command", TestCopilotGhLauncherBuildsWrapperCommand);
        failed += Run("Copilot auto launcher uses binary",
            TestCopilotAutoLauncherUsesBinary);
        failed += Run("Copilot auto launcher keeps gh wrapper explicit",
            TestCopilotAutoLauncherKeepsGhWrapperExplicit);
        failed += Run("Copilot auto launcher uses binary for auto-install",
            TestCopilotAutoLauncherUsesBinaryForAutoInstall);
        failed += Run("Copilot CLI auto-install defaults prefer Linux script",
            TestCopilotCliAutoInstallDefaultsPreferLinuxScript);
        failed += Run("Copilot CLI auto-install defaults honor Linux prerelease",
            TestCopilotCliAutoInstallDefaultsHonorLinuxPrerelease);
        failed += Run("Copilot CLI auto-install defaults keep macOS Homebrew",
            TestCopilotCliAutoInstallDefaultsKeepMacHomebrew);
        failed += Run("Copilot launcher diagnostics describe resolved command",
            TestCopilotLauncherDiagnosticsDescribeResolvedCommand);
        failed += Run("Copilot binary launcher keeps direct cli path", TestCopilotBinaryLauncherKeepsDirectCliPath);
        failed += Run("Copilot client wraps rooted Windows cmd paths", TestCopilotClientWrapsRootedWindowsCmdPaths);
        failed += Run("Copilot inherit env default", TestCopilotInheritEnvironmentDefault);
        failed += Run("Copilot direct timeout validation", TestCopilotDirectTimeoutValidation);
        failed += Run("Copilot chat timeout validation", TestCopilotChatTimeoutValidation);
        failed += Run("Copilot prompt timeout honors configured wait", TestCopilotPromptTimeoutUsesRunnerSafeMinimum);
        failed += Run("Copilot prompt timeout honors higher explicit wait", TestCopilotPromptTimeoutHonorsHigherExplicitWait);
        failed += Run("Copilot CLI session timeout honors configured wait",
            TestCopilotCliSessionTimeoutUsesRunnerSafeMinimum);
        failed += Run("Copilot CLI session timeout honors higher explicit wait",
            TestCopilotCliSessionTimeoutHonorsHigherExplicitWait);
        failed += Run("Copilot prompt failure falls back for timeout and prompt errors",
            TestCopilotPromptFailureFallsBackForTimeoutAndPromptErrors);
        failed += Run("Copilot prompt mode selection uses cliUrl only",
            TestCopilotPromptModeSelectionUsesCliUrlOnly);
        failed += Run("Copilot CLI session requires idle signal for completion",
            TestCopilotCliSessionRequiresIdleSignalForCompletion);
        failed += Run("Copilot prompt start failure keeps cause details",
            TestCopilotPromptStartFailureKeepsCauseDetails);
        failed += Run("Copilot install resolver finds platform install", TestCopilotInstallResolverFindsPlatformInstall);
        failed += Run("Copilot prompt runner rejects missing configured cli path",
            TestCopilotPromptRunnerRejectsMissingConfiguredCliPath);
        failed += Run("Copilot prompt runner write honors timeout", TestCopilotPromptRunnerWriteHonorsTimeout);
        failed += Run("Copilot direct auth conflict", TestCopilotDirectAuthorizationConflict);
        failed += Run("Copilot CLI path requires env", TestCopilotCliPathRequiresEnvironment);
        failed += Run("Copilot CLI path optional with url", TestCopilotCliPathOptionalWithUrl);
        failed += Run("Copilot CLI url validation", TestCopilotCliUrlValidation);
        failed += Run("Copilot prompt requires Actions token", TestCopilotPromptRunnerRequiresActionsCopilotToken);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads default bot logins include managed bots", TestResolveThreadsDefaultBotLoginsIncludeManagedBots);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
        failed += Run("Resolve-threads runner treats already-resolved as success",
            TestResolveThreadsRunnerTreatsAlreadyResolvedAsSuccess);
        failed += Run("OpenAI account order round-robin", TestOpenAiAccountOrderRoundRobin);
        failed += Run("OpenAI account order round-robin many accounts", TestOpenAiAccountOrderRoundRobinSupportsManyAccounts);
        failed += Run("OpenAI account order sticky", TestOpenAiAccountOrderSticky);
        failed += Run("Normalize account id list dedupes case-insensitive", TestNormalizeAccountIdListDedupesCaseInsensitive);
        failed += Run("Try resolve OpenAI account stores rotated order", TestTryResolveOpenAiAccountStoresRotatedOrder);
        failed += Run("Try resolve OpenAI account prefers explicit primary over ids list",
            TestTryResolveOpenAiAccountPrefersExplicitPrimaryOverIdsList);
        failed += Run("Filter files include-only", TestFilterFilesIncludeOnly);
        failed += Run("Filter files exclude-only", TestFilterFilesExcludeOnly);
        failed += Run("Filter files include+exclude", TestFilterFilesIncludeExclude);
        failed += Run("Filter files glob patterns", TestFilterFilesGlobPatterns);
        failed += Run("Filter files empty filters", TestFilterFilesEmptyFilters);
        failed += Run("Filter files skip binary", TestFilterFilesSkipBinary);
        failed += Run("Filter files skip binary case-insensitive", TestFilterFilesSkipBinaryCaseInsensitive);
        failed += Run("Filter files skip generated", TestFilterFilesSkipGenerated);
        failed += Run("Filter files skip before include", TestFilterFilesSkipBeforeInclude);
        failed += Run("Filter files generated globs extend", TestFilterFilesGeneratedGlobsExtend);
        failed += Run("Workflow changes detection", TestWorkflowChangesDetection);
        failed += Run("Workflow changes filtering", TestWorkflowChangesFiltering);
        failed += Run("Workflow guard note skip", TestWorkflowGuardNoteSkip);
        failed += Run("Workflow guard note filtered", TestWorkflowGuardNoteFiltered);
        failed += Run("Secrets audit records", TestSecretsAuditRecords);
        failed += Run("Prompt language hints", TestPromptBuilderLanguageHints);
        failed += Run("Prompt language hints disabled", TestPromptBuilderLanguageHintsDisabled);
        failed += Run("Prompt narrative mode structured default", TestPromptBuilderNarrativeModeStructuredDefault);
        failed += Run("Prompt narrative mode freedom", TestPromptBuilderNarrativeModeFreedom);
        failed += Run("Prompt merge blocker sections default", TestPromptBuilderMergeBlockerSectionsDefault);
        failed += Run("Prompt merge blocker sections compact default", TestPromptBuilderMergeBlockerSectionsCompactDefault);
        failed += Run("Prompt includes review history section", TestPromptBuilderIncludesReviewHistorySection);
        failed += Run("Prompt compact history guard includes critical issues",
            TestPromptBuilderCompactHistoryGuardIncludesCriticalIssues);
        failed += Run("Prompt includes ci context section", TestPromptBuilderIncludesCiContextSection);
        failed += Run("Review history builder includes sticky summary and thread snapshot",
            TestReviewHistoryBuilderIncludesStickySummaryAndThreadSnapshot);
        failed += Run("Review history builder builds comment block", TestReviewHistoryBuilderBuildsCommentBlock);
        failed += Run("Review history builder uses latest same-head round",
            TestReviewHistoryBuilderUsesLatestSameHeadRound);
        failed += Run("Review history builder does not resolve across different heads",
            TestReviewHistoryBuilderDoesNotResolveAcrossDifferentHeads);
        failed += Run("Review history builder dedupes latest same-head open findings",
            TestReviewHistoryBuilderDedupesLatestSameHeadOpenFindings);
        failed += Run("Review history builder does not resolve missing finding when latest same-head hits limit",
            TestReviewHistoryBuilderDoesNotResolveMissingFindingWhenLatestSameHeadHitsLimit);
        failed += Run("Review history builder latest same-head uses resolved status precedence",
            TestReviewHistoryBuilderLatestSameHeadUsesResolvedStatusPrecedence);
        failed += Run("Review history builder resolves exact cap when latest round is complete",
            TestReviewHistoryBuilderResolvesExactCapWhenLatestRoundIsComplete);
        failed += Run("Review history builder does not resolve when latest same-head blockers are unparseable",
            TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadBlockersAreUnparseable);
        failed += Run("Review history builder does not resolve when latest same-head blockers are partially unparseable",
            TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadBlockersArePartiallyUnparseable);
        failed += Run("Review history builder does not resolve when parse-incomplete latest round evades blocker detection",
            TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadParseIncompleteWithoutDetectedBlockers);
        failed += Run("Review history builder does not infer resolution from previously resolved duplicate",
            TestReviewHistoryBuilderDoesNotInferResolutionFromPreviouslyResolvedDuplicate);
        failed += Run("Review summary stability drops history progress block",
            TestReviewSummaryStabilityDropsHistoryProgressBlock);
        failed += Run("Review history artifacts render json and markdown",
            TestReviewHistoryArtifactsRenderJsonAndMarkdown);
        failed += Run("Redaction defaults", TestRedactionDefaults);
        failed += Run("Review budget note", TestReviewBudgetNote);
        failed += Run("Review budget note empty", TestReviewBudgetNoteEmpty);
        failed += Run("Review budget note comment", TestReviewBudgetNoteComment);
        failed += Run("Review comment includes history block", TestReviewCommentIncludesHistoryBlock);
        failed += Run("Combine notes", TestCombineNotes);
        failed += Run("Review retry backoff multiplier config", TestReviewRetryBackoffMultiplierConfig);
        failed += Run("Review retry backoff multiplier env", TestReviewRetryBackoffMultiplierEnv);
        failed += Run("Prepare files max files zero", TestPrepareFilesMaxFilesZero);
        failed += Run("Prepare files max files negative", TestPrepareFilesMaxFilesNegative);
        failed += Run("Azure DevOps changes pagination", TestAzureDevOpsChangesPagination);
        failed += Run("Azure DevOps diff note zero iterations", TestAzureDevOpsDiffNoteZeroIterations);
        failed += Run("Azure DevOps inline patch line map parses added lines", TestAzureDevOpsInlinePatchLineMapParsesAddedLines);
        failed += Run("Azure DevOps inline patch line map handles CRLF and deletions", TestAzureDevOpsInlinePatchLineMapHandlesCrlfAndDeletions);
        failed += Run("Azure DevOps inline patch line map handles ++/-- content", TestAzureDevOpsInlinePatchLineMapHandlesPlusPlusAndDashDashContent);
        failed += Run("Azure DevOps inline threadContext positions", TestAzureDevOpsInlineThreadContextUsesOneBasedLineAndZeroBasedOffset);
        failed += Run("Azure DevOps error sanitization", TestAzureDevOpsErrorSanitization);
        failed += Run("Context deny invalid regex", TestContextDenyInvalidRegex);
        failed += Run("Context deny timeout", TestContextDenyTimeout);
        failed += Run("Review summary parser", TestReviewSummaryParser);
        failed += Run("Review summary parser finding extraction", TestReviewSummaryParserFindingExtraction);
        failed += Run("Review summary parser merge blocker detection", TestReviewSummaryParserMergeBlockerDetection);
        failed += Run("Review summary parser ignores starred prose for parse incomplete",
            TestReviewSummaryParserIgnoresStarredProseForParseIncomplete);
        failed += Run("Review summary parser keeps flexible starred checklist parse incomplete",
            TestReviewSummaryParserKeepsFlexibleStarredChecklistAsParseIncomplete);
        failed += Run("Review summary parser keeps plain starred bullet parse incomplete",
            TestReviewSummaryParserKeepsPlainStarredBulletAsParseIncomplete);
        failed += Run("Review summary parser keeps compact starred checklist parse incomplete",
            TestReviewSummaryParserKeepsCompactStarredChecklistAsParseIncomplete);
        failed += Run("Review summary parser merge blocker detection inline section labels",
            TestReviewSummaryParserMergeBlockerDetectionInlineSectionLabels);
        failed += Run("Review summary parser merge blocker detection heading inline section labels",
            TestReviewSummaryParserMergeBlockerDetectionHeadingInlineSectionLabels);
        failed += Run("Review summary parser merge blocker detection no-space heading prefixes",
            TestReviewSummaryParserMergeBlockerDetectionNoSpaceHeadingPrefixes);
        failed += Run("Review summary parser ignores checklist inside long fence code blocks",
            TestReviewSummaryParserIgnoresChecklistInsideLongFenceCodeBlocks);
        failed += Run("Review summary parser ignores checklist inside long fence code blocks with longer closer",
            TestReviewSummaryParserIgnoresChecklistInsideLongFenceCodeBlocksWithLongerCloser);
        failed += Run("Review summary parser merge blocker detection compact defaults",
            TestReviewSummaryParserMergeBlockerDetectionCompactDefaults);
        failed += Run("Review summary parser merge blocker detection compact aliases",
            TestReviewSummaryParserMergeBlockerDetectionCompactAliases);
        failed += Run("Review summary parser merge blocker detection custom sections",
            TestReviewSummaryParserMergeBlockerDetectionCustomSections);
        failed += Run("Review swarm shadow plan uses reviewer overrides", TestReviewSwarmShadowPlanUsesReviewerOverrides);
        failed += Run("Review swarm shadow plan uses agent profiles", TestReviewSwarmShadowPlanUsesAgentProfiles);
        failed += Run("Review agent profile switch rebases to baseline",
            TestReviewAgentProfileSwitchRebasesToBaseline);
        failed += Run("Review swarm shadow clone uses refreshed runtime baseline",
            TestReviewSwarmShadowCloneUsesRefreshedRuntimeBaseline);
        failed += Run("Review swarm shadow plan falls back to primary provider and model",
            TestReviewSwarmShadowPlanFallsBackToPrimaryProviderAndModel);
        failed += Run("Review swarm shadow reviewer prompt shapes focus", TestReviewSwarmShadowReviewerPromptShapesFocus);
        failed += Run("Review swarm shadow runner captures failures", TestReviewSwarmShadowRunnerCapturesFailures);
        failed += Run("Review swarm shadow runner fails closed when configured",
            TestReviewSwarmShadowRunnerFailsClosedWhenConfigured);
        failed += Run("Review swarm shadow runner honors max parallel",
            TestReviewSwarmShadowRunnerHonorsMaxParallel);
        failed += Run("Review swarm shadow aggregator prompt includes subreviews",
            TestReviewSwarmShadowAggregatorPromptIncludesSubreviews);
        failed += Run("Review swarm shadow aggregator prompt uses safe subreview fence",
            TestReviewSwarmShadowAggregatorPromptUsesSafeSubreviewFence);
        failed += Run("Review swarm shadow aggregator prompt closes truncated fence",
            TestReviewSwarmShadowAggregatorPromptClosesTruncatedFence);
        failed += Run("Review swarm shadow aggregator prompt keeps context with large base",
            TestReviewSwarmShadowAggregatorPromptKeepsContextWithLargeBase);
        failed += Run("Review swarm shadow artifacts render json and markdown",
            TestReviewSwarmShadowArtifactsRenderJsonAndMarkdown);
        failed += Run("Review swarm shadow artifacts render metrics json line",
            TestReviewSwarmShadowArtifactsRenderMetricsJsonLine);
        failed += Run("Review summary parser merge blocker detection allow missing section match",
            TestReviewSummaryParserMergeBlockerDetectionAllowNoSectionMatch);
        failed += Run("Review formatter model usage section", TestReviewFormatterModelUsageSection);
        failed += Run("Review formatter model usage unavailable", TestReviewFormatterModelUsageUnavailable);
        failed += Run("Review formatter copilot model usage provider display", TestReviewFormatterCopilotModelUsageUsesProviderDisplay);
        failed += Run("Review formatter golden snapshot", TestReviewFormatterGoldenSnapshot);
        failed += Run("Review formatter normalizes inline section labels", TestReviewFormatterNormalizesInlineSectionLabels);
        failed += Run("Review formatter normalizes malformed heading inline section labels",
            TestReviewFormatterNormalizesMalformedHeadingInlineSectionLabels);
        failed += Run("Review formatter normalizes no-separator section labels",
            TestReviewFormatterNormalizesNoSeparatorSectionLabels);
        failed += Run("Review formatter preserves section labels inside code blocks",
            TestReviewFormatterDoesNotNormalizeSectionLabelsInsideCodeBlocks);
        failed += Run("Review formatter preserves section labels inside long fence code blocks",
            TestReviewFormatterPreservesSectionLabelsInsideLongFenceCodeBlocks);
        failed += Run("Review formatter allows longer fence closers",
            TestReviewFormatterAllowsLongerFenceClosers);
        failed += Run("Review formatter ignores indented fence-like lines",
            TestReviewFormatterIgnoresIndentedFenceLikeLines);
        failed += Run("Review usage integration display", TestReviewUsageIntegrationDisplay);
        failed += Run("Review usage summary line", TestReviewUsageSummaryLine);
        failed += Run("Review Claude usage summary line", TestReviewClaudeUsageSummaryLine);
        failed += Run("Review usage summary includes weekly window", TestReviewUsageSummaryIncludesWeeklyWindow);
        failed += Run("Review usage summary uses secondary fallback label", TestReviewUsageSummaryUsesSecondaryFallbackLabel);
        failed += Run("Review usage budget guard blocks exhausted credits and weekly", TestReviewUsageBudgetGuardBlocksWhenCreditsAndWeeklyExhausted);
        failed += Run("Review usage budget guard allows credits fallback", TestReviewUsageBudgetGuardAllowsCreditsFallback);
        failed += Run("Review Claude usage budget guard blocks when weekly exhausted",
            TestReviewClaudeUsageBudgetGuardBlocksWhenWeeklyExhausted);
        failed += Run("Review Claude usage budget guard allows remaining weekly",
            TestReviewClaudeUsageBudgetGuardAllowsRemainingWeekly);
        failed += Run("Review usage budget guard blocks when no budget sources are allowed",
            TestReviewUsageBudgetGuardBlocksWhenNoBudgetSourcesAllowed);
        return failed;
#else
        return 0;
#endif
    }
}
