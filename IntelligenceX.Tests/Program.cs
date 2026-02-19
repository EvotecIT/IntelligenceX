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
        failed += Run("ChatGPT usage cache account path", TestChatGptUsageCacheAccountPath);
        failed += Run("ChatGPT usage cache directory override path", TestChatGptUsageCacheDirectoryOverridePath);
        failed += Run("ChatGPT usage cache trailing separator override path",
            TestChatGptUsageCacheTrailingSeparatorOverridePath);
        failed += Run("EasySession forwards auth account id", TestEasySessionBuildClientOptionsCarriesAuthAccountId);
#if !NET472
        failed += Run("Usage options parse account id", TestUsageOptionsParseAccountId);
        failed += Run("Usage options parse by-surface", TestUsageOptionsParseBySurface);
        failed += Run("Usage surface summary json buckets", TestUsageSurfaceSummaryJsonBuckets);
        failed += Run("CLI auth sync-codex help options", TestCliAuthSyncCodexHelpSupportsOptions);
        failed += Run("CLI auth sync-codex missing provider value shows help", TestCliAuthSyncCodexMissingProviderValueShowsHelp);
        failed += Run("CLI models help routes", TestCliModelsHelpRoutes);
#endif
        failed += Run("Tool call parsing", TestToolCallParsing);
        failed += Run("Tool call invalid JSON", TestToolCallParsingInvalidJson);
        failed += Run("Tool output input", TestToolOutputInput);
#if !NET472
        failed += Run("Tool output envelope error omits meta when null", TestToolOutputEnvelopeErrorOmitsMetaWhenNull);
        failed += Run("Tool output envelope error includes meta when provided", TestToolOutputEnvelopeErrorIncludesMetaWhenProvided);
        failed += Run("Tool output envelope error string includes meta when provided", TestToolOutputEnvelopeErrorStringIncludesMetaWhenProvided);
        failed += Run("Tool output envelope error includes failure object", TestToolOutputEnvelopeErrorIncludesFailureObject);
        failed += Run("Tool output envelope error string includes failure object", TestToolOutputEnvelopeErrorStringIncludesFailureObject);
#endif
        failed += Run("Turn response_id parsing", TestTurnResponseIdParsing);
        failed += Run("Turn usage parsing", TestTurnUsageParsing);
        failed += Run("Thread usage summary parsing", TestThreadUsageSummaryParsing);
        failed += Run("Native thread state usage accumulation", TestNativeThreadStateUsageAccumulation);
        failed += Run("Tool definitions ordered", TestToolDefinitionOrdering);
        failed += Run("Tool definition alias merges tags", TestToolDefinitionAliasMergesTags);
        failed += Run("Tool registry registers aliases from definition", TestToolRegistryRegistersAliasesFromDefinition);
        failed += Run("Tool registry register alias override", TestToolRegistryRegisterAliasWithOverrides);
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
        failed += Run("Auth store decrypt with explicit key override", TestAuthStoreDecryptWithExplicitKeyOverride);
        failed += Run("Path safety blocks symlink traversal", TestPathSafetyBlocksSymlinkTraversal);
#endif
        failed += Run("Native tool schema fallback detects tools[n]", TestNativeToolSchemaFallbackDetectsIndex);
        failed += Run("Native tool schema fallback detects tools.n", TestNativeToolSchemaFallbackDetectsDotIndex);
        failed += Run("Native tool_choice matches wire format", TestNativeToolChoiceSerializationMatchesWireFormat);
        failed += Run("Native tool schema fallback handles AggregateException", TestNativeToolSchemaFallbackHandlesAggregateException);
        failed += Run("Native tool schema fallback uses structured error data", TestNativeToolSchemaFallbackUsesStructuredErrorData);
        failed += Run("Native tool schema fallback ignores unrelated", TestNativeToolSchemaFallbackIgnoresUnrelated);
        failed += Run("Native tool schema fallback retries missing tool name", TestNativeToolSchemaFallbackRetriesOnMissingToolName);
        failed += Run("Native tool schema serialization switches field name", TestNativeToolSchemaSerializationSwitchesFieldName);
        failed += Run("Native tool schema serialization includes tags in description", TestNativeToolSchemaSerializationIncludesTagsInDescription);
        failed += Run("Native request body omits previous_response_id", TestNativeRequestBodyOmitsPreviousResponseId);
        failed += Run("Native request body normalizes tools/tool_choice", TestNativeRequestBodyNormalizesToolsAndToolChoice);
#if !NET472
        failed += Run("Setup args reject skip+update", TestSetupArgsRejectSkipUpdate);
        failed += Run("Setup args include analysis options", TestSetupArgsIncludeAnalysisOptions);
        failed += Run("Setup args include analysis run strict option", TestSetupArgsIncludeAnalysisRunStrictOption);
        failed += Run("Setup args include analysis export path", TestSetupArgsIncludeAnalysisExportPath);
        failed += Run("Setup args disable analysis omits gate and packs", TestSetupArgsDisableAnalysisOmitsGateAndPacks);
        failed += Run("Setup args include triage bootstrap", TestSetupArgsIncludeTriageBootstrap);
        failed += Run("Setup triage control issue provision decision", TestSetupTriageControlIssueProvisionDecision);
        failed += Run("Setup project view apply issue provision decision", TestSetupProjectViewApplyIssueProvisionDecision);
        failed += Run("Setup triage bootstrap links comment includes assistive issue links",
            TestSetupTriageBootstrapLinksCommentIncludesAssistiveIssueLinks);
        failed += Run("Setup triage bootstrap links comment handles missing view issue",
            TestSetupTriageBootstrapLinksCommentHandlesMissingViewIssue);
        failed += Run("Setup triage bootstrap links comment handles label ensure failure",
            TestSetupTriageBootstrapLinksCommentHandlesLabelEnsureFailure);
        failed += Run("Setup args include OpenAI account routing", TestSetupArgsIncludeOpenAiAccountRouting);
        failed += Run("Setup args include OpenAI account routing with primary only",
            TestSetupArgsIncludeOpenAiAccountRoutingWithPrimaryOnly);
        failed += Run("Setup config rejects invalid OpenAI account rotation", TestSetupConfigRejectsInvalidOpenAiAccountRotation);
        failed += Run("Setup config rejects analysis strict without analysis enabled",
            TestSetupConfigRejectsAnalysisStrictWithoutAnalysisEnabled);
        failed += Run("Setup config rejects analysis options with config override",
            TestSetupConfigRejectsAnalysisOptionsWithConfigOverride);
        failed += Run("Setup config rejects analysis options without with-config",
            TestSetupConfigRejectsAnalysisOptionsWithoutWithConfig);
        failed += Run("Setup config rejects invalid analysis pack id",
            TestSetupConfigRejectsInvalidAnalysisPackId);
        failed += Run("Setup config merge rejects invalid OpenAI account rotation from snapshot",
            TestSetupConfigMergeRejectsInvalidOpenAiAccountRotationFromSnapshot);
        failed += Run("Setup analysis export path normalization", TestSetupAnalysisExportPathNormalization);
        failed += Run("Setup analysis export path combine rejects rooted file name", TestSetupAnalysisExportPathCombineRejectsRootedFileName);
        failed += Run("Setup analysis export catalog prereq validation", TestSetupAnalysisExportCatalogPrereqValidation);
        failed += Run("Setup analysis export duplicate target detection", TestSetupAnalysisExportDuplicateTargetDetection);
        failed += Run("Setup analysis disable writes enabled=false", TestSetupAnalysisDisableWritesFalse);
        failed += Run("Setup analysis defaults packs to all-50", TestSetupAnalysisDefaultsPacksToAll50);
        failed += Run("Setup config build honors analysis gate", TestSetupBuildConfigJsonHonorsAnalysisGateOnNewConfig);
        failed += Run("Setup config build includes analysis run strict", TestSetupBuildConfigJsonIncludesAnalysisRunStrict);
        failed += Run("Setup config build includes OpenAI account routing", TestSetupBuildConfigJsonIncludesOpenAiAccountRouting);
        failed += Run("Setup config normalizes OpenAI primary into account ids",
            TestSetupBuildConfigJsonNormalizesOpenAiPrimaryInAccountIds);
        failed += Run("Setup config build persists OpenAI routing with primary only",
            TestSetupBuildConfigJsonPersistsOpenAiRoutingWithPrimaryOnly);
        failed += Run("Setup config merge persists OpenAI routing with primary only",
            TestSetupBuildConfigJsonMergePersistsOpenAiRoutingWithPrimaryOnly);
        failed += Run("Setup config merge preserves OpenAI routing when account ids absent",
            TestSetupBuildConfigJsonMergePreservesOpenAiRoutingWhenAccountIdsAbsent);
        failed += Run("Setup config merge clears OpenAI routing when account ids explicitly empty",
            TestSetupBuildConfigJsonMergeClearsOpenAiRoutingWhenAccountIdsExplicitlyEmpty);
        failed += Run("Setup config merge clears OpenAI ids but keeps routing with primary",
            TestSetupBuildConfigJsonMergeClearsOpenAiIdsButKeepsRoutingWithPrimary);
        failed += Run("Setup config merge clears OpenAI ids when snapshot has primary",
            TestSetupBuildConfigJsonMergeClearsOpenAiIdsWhenSnapshotHasPrimary);
        failed += Run("Setup config merge preserves review settings when enabling analysis", TestSetupBuildConfigJsonMergePreservesReviewSettingsWhenEnablingAnalysis);
        failed += Run("Setup autodetect JSON serializes check statuses as lowercase strings",
            TestSetupAutodetectJsonSerializesCheckStatusesAsLowercaseStrings);
        failed += Run("Setup autodetect missing workspace value fails", TestSetupAutodetectMissingWorkspaceValueFails);
        failed += Run("Setup autodetect missing repo value fails", TestSetupAutodetectMissingRepoValueFails);
        failed += Run("Setup autodetect unknown option fails", TestSetupAutodetectUnknownOptionFails);
        failed += Run("Setup onboarding contract canonical paths", TestSetupOnboardingContractCanonicalPaths);
        failed += Run("Setup wizard path id maps to operation", TestSetupWizardPathIdMapsToOperation);
        failed += Run("Setup wizard operation maps to path id", TestSetupWizardOperationMapsToPathId);
        failed += Run("Setup wizard auto-detect reason normalization", TestSetupWizardAutoDetectReasonNormalization);
        failed += Run("Setup wizard auto-detect prompt fallback recommendation", TestSetupWizardAutoDetectPromptRecommendationFallback);
        failed += Run("Setup wizard auto-detect unavailable message formatting", TestSetupWizardAutoDetectUnavailableMessageFormatting);
        failed += Run("Setup onboarding contract command templates", TestSetupOnboardingContractCommandTemplates);
        failed += Run("Setup onboarding contract verification matches canonical values",
            TestSetupOnboardingContractVerificationMatchesCanonicalValues);
        failed += Run("Setup onboarding contract verification detects mismatches",
            TestSetupOnboardingContractVerificationDetectsMismatches);
        failed += Run("Setup onboarding contract verification rejects missing autodetect metadata",
            TestSetupOnboardingContractVerificationRejectsMissingAutodetectMetadata);
        failed += Run("Setup workflow upgrade preserves custom sections outside managed block",
            TestSetupWorkflowUpgradePreservesCustomSectionsOutsideManagedBlock);
        failed += Run("Setup workflow upgrade preserves outside managed block verbatim",
            TestSetupWorkflowUpgradePreservesOutsideManagedBlockVerbatim);
        failed += Run("Setup workflow template includes OpenAI account routing pass-through",
            TestSetupWorkflowTemplateIncludesOpenAiAccountRoutingPassThrough);
        failed += Run("Setup workflow template explicit-secrets includes diagnostics and preflight pass-through",
            TestSetupWorkflowTemplateExplicitSecretsIncludesDiagnosticsAndPreflightPassThrough);
        failed += Run("Setup workflow template non-explicit secrets uses inherit mode",
            TestSetupWorkflowTemplateNonExplicitSecretsUsesInheritMode);
        failed += Run("Setup post-apply verify passes for managed setup", TestSetupPostApplyVerifySetupPassesWithManagedWorkflowAndSecret);
        failed += Run("Setup post-apply verify detects residual cleanup config", TestSetupPostApplyVerifyCleanupDetectsResidualConfig);
        failed += Run("Setup post-apply verify allows unknown branch state when PR exists",
            TestSetupPostApplyVerifySetupAllowsUnknownBranchStateWithPr);
        failed += Run("Setup post-apply verify unauthorized secret lookup fails deterministically",
            TestSetupPostApplyVerifySecretLookupUnauthorizedFailsDeterministically);
        failed += Run("Setup post-apply verify includes latest workflow run link",
            TestSetupPostApplyVerifyIncludesLatestWorkflowRunLink);
        failed += Run("Setup post-apply verify workflow run lookup failure is not reported as none",
            TestSetupPostApplyVerifyWorkflowRunLookupFailureIsNotReportedAsNone);
        failed += Run("Setup post-apply verify does not swallow unexpected workflow lookup exceptions",
            TestSetupPostApplyVerifyDoesNotSwallowUnexpectedWorkflowLookupExceptions);
        failed += Run("Setup wizard plain reaches PR created with fake GitHub API",
            TestSetupWizardPlainReachesPullRequestCreatedWithFakeGitHubApi);
        failed += Run("Web setup args reach PR created with fake GitHub API",
            TestWebSetupArgsCanReachPullRequestCreatedWithFakeGitHubApi);
        failed += Run("Setup manual secret suppresses secret output", TestSetupManualSecretDoesNotPrintSecretValue);
        failed += Run("Setup manual secret can print secret output when explicitly enabled",
            TestSetupManualSecretCanPrintSecretValueWhenOptedIn);
        failed += Run("Setup manual secret stdout requires manual secret mode",
            TestSetupManualSecretStdoutRequiresManualSecret);
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
        failed += Run("Web setup autodetect response matches shared contract payload",
            TestWebSetupAutodetectResponseJsonMatchesSharedContractPayload);
        failed += Run("Web setup autodetect response fallbacks for null payloads",
            TestWebSetupAutodetectResponseJsonFallbacksForNullPayloads);
        failed += Run("Web setup autodetect response rejects unknown check status",
            TestWebSetupAutodetectResponseJsonRejectsUnknownCheckStatus);
        failed += Run("Web setup args propagate request dry-run", TestWebSetupBuildSetupArgsPropagatesRequestDryRun);
        failed += Run("Web setup args propagate OpenAI account routing", TestWebSetupBuildSetupArgsPropagatesOpenAiAccountRouting);
        failed += Run("Web setup args propagate OpenAI account routing with primary only",
            TestWebSetupBuildSetupArgsPropagatesOpenAiAccountRoutingWithPrimaryOnly);
        failed += Run("Web setup args propagate analysis run strict", TestWebSetupBuildSetupArgsPropagatesAnalysisRunStrict);
        failed += Run("Web setup args propagate triage bootstrap", TestWebSetupBuildSetupArgsPropagatesTriageBootstrap);
        failed += Run("Web setup resolves with-config from args", TestWebSetupResolveWithConfigFromArgs);
        failed += Run("Web setup OpenAI routing validation rejects config override",
            TestWebSetupOpenAiRoutingValidationRejectsConfigOverride);
        failed += Run("Web setup OpenAI routing validation rejects invalid rotation with primary only",
            TestWebSetupOpenAiRoutingValidationRejectsInvalidRotationWithPrimaryOnly);
        failed += Run("Web setup analysis validation normalizes run strict",
            TestWebSetupAnalysisValidationNormalizesRunStrict);
        failed += Run("Web setup analysis validation rejects run strict without analysis enabled",
            TestWebSetupAnalysisValidationRejectsRunStrictWithoutAnalysisEnabled);
        failed += Run("Web setup analysis validation rejects run strict outside preset generation",
            TestWebSetupAnalysisValidationRejectsRunStrictOutsidePresetGeneration);
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
        failed += Run("GitHub repo client list workflow runs parses latest run",
            TestGitHubRepoClientListWorkflowRunsParsesLatestRun);
        failed += Run("GitHub repo client workflow run lookup result uses defensive copy",
            TestGitHubRepoClientWorkflowRunLookupResultUsesDefensiveCopy);
        failed += Run("GitHub repo client list workflow runs invalid payload returns empty",
            TestGitHubRepoClientListWorkflowRunsInvalidPayloadReturnsEmpty);
        failed += Run("GitHub repo client list workflow runs encodes path segments",
            TestGitHubRepoClientListWorkflowRunsEncodesPathSegments);
        failed += Run("GitHub repo client list workflow runs maps unauthorized",
            TestGitHubRepoClientListWorkflowRunsMapsUnauthorized);
        failed += Run("GitHub repo client file fetch cancellation propagates", TestGitHubRepoClientFileFetchCancellationPropagates);
        failed += Run("GitHub repo client file fetch invalid base64 returns null", TestGitHubRepoClientFileFetchInvalidBase64ReturnsNull);
        failed += Run("GitHub repo client injected http client applies default headers", TestGitHubRepoClientInjectedHttpClientAppliesDefaultHeaders);
        failed += Run("GitHub repo client reused injected http client remains idempotent", TestGitHubRepoClientReusedInjectedHttpClientRemainsIdempotent);
        failed += Run("GitHub repo client file fetch missing sha returns null", TestGitHubRepoClientFileFetchMissingShaReturnsNull);
        failed += Run("GitHub repo client injected ctor null guard", TestGitHubRepoClientInjectedCtorNullGuard);
        failed += Run("GitHub repo client dispose ownership semantics", TestGitHubRepoClientDisposeOwnershipSemantics);
        failed += Run("GitHub repo client rejects use after dispose", TestGitHubRepoClientRejectsUseAfterDispose);
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
        failed += Run("Analysis packs: all-security includes PowerShell", TestAnalysisPacksAllSecurityIncludesPowerShell);
        failed += Run("Analysis packs: powershell-default resolves", TestAnalysisPacksPowerShellDefaultResolves);
        failed += Run("Analysis packs: powershell-50 resolves to 50 rules", TestAnalysisPacksPowerShell50ResolvesTo50Rules);
        failed += Run("Analysis catalog rule overrides apply", TestAnalysisCatalogRuleOverridesApply);
        failed += Run("Analysis catalog PowerShell overrides apply", () => TestAnalysisCatalogPowerShellOverridesApply());
        failed += Run("Analysis catalog PowerShell docs links", TestAnalysisCatalogPowerShellDocsLinksMatchLearnPattern);
        failed += Run("PowerShell docs snippets use exported cmdlets", TestPowerShellDocsSnippetsUseExportedCmdlets);
        failed += Run("PowerShell example scripts use exported cmdlets", TestPowerShellExampleScriptsUseExportedCmdlets);
        failed += Run("PowerShell cmdlet source XML docs are rich", TestPowerShellCmdletSourceXmlDocsAreRich);
        failed += Run("PowerShell help XML covers all cmdlets with rich docs", TestPowerShellHelpXmlCoversAllCmdletsWithRichDocs);
        failed += Run("OpenAI client C# XML docs are complete", TestOpenAiClientCSharpXmlDocsAreComplete);
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
        failed += Run("Analysis config reader keeps default duplication ruleIds on empty input",
            TestAnalysisConfigReaderKeepsDefaultDuplicationRuleIdsWhenConfiguredListEmpty);
        failed += Run("Analysis config reader reads run strict",
            TestAnalysisConfigReaderReadsRunStrict);
        failed += Run("Analysis config reader reads duplication maxOverallPercentIncrease",
            TestAnalysisConfigReaderReadsDuplicationMaxOverallPercentIncrease);
        failed += Run("Analysis config reader reads duplication maxFilePercentIncrease",
            TestAnalysisConfigReaderReadsDuplicationMaxFilePercentIncrease);
        failed += Run("Analyze gate changed-files accepts absolute in-workspace path",
            TestAnalyzeGateChangedFilesAcceptsAbsoluteInWorkspace);
        failed += Run("Analyze gate changed-files rejects absolute outside-workspace path",
            TestAnalyzeGateChangedFilesRejectsAbsoluteOutsideWorkspace);
        failed += Run("Analyze gate changed-files rejects relative traversal outside workspace",
            TestAnalyzeGateChangedFilesRejectsRelativeTraversalOutsideWorkspace);
        failed += Run("Analyze gate hotspots to-review blocks when threshold exceeded",
            TestAnalyzeGateHotspotsToReviewBlocksWhenAboveThreshold);
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
        failed += Run("Todo project labels include decision taxonomy", TestProjectLabelCatalogDefaultsIncludeDecisionLabels);
        failed += Run("Todo project labels include dynamic category and tag taxonomy", TestProjectLabelCatalogBuildEnsureCatalogIncludesDynamicCategoryAndTags);
        failed += Run("Todo project views include queue and merge views", TestProjectViewCatalogDefaultsIncludeQueueAndMergeViews);
        failed += Run("Todo project views missing detection", TestProjectViewCatalogFindMissingDefaultViewsReturnsMissingOnly);
        failed += Run("Todo project views full coverage has no missing", TestProjectViewCatalogFindMissingDefaultViewsReturnsEmptyWhenComplete);
        failed += Run("Todo project view checklist markdown includes marker and coverage", TestProjectViewChecklistMarkdownIncludesMarkerAndCoverage);
        failed += Run("Todo project view checklist markdown includes apply instructions", TestProjectViewChecklistMarkdownIncludesApplyInstructions);
        failed += Run("Todo project view apply markdown includes missing views and platform note", TestProjectViewApplyMarkdownIncludesMissingViewsAndPlatformNote);
        failed += Run("Todo project view apply markdown all views present shows completed checklist", TestProjectViewApplyMarkdownAllViewsPresentShowsCompletedChecklist);
        failed += Run("Todo project sync merges vision and canonical", TestProjectSyncBuildEntriesMergesVisionAndCanonical);
        failed += Run("Todo project sync parses category/tag confidence fields", TestProjectSyncBuildEntriesParsesCategoryAndTagConfidences);
        failed += Run("Todo project sync merges issue review action signals", TestProjectSyncBuildEntriesMergesIssueReviewSignals);
        failed += Run("Todo project sync labels include tags and high-confidence match", TestProjectSyncBuildLabelsIncludesTagsAndHighConfidenceIssueMatch);
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
        failed += Run("Analyze run PowerShell script captures engine errors", TestAnalyzeRunPowerShellScriptCapturesEngineErrors);
        failed += Run("Analyze run PowerShell strict args include fail switch", TestAnalyzeRunPowerShellStrictArgsIncludeFailSwitch);
        failed += Run("Analyze run disabled writes empty findings", TestAnalyzeRunDisabledWritesEmptyFindings);
        failed += Run("Analyze run non-strict allows runner failure", TestAnalyzeRunNonStrictAllowsRunnerFailure);
        failed += Run("Analyze run strict from config fails runner failure", TestAnalyzeRunStrictFromConfigFailsRunnerFailure);
        failed += Run("Analyze run strict false flag overrides config strict true",
            TestAnalyzeRunStrictFlagFalseOverridesConfigStrictTrue);
        failed += Run("Analyze run strict equals false overrides config strict true",
            TestAnalyzeRunStrictEqualsFalseOverridesConfigStrictTrue);
        failed += Run("Analyze run strict equals true overrides config strict false",
            TestAnalyzeRunStrictEqualsTrueOverridesConfigStrictFalse);
        failed += Run("Analyze run strict flag does not consume following option",
            TestAnalyzeRunStrictFlagDoesNotConsumeFollowingOption);
        failed += Run("Analyze run strict invalid explicit value fails",
            TestAnalyzeRunStrictFlagInvalidExplicitValueFails);
        failed += Run("Analyze run strict unknown option fails as unknown argument",
            TestAnalyzeRunStrictUnknownOptionFailsAsUnknownArgument);
        failed += Run("Analyze run strict keeps known option lookahead with dash-prefixed value",
            TestAnalyzeRunStrictFlagAllowsKnownOptionLookaheadWithDashValue);
        failed += Run("Analyze run strict keeps known option lookahead with framework value",
            TestAnalyzeRunStrictFlagAllowsKnownOptionLookaheadWithFrameworkValue);
        failed += Run("Analyze run pack override skips configured csharp runner failure",
            TestAnalyzeRunPacksOverrideSkipsConfiguredCsharpFailure);
        failed += Run("Analyze run invalid pack override fails", TestAnalyzeRunInvalidPackOverrideFails);
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
        failed += Run("Analyze run internal generated header marker supports hash comments",
            TestAnalyzeRunInternalFileSizeRuleGeneratedHeaderMarkerSupportsHashComments);
        failed += Run("Analyze run internal maintainability supports multiple rules",
            TestAnalyzeRunInternalMaintainabilitySupportsMultipleRules);
        failed += Run("Analyze run internal duplication threshold",
            TestAnalyzeRunInternalDuplicationRuleRespectsThreshold);
        failed += Run("Analyze run internal duplication malformed tags warn",
            TestAnalyzeRunInternalDuplicationRuleWarnsOnMalformedTags);
        failed += Run("Analyze run internal duplication tokenized javascript",
            TestAnalyzeRunInternalDuplicationTokenizesJavaScript);
        failed += Run("Analyze run internal duplication ignores javascript imports",
            TestAnalyzeRunInternalDuplicationIgnoresJavaScriptImports);
        failed += Run("Analyze run internal duplication ignores PowerShell using statements",
            TestAnalyzeRunInternalDuplicationIgnoresPowerShellUsingStatements);
        failed += Run("Analyze run internal duplication tokenized python",
            TestAnalyzeRunInternalDuplicationTokenizesPython);
        failed += Run("Analyze run internal duplication ignores python imports",
            TestAnalyzeRunInternalDuplicationIgnoresPythonImports);
        failed += Run("Analyze run internal duplication python triple-quote comment handling",
            TestAnalyzeRunInternalDuplicationPythonTripleQuoteCommentHandling);
        failed += Run("Analyze run include-ext is per-rule",
            TestAnalyzeRunInternalMaintainabilityIncludeExtIsPerRule);
        failed += Run("Analyze run duplication language threshold",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThreshold);
        failed += Run("Analyze run duplication language-only tag activates rule",
            TestAnalyzeRunInternalDuplicationLanguageSpecificTagOnlyActivatesRule);
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
        failed += Run("Review provider invalid config throws", TestReviewProviderConfigInvalidThrows);
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
        failed += Run("Review code host env", TestReviewCodeHostEnv);
        failed += Run("Reviewer untrusted PR skips auth store write from env", TestReviewerUntrustedPrSkipsAuthStoreWriteFromEnv);
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
        failed += Run("Copilot inherit env default", TestCopilotInheritEnvironmentDefault);
        failed += Run("Copilot direct timeout validation", TestCopilotDirectTimeoutValidation);
        failed += Run("Copilot chat timeout validation", TestCopilotChatTimeoutValidation);
        failed += Run("Copilot direct auth conflict", TestCopilotDirectAuthorizationConflict);
        failed += Run("Copilot CLI path requires env", TestCopilotCliPathRequiresEnvironment);
        failed += Run("Copilot CLI path optional with url", TestCopilotCliPathOptionalWithUrl);
        failed += Run("Copilot CLI url validation", TestCopilotCliUrlValidation);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
        failed += Run("OpenAI account order round-robin", TestOpenAiAccountOrderRoundRobin);
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
        failed += Run("Review usage budget guard blocks exhausted credits and weekly", TestReviewUsageBudgetGuardBlocksWhenCreditsAndWeeklyExhausted);
        failed += Run("Review usage budget guard allows credits fallback", TestReviewUsageBudgetGuardAllowsCreditsFallback);
        failed += Run("Review usage budget guard blocks when no budget sources are allowed",
            TestReviewUsageBudgetGuardBlocksWhenNoBudgetSourcesAllowed);
#endif

        Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
