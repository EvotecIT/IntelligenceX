namespace IntelligenceX.Tests;

internal static partial class Program {
    private static int Main() {
        var failed = 0;
        failed += Run("Parse basic object", TestParseBasicObject);
        failed += Run("Serialize roundtrip", TestSerializeRoundtrip);
        failed += Run("Escape handling", TestEscapeHandling);
        failed += Run("RPC malformed JSON", TestRpcMalformedJson);
        failed += Run("RPC unknown shape", TestRpcUnknownShape);
        failed += Run("RPC notification", TestRpcNotification);
        failed += Run("RPC error hints", TestRpcErrorHints);
        failed += Run("RPC call cancellation cleans pending", TestRpcCallCancellationCleansPending);
        failed += Run("RPC call send failure cleans pending", TestRpcCallSendFailureCleansPending);
        failed += Run("Header transport message", TestHeaderTransportMessage);
        failed += Run("Header transport truncated", TestHeaderTransportTruncated);
        failed += Run("Config load invalid JSON", TestConfigLoadInvalidJsonThrows);
        failed += Run("Copilot idle event", TestCopilotIdleEvent);
        failed += Run("ChatGPT usage parse", TestChatGptUsageParse);
        failed += Run("ChatGPT usage cache invalid JSON", TestChatGptUsageCacheInvalidJson);
        failed += Run("Tool call parsing", TestToolCallParsing);
        failed += Run("Tool call invalid JSON", TestToolCallParsingInvalidJson);
        failed += Run("Tool output input", TestToolOutputInput);
        failed += Run("Turn response_id parsing", TestTurnResponseIdParsing);
        failed += Run("Tool definitions ordered", TestToolDefinitionOrdering);
        failed += Run("Tool runner max rounds", TestToolRunnerMaxRounds);
        failed += Run("Tool runner unregistered tool", TestToolRunnerUnregisteredTool);
        failed += Run("Tool runner parallel execution", TestToolRunnerParallelExecution);
        failed += Run("Ensure ChatGPT login uses cache", TestEnsureChatGptLoginUsesCache);
        failed += Run("Ensure ChatGPT login triggers when missing", TestEnsureChatGptLoginTriggersLoginWhenMissing);
        failed += Run("Ensure ChatGPT login force triggers", TestEnsureChatGptLoginForceTriggersLogin);
        failed += Run("Ensure ChatGPT login cancellation propagates", TestEnsureChatGptLoginCancellationPropagates);
#if NET8_0_OR_GREATER
        failed += Run("Auth store invalid key throws", TestAuthStoreInvalidKeyThrows);
        failed += Run("Auth store encrypted roundtrip", TestAuthStoreEncryptedRoundtrip);
        failed += Run("Path safety blocks symlink traversal", TestPathSafetyBlocksSymlinkTraversal);
#endif
        failed += Run("Native tool schema fallback detects tools[n]", TestNativeToolSchemaFallbackDetectsIndex);
        failed += Run("Native tool schema fallback detects tools.n", TestNativeToolSchemaFallbackDetectsDotIndex);
        failed += Run("Native tool_choice matches wire format", TestNativeToolChoiceSerializationMatchesWireFormat);
        failed += Run("Native tool schema fallback handles AggregateException", TestNativeToolSchemaFallbackHandlesAggregateException);
        failed += Run("Native tool schema fallback uses structured error data", TestNativeToolSchemaFallbackUsesStructuredErrorData);
        failed += Run("Native tool schema fallback ignores unrelated", TestNativeToolSchemaFallbackIgnoresUnrelated);
        failed += Run("Native tool schema serialization switches field name", TestNativeToolSchemaSerializationSwitchesFieldName);
        failed += Run("Native request body omits previous_response_id", TestNativeRequestBodyOmitsPreviousResponseId);
#if !NET472
        failed += Run("Setup args reject skip+update", TestSetupArgsRejectSkipUpdate);
        failed += Run("Setup args include analysis options", TestSetupArgsIncludeAnalysisOptions);
        failed += Run("Setup args include analysis export path", TestSetupArgsIncludeAnalysisExportPath);
        failed += Run("Setup args disable analysis omits gate and packs", TestSetupArgsDisableAnalysisOmitsGateAndPacks);
        failed += Run("Setup analysis export path normalization", TestSetupAnalysisExportPathNormalization);
        failed += Run("Setup analysis export path combine rejects rooted file name", TestSetupAnalysisExportPathCombineRejectsRootedFileName);
        failed += Run("Setup analysis export catalog prereq validation", TestSetupAnalysisExportCatalogPrereqValidation);
        failed += Run("Setup analysis export duplicate target detection", TestSetupAnalysisExportDuplicateTargetDetection);
        failed += Run("Setup analysis disable writes enabled=false", TestSetupAnalysisDisableWritesFalse);
        failed += Run("Setup analysis defaults packs to all-50", TestSetupAnalysisDefaultsPacksToAll50);
        failed += Run("Setup config build honors analysis gate", TestSetupBuildConfigJsonHonorsAnalysisGateOnNewConfig);
        failed += Run("Setup config merge preserves review settings when enabling analysis", TestSetupBuildConfigJsonMergePreservesReviewSettingsWhenEnablingAnalysis);
        failed += Run("Setup workflow upgrade preserves custom sections outside managed block",
            TestSetupWorkflowUpgradePreservesCustomSectionsOutsideManagedBlock);
        failed += Run("Setup post-apply verify passes for managed setup", TestSetupPostApplyVerifySetupPassesWithManagedWorkflowAndSecret);
        failed += Run("Setup post-apply verify detects residual cleanup config", TestSetupPostApplyVerifyCleanupDetectsResidualConfig);
        failed += Run("Setup post-apply verify allows unknown branch state when PR exists",
            TestSetupPostApplyVerifySetupAllowsUnknownBranchStateWithPr);
        failed += Run("Setup post-apply verify unauthorized secret lookup fails deterministically",
            TestSetupPostApplyVerifySecretLookupUnauthorizedFailsDeterministically);
        failed += Run("Wizard post-apply verify skips callback on failed apply",
            TestWizardPostApplyVerifySkipsCallbackWhenApplyFails);
        failed += Run("CLI dispatch no-args interactive runs manage", TestCliDispatchNoArgsInteractiveRunsManage);
        failed += Run("CLI dispatch no-args non-interactive shows help", TestCliDispatchNoArgsNonInteractiveShowsHelp);
        failed += Run("CLI dispatch manage command routes to manage", TestCliDispatchManageCommandRoutesToManage);
        failed += Run("CLI dispatch no-args manage failure fallback", TestCliDispatchNoArgsManageFailureShowsFallbackError);
        failed += Run("CLI dispatch manage command failure fallback", TestCliDispatchManageCommandFailureShowsFallbackError);
        failed += Run("CLI dispatch manage command unexpected failure fallback", TestCliDispatchManageCommandUnexpectedFailureShowsFallbackError);
        failed += Run("CLI dispatch detailed error flags", TestCliDispatchDetailedErrorFlagParsing);
        failed += Run("Resolve default repo normalizes env value", TestResolveDefaultRepoNormalizesEnvironmentValue);
        failed += Run("Manage external command timeout returns promptly", TestManageRunExternalCommandTimeoutReturnsPromptly);
        failed += Run("Manage external command captures help tail line", TestManageRunExternalCommandCapturesHelpTailLine);
        failed += Run("Manage external command start failure returns promptly", TestManageRunExternalCommandStartFailureReturnsPromptly);
        failed += Run("Manage external command non-timeout failure is not timeout", TestManageRunExternalCommandNonTimeoutFailureIsNotTimeout);
        failed += Run("Web setup args propagate request dry-run", TestWebSetupBuildSetupArgsPropagatesRequestDryRun);
        failed += Run("Web setup resolves with-config from args", TestWebSetupResolveWithConfigFromArgs);
        failed += Run("Web setup post-apply verify skips callback on failed apply",
            TestWebSetupPostApplyVerifySkipsCallbackWhenApplyFails);
        failed += Run("Web setup resolves org-secret verification context", TestWebSetupResolveOrgSecretVerificationContext);
        failed += Run("Web setup resolves org-secret verification context per repo", TestWebSetupResolveOrgSecretVerificationContextPerRepo);
        failed += Run("Web setup subprocess timeout returns promptly", TestWebSetupRunProcessTimeoutReturnsPromptly);
        failed += Run("Manage GitHub CLI status token authenticated", TestManageGitHubCliStatusWithTokenIsAuthenticated);
        failed += Run("Manage GitHub CLI status exit code zero authenticated", TestManageGitHubCliStatusExitCodeZeroAuthenticated);
        failed += Run("Manage GitHub CLI status exit code non-zero unauthenticated", TestManageGitHubCliStatusExitCodeNonZeroUnauthenticated);
        failed += Run("Manage GitHub CLI status missing CLI", TestManageGitHubCliStatusMissingCli);
        failed += Run("GitHub repo detector parses remote urls", TestGitHubRepoDetectorParsesRemoteUrls);
        failed += Run("GitHub repo detector parses git config sections", TestGitHubRepoDetectorParsesGitConfigRemoteSection);
        failed += Run("GitHub repo client secret lookup maps status codes", TestGitHubRepoClientSecretLookupMapsStatusCodes);
        failed += Run("GitHub repo client secret lookup maps client exceptions", TestGitHubRepoClientSecretLookupMapsClientExceptions);
        failed += Run("GitHub repo client secret lookup cancellation propagates", TestGitHubRepoClientSecretLookupCancellationPropagates);
        failed += Run("GitHub repo client file fetch cancellation propagates", TestGitHubRepoClientFileFetchCancellationPropagates);
        failed += Run("GitHub repo client file fetch invalid base64 returns null", TestGitHubRepoClientFileFetchInvalidBase64ReturnsNull);
        failed += Run("GitHub secrets reject empty value", TestGitHubSecretsRejectEmptyValue);
        failed += Run("Release reviewer env token", TestReleaseReviewerEnvToken);
        failed += Run("CI path safety rejects non-existent directory leaf", TestCiPathSafetyUnderRootPhysicalRejectsNonexistentDirectoryLeaf);
        failed += Run("CI path safety ensure-safe-dir allows new leaf", TestCiPathSafetyTryEnsureSafeDirectoryAllowsNewDirectoryLeaf);
        failed += Run("CI path safety handles trailing separators", TestCiPathSafetyUnderRootPhysicalTrailingSeparators);
        failed += Run("CI path safety rejects symlink traversal", TestCiPathSafetyUnderRootPhysicalRejectsSymlinkTraversal);
        failed += Run("CI path safety allows nested non-existent segments", TestCiPathSafetyUnderRootPhysicalAllowsNestedNonexistentSegments);
        failed += Run("CI changed-files writes into new output directory", TestCiChangedFilesWritesIntoNewDirectory);
        failed += Run("CI changed-files strict fails on diff failure even with fallback", TestCiChangedFilesStrictFailsWhenDiffFailsEvenIfFallbackSucceeds);
        failed += Run("CI tune-reviewer-budgets rejects out-env outside workspace", TestCiTuneReviewerBudgetsRejectsOutEnvOutsideWorkspaceWhenGitHubEnvMissing);
        failed += Run("Reviewer GraphQL mutation detection", TestReviewerGraphQlMutationDetection);
#endif

        // Reviewer tests are excluded from NET472 builds (no reviewer references there), and enforced for non-NET472
        // builds via `IntelligenceX.Tests/ReviewerSymbolGuard.cs` + `IntelligenceX.Tests/IntelligenceX.Tests.csproj`.
#if INTELLIGENCEX_REVIEWER
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
        failed += Run("Thread assessment evidence parse", TestThreadAssessmentEvidenceParse);
        failed += Run("Thread triage fallback summary", TestThreadTriageFallbackSummary);
        failed += Run("Review thread inline key allowlist", TestReviewThreadInlineKeyAllowlist);
        failed += Run("Thread auto-resolve summary comment", TestThreadAutoResolveSummaryComment);
        failed += Run("Thread triage embed placement", TestThreadTriageEmbedPlacement);
        failed += Run("Thread assessment prompt smoke", TestThreadAssessmentPromptSmoke);
        failed += Run("Auto-resolve stale threads smoke", TestAutoResolveStaleThreadsSmoke);
        failed += Run("Auto-resolve missing inline empty keys", TestAutoResolveMissingInlineEmptyKeys);
        failed += Run("Resolve thread payload parser rejects invalid JSON", TestResolveThreadPayloadParserRejectsInvalidJson);
        failed += Run("Auto-resolve missing inline gate empty set", TestAutoResolveMissingInlineGateAllowsEmptySet);
        failed += Run("Auto-resolve missing inline gate null set", TestAutoResolveMissingInlineGateRejectsNull);
        failed += Run("Auto-resolve missing inline gate empty mapped keys", TestAutoResolveMissingInlineGateRejectsEmptyWhenInlineCommentsPresent);
        failed += Run("Review retry transient", TestReviewRetryTransient);
        failed += Run("Review retry non-transient", TestReviewRetryNonTransient);
        failed += Run("Review retry rethrows", TestReviewRetryRethrows);
        failed += Run("Review retry extra attempt", TestReviewRetryExtraAttempt);
        failed += Run("Review failure marker", TestReviewFailureMarker);
        failed += Run("Review failure body redacts errors", TestReviewFailureBodyRedactsErrors);
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
        failed += Run("Analysis catalog validator detects invalid catalog", TestAnalysisCatalogValidatorDetectsInvalidCatalog);
        failed += Run("Analysis catalog validator detects missing rule metadata", TestAnalysisCatalogValidatorDetectsMissingRuleMetadata);
        failed += Run("Analysis packs: all-security includes PowerShell", TestAnalysisPacksAllSecurityIncludesPowerShell);
        failed += Run("Analysis packs: powershell-default resolves", TestAnalysisPacksPowerShellDefaultResolves);
        failed += Run("Analysis packs: powershell-50 resolves to 50 rules", TestAnalysisPacksPowerShell50ResolvesTo50Rules);
        failed += Run("Analysis catalog rule overrides apply", TestAnalysisCatalogRuleOverridesApply);
        failed += Run("Analysis catalog PowerShell overrides apply", () => TestAnalysisCatalogPowerShellOverridesApply());
        failed += Run("Analysis catalog PowerShell docs links", TestAnalysisCatalogPowerShellDocsLinksMatchLearnPattern);
        failed += Run("Analysis catalog override invalid type falls back", TestAnalysisCatalogOverrideInvalidTypeFallsBack);
        failed += Run("Analysis catalog validator rejects dangling override", TestAnalysisCatalogValidatorRejectsDanglingOverride);
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
        failed += Run("Analyze hotspots sync-state writes state file", TestAnalyzeHotspotsSyncStateWritesStateFile);
        failed += Run("Analyze hotspots help has no side effects", TestAnalyzeHotspotsHelpHasNoSideEffects);
        failed += Run("Analyze hotspots state path is workspace-bound", TestAnalyzeHotspotsStatePathIsWorkspaceBound);
        failed += Run("Analyze validate-catalog command", TestAnalyzeValidateCatalogCommand);
        failed += Run("Analyze list-rules markdown format", TestAnalyzeListRulesMarkdownFormat);
        failed += Run("Analyze list-rules json with pack filter", TestAnalyzeListRulesJsonWithPackFilter);
        failed += Run("Analyze list-rules tier counts", TestAnalyzeListRulesTierCounts);
        failed += Run("Analyze list-rules invalid format", TestAnalyzeListRulesInvalidFormat);
        failed += Run("Analyze list-rules help", TestAnalyzeListRulesHelp);
        failed += Run("Analyze list-rules json warnings to stderr", TestAnalyzeListRulesJsonWarningsToStderr);
        failed += Run("Analyze list-rules json empty outputs array", TestAnalyzeListRulesJsonEmptyOutputsArray);
        failed += Run("Analyze gate disabled skips", TestAnalyzeGateDisabledSkips);
        failed += Run("Analyze gate fails on violations", TestAnalyzeGateFailsOnViolations);
        failed += Run("Analyze gate passes on clean", TestAnalyzeGatePassesOnClean);
        failed += Run("Analyze gate fails on no enabled rules", TestAnalyzeGateFailsOnNoEnabledRules);
        failed += Run("Analyze gate minSeverity filters", TestAnalyzeGateMinSeverityFilters);
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
        failed += Run("Analyze gate hotspot state path bound", TestAnalyzeGateHotspotsStatePathIsWorkspaceBound);
        failed += Run("Analyze gate help token", TestAnalyzeGateHelpToken);
        failed += Run("Doctor help", TestDoctorHelp);
        failed += Run("Doctor missing auth store fails", TestDoctorMissingAuthStoreFails);
        failed += Run("Doctor multiple bundles warns", TestDoctorMultipleBundlesWarns);
        failed += Run("Todo help", TestTodoHelp);
        failed += Run("Todo unknown command", TestTodoUnknownCommandShowsMessage);
        failed += Run("Todo bot feedback render LF", TestBotFeedbackRenderHonorsLfNewlines);
        failed += Run("Todo bot feedback parse existing", TestBotFeedbackParseExistingPrBlockExtractsTasks);
        failed += Run("Todo bot feedback merge", TestBotFeedbackMergePreservesManualCheckedStateAndOrder);
        failed += Run("Todo bot feedback update section", TestBotFeedbackUpdateSectionIsDeterministicAndNoDuplicates);
        failed += Run("Analyze run PowerShell script captures engine errors", TestAnalyzeRunPowerShellScriptCapturesEngineErrors);
        failed += Run("Analyze run PowerShell strict args include fail switch", TestAnalyzeRunPowerShellStrictArgsIncludeFailSwitch);
        failed += Run("Analyze run disabled writes empty findings", TestAnalyzeRunDisabledWritesEmptyFindings);
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
        failed += Run("Analyze run internal file size newline variants",
            TestAnalyzeRunInternalFileSizeRuleHandlesLineEndings);
        failed += Run("Analyze run internal malformed tags warn and fallback",
            TestAnalyzeRunInternalFileSizeRuleWarnsOnMalformedTags);
        failed += Run("Analyze run internal unknown tag prefixes warn",
            TestAnalyzeRunInternalFileSizeRuleWarnsOnUnknownTagPrefixes);
        failed += Run("Analyze run internal generated header scan can be disabled",
            TestAnalyzeRunInternalFileSizeRuleGeneratedHeaderScanCanBeDisabled);
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
        failed += Run("Review provider fallback env", TestReviewProviderFallbackEnv);
        failed += Run("Review provider fallback config", TestReviewProviderFallbackConfig);
        failed += Run("Review provider fallback plan", TestReviewProviderFallbackPlan);
        failed += Run("Review provider health env", TestReviewProviderHealthEnv);
        failed += Run("Review provider health config", TestReviewProviderHealthConfig);
        failed += Run("Review provider circuit breaker", TestReviewProviderCircuitBreaker);
        failed += Run("Review intent applies defaults", TestReviewIntentAppliesDefaults);
        failed += Run("Review intent respects settings", TestReviewIntentRespectsSettings);
        failed += Run("Review intent perf alias", TestReviewIntentPerfAlias);
        failed += Run("Review intent null settings", TestReviewIntentNullSettings);
        failed += Run("Triage-only loads threads", TestTriageOnlyLoadsThreads);
        failed += Run("Review code host env", TestReviewCodeHostEnv);
        failed += Run("GitHub context cache", TestGitHubContextCache);
        failed += Run("GitHub concurrency env", TestGitHubConcurrencyEnv);
        failed += Run("GitHub client concurrency", TestGitHubClientConcurrency);
        failed += Run("GitHub code host reader smoke", TestGitHubCodeHostReaderSmoke);
        failed += Run("GitHub compare truncation", TestGitHubCompareTruncation);
        failed += Run("Diff range compare truncation", TestDiffRangeCompareTruncation);
        failed += Run("Azure auth scheme env", TestAzureAuthSchemeEnv);
        failed += Run("Azure auth scheme invalid env", TestAzureAuthSchemeInvalidEnv);
        failed += Run("Review settings defaults and env merge", TestReviewSettingsDefaultsAndEnvMerge);
        failed += Run("Review settings load config then env precedence", TestReviewSettingsLoadConfigThenEnvPrecedence);
        failed += Run("Review settings policy preview clamp range", TestReviewSettingsPolicyRulePreviewConfigClampRange);
        failed += Run("Azure code host reader smoke", TestAzureDevOpsCodeHostReaderSmoke);
        failed += Run("Review threads diff range normalize", TestReviewThreadsDiffRangeNormalize);
        failed += Run("Copilot env allowlist config", TestCopilotEnvAllowlistConfig);
        failed += Run("Copilot inherit env default", TestCopilotInheritEnvironmentDefault);
        failed += Run("Copilot direct timeout validation", TestCopilotDirectTimeoutValidation);
        failed += Run("Copilot chat timeout validation", TestCopilotChatTimeoutValidation);
        failed += Run("Copilot direct auth conflict", TestCopilotDirectAuthorizationConflict);
        failed += Run("Copilot CLI path requires env", TestCopilotCliPathRequiresEnvironment);
        failed += Run("Copilot CLI path optional with url", TestCopilotCliPathOptionalWithUrl);
        failed += Run("Copilot CLI url validation", TestCopilotCliUrlValidation);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
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
        failed += Run("Redaction defaults", TestRedactionDefaults);
        failed += Run("Review budget note", TestReviewBudgetNote);
        failed += Run("Review budget note empty", TestReviewBudgetNoteEmpty);
        failed += Run("Review budget note comment", TestReviewBudgetNoteComment);
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
        failed += Run("Review formatter model usage section", TestReviewFormatterModelUsageSection);
        failed += Run("Review formatter model usage unavailable", TestReviewFormatterModelUsageUnavailable);
        failed += Run("Review formatter golden snapshot", TestReviewFormatterGoldenSnapshot);
        failed += Run("Review usage integration display", TestReviewUsageIntegrationDisplay);
        failed += Run("Review usage summary line", TestReviewUsageSummaryLine);
        failed += Run("Review usage summary disambiguates code review weekly", TestReviewUsageSummaryDisambiguatesCodeReviewWeekly);
        failed += Run("Review usage summary disambiguates code review weekly secondary", TestReviewUsageSummaryDisambiguatesCodeReviewWeeklySecondary);
        failed += Run("Review usage summary prefixes non-weekly code review", TestReviewUsageSummaryPrefixesNonWeeklyCodeReview);
#endif

        Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
