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
        failed += Run("ChatGPT usage parse ignores legacy code review rate limit",
            TestChatGptUsageParseIgnoresLegacyCodeReviewRateLimit);
        failed += Run("ChatGPT daily token breakdown parse", TestChatGptDailyTokenBreakdownParse);
        failed += Run("ChatGPT usage cache invalid JSON", TestChatGptUsageCacheInvalidJson);
        failed += Run("ChatGPT usage cache account path", TestChatGptUsageCacheAccountPath);
        failed += Run("ChatGPT usage cache directory override path", TestChatGptUsageCacheDirectoryOverridePath);
        failed += Run("ChatGPT usage cache trailing separator override path",
            TestChatGptUsageCacheTrailingSeparatorOverridePath);
        failed += Run("EasySession forwards auth account id", TestEasySessionBuildClientOptionsCarriesAuthAccountId);
        failed += Run("EasySession forwards usage telemetry settings", TestEasySessionBuildClientOptionsCarriesUsageTelemetrySettings);
        failed += Run("Usage telemetry stable source root id normalizes paths", TestUsageTelemetryStableSourceRootIdNormalizesPaths);
        failed += Run("Usage telemetry dedupe prefers account session turn", TestUsageTelemetryDedupePrefersAccountSessionTurn);
        failed += Run("Usage conversation summary builder groups raw conversation rows",
            TestUsageConversationSummaryBuilderGroupsRawConversationRows);
        failed += Run("Usage telemetry store merges response duplicates", TestUsageTelemetryStoreMergesResponseDuplicates);
        failed += Run("Usage telemetry source root store orders roots", TestUsageTelemetrySourceRootStoreOrdersRoots);
        failed += Run("Usage telemetry sqlite store merges response duplicates", TestUsageTelemetrySqliteStoreMergesResponseDuplicates);
        failed += Run("Usage telemetry sqlite stores persist across reopen", TestUsageTelemetrySqliteStoresPersistAcrossReopen);
        failed += Run("Usage telemetry sqlite raw artifact store persists across reopen",
            TestUsageTelemetrySqliteRawArtifactStorePersistsAcrossReopen);
        failed += Run("GitHub repository watch store looks up canonical repository",
            TestGitHubRepositoryWatchStoreLooksUpCanonicalRepository);
        failed += Run("GitHub repository snapshot analytics builds daily deltas using latest snapshot per day",
            TestGitHubRepositorySnapshotAnalyticsBuildsDailyDeltasUsingLatestSnapshotPerDay);
        failed += Run("GitHub observability summary builds repo movement correlations",
            TestGitHubObservabilitySummaryBuildsRepoMovementCorrelations);
        failed += Run("GitHub observability summary builds star correlations",
            TestGitHubObservabilitySummaryBuildsStarCorrelations);
        failed += Run("GitHub observability summary builds shared fork network overlaps",
            TestGitHubObservabilitySummaryBuildsSharedForkNetworkOverlaps);
        failed += Run("GitHub observability summary builds shared stargazer audience overlaps",
            TestGitHubObservabilitySummaryBuildsSharedStargazerAudienceOverlaps);
        failed += Run("GitHub observability summary keeps full repository set for correlations",
            TestGitHubObservabilitySummaryKeepsFullRepositorySetForCorrelations);
        failed += Run("GitHub observability summary tracks stargazer coverage status",
            TestGitHubObservabilitySummaryTracksStargazerCoverageStatus);
        failed += Run("GitHub observability summary tracks fork coverage status",
            TestGitHubObservabilitySummaryTracksForkCoverageStatus);
        failed += Run("GitHub watch auto sync service syncs stale snapshots and stargazers",
            TestGitHubRepositoryWatchAutoSyncServiceSyncsStaleSnapshotsAndStargazers);
        failed += Run("GitHub watch auto sync service skips fresh repositories",
            TestGitHubRepositoryWatchAutoSyncServiceSkipsFreshRepositories);
        failed += Run("GitHub watch auto sync service marks empty fork and stargazer captures fresh",
            TestGitHubRepositoryWatchAutoSyncServiceMarksEmptyForkAndStargazerCapturesFresh);
        failed += Run("GitHub local activity correlation summary builds repo signals",
            TestGitHubLocalActivityCorrelationSummaryBuildsRepoSignals);
        failed += Run("GitHub repository cluster summary builds related repo signals",
            TestGitHubRepositoryClusterSummaryBuildsRelatedRepoSignals);
        failed += Run("Git code churn summary builds daily windows",
            TestGitCodeChurnSummaryBuildsDailyWindows);
        failed += Run("GitHub repository sqlite stores persist across reopen",
            TestGitHubRepositorySqliteStoresPersistAcrossReopen);
        failed += Run("GitHub repository fork history analytics builds new and rising statuses",
            TestGitHubRepositoryForkHistoryAnalyticsBuildsNewAndRisingStatuses);
        failed += Run("GitHub repository fork sqlite store persists across reopen",
            TestGitHubRepositoryForkSqliteStorePersistsAcrossReopen);
        failed += Run("GitHub repository stargazer sqlite store persists across reopen",
            TestGitHubRepositoryStargazerSqliteStorePersistsAcrossReopen);
        failed += Run("Usage account binding resolver matches source root and raw label",
            TestUsageAccountBindingResolverMatchesSourceRootAndRawLabel);
        failed += Run("Usage telemetry import coordinator reimport applies account binding overrides",
            TestUsageTelemetryImportCoordinatorReimportAppliesAccountBindingOverrides);
        failed += Run("Usage telemetry sqlite account binding store persists across reopen",
            TestUsageTelemetrySqliteAccountBindingStorePersistsAcrossReopen);
        failed += Run("Usage daily aggregate builder groups by provider account model and surface",
            TestUsageDailyAggregateBuilderGroupsByProviderAccountModelAndSurface);
        failed += Run("Usage daily aggregate builder can collapse across dimensions",
            TestUsageDailyAggregateBuilderCanCollapseAcrossDimensions);
        failed += Run("Usage heatmap document builder builds surface legend and tooltip",
            TestUsageHeatmapDocumentBuilderBuildsSurfaceLegendAndTooltip);
        failed += Run("Usage heatmap document builder supports cost metric and year sections",
            TestUsageHeatmapDocumentBuilderSupportsCostMetricAndYearSections);
        failed += Run("Usage heatmap document builder pads explicit range",
            TestUsageHeatmapDocumentBuilderPadsExplicitRange);
        failed += Run("Usage heatmap document builder supports single range section",
            TestUsageHeatmapDocumentBuilderSupportsSingleRangeSection);
        failed += Run("Usage summary builder calculates totals peak and rolling windows",
            TestUsageSummaryBuilderCalculatesTotalsPeakAndRollingWindows);
        failed += Run("Usage summary builder builds top breakdowns",
            TestUsageSummaryBuilderBuildsTopBreakdowns);
        failed += Run("Usage telemetry overview builder builds Copilot activity section without tokens",
            TestUsageTelemetryOverviewBuilderBuildsCopilotActivitySectionWithoutTokens);
        failed += Run("Usage telemetry API pricing blends exact and estimated costs",
            TestUsageTelemetryApiPricingBlendsExactAndEstimatedCosts);
        failed += Run("Usage telemetry API pricing covers OpenAI mode suffixes",
            TestUsageTelemetryApiPricingCoversOpenAiModeSuffixes);
        failed += Run("Provider limit forecasting flags over-limit pace",
            TestProviderLimitForecastingFlagsOverLimitPace);
        failed += Run("Provider limit forecasting recognizes on-pace window",
            TestProviderLimitForecastingRecognizesOnPaceWindow);
        failed += Run("Provider limit forecasting ranks best account",
            TestProviderLimitForecastingRanksBestAccount);
        failed += Run("Provider limit forecasting describes account runway",
            TestProviderLimitForecastingDescribesAccountRunway);
        failed += Run("Provider limit forecasting keeps unavailable accounts visible",
            TestProviderLimitForecastingKeepsUnavailableAccountsVisible);
        failed += Run("Provider limit forecasting uses watch closely for pace risk",
            TestProviderLimitForecastingUsesWatchCloselyForPaceRisk);
        failed += Run("Provider limit forecasting keeps early weekly pace as tight",
            TestProviderLimitForecastingKeepsEarlyWeeklyPaceAsTight);
        failed += Run("Provider limit forecasting keeps current account when not hard avoid",
            TestProviderLimitForecastingKeepsCurrentAccountWhenNotHardAvoid);
        failed += Run("Provider limit forecasting uses live-window wording for zero usage",
            TestProviderLimitForecastingUsesLiveWindowWordingForZeroUsage);
        failed += Run("Usage telemetry overview builder builds cards and heatmaps",
            TestUsageTelemetryOverviewBuilderBuildsCardsAndHeatmaps);
        failed += Run("Usage telemetry overview builder estimates API cost for mini and nano models",
            TestUsageTelemetryOverviewBuilderEstimatesApiCostForMiniAndNanoModels);
        failed += Run("GitHub wrapped html renderer builds shareable page",
            TestGitHubWrappedHtmlRendererBuildsShareablePage);
        failed += Run("GitHub wrapped card html renderer builds compact card",
            TestGitHubWrappedCardHtmlRendererBuildsCompactCard);
        failed += Run("Usage overview html renderer builds provider diagnostics",
            TestUsageTelemetryOverviewHtmlRendererBuildsProviderDiagnostics);
        failed += Run("Usage overview html renderer builds conversation pulse",
            TestUsageTelemetryOverviewHtmlRendererBuildsConversationPulse);
        failed += Run("Popup placement math converts pixels to DIPs",
            TestPopupPlacementMathConvertsPixelsToDips);
        failed += Run("Popup placement math clamps within work area",
            TestPopupPlacementMathClampsWithinWorkArea);
#if !NET472
        failed += Run("GitHub dashboard service explicit self lookup keeps authenticated organizations",
            TestGitHubDashboardServiceExplicitSelfLookupKeepsAuthenticatedOrganizations);
#endif
#if !NET472
        failed += Run("GitHub dashboard repository ranking deduplicates overlapping repositories",
            TestGitHubDashboardRepositoryRankingDeduplicatesOverlappingRepositories);
        failed += Run("GitHub dashboard repository ranking orders and caps repositories",
            TestGitHubDashboardRepositoryRankingOrdersAndCapsRepositories);
#endif
        failed += Run("Provider limit snapshot batch keeps healthy providers when one fails",
            TestProviderLimitSnapshotServiceBatchKeepsHealthyProvidersWhenOneFails);
        failed += Run("Provider limit snapshot batch propagates caller cancellation",
            TestProviderLimitSnapshotServiceBatchPropagatesCallerCancellation);
        failed += Run("Usage breakdown html renderer uses shared assets",
            TestUsageTelemetryBreakdownHtmlRendererUsesSharedAssets);
        failed += Run("Usage breakdown html renderer adds source family badges",
            TestUsageTelemetryBreakdownHtmlRendererAddsSourceFamilyBadges);
        failed += Run("Usage overview html renderer adds source family chips to supporting panel",
            TestUsageTelemetryOverviewHtmlRendererAddsSourceFamilyChipsToSupportingPanel);
        failed += Run("Usage breakdown page model builder builds server summary",
            TestUsageTelemetryBreakdownPageModelBuilderBuildsServerSummary);
        failed += Run("Usage overview page model builder builds supporting breakdown summaries",
            TestUsageTelemetryOverviewPageModelBuilderBuildsSupportingBreakdownSummaries);
        failed += Run("Usage overview html renderer builds GitHub owner explorer",
            TestUsageTelemetryOverviewHtmlRendererBuildsGitHubOwnerExplorer);
        failed += Run("Usage presentation helpers build source root labels",
            TestUsageTelemetryPresentationHelpersBuildSourceRootLabels);
        failed += Run("Usage presentation helpers disambiguate duplicate source root labels",
            TestUsageTelemetryPresentationHelpersDisambiguateDuplicateSourceRootLabels);
        failed += Run("Usage provider catalog resolves aliases and sort order",
            TestUsageTelemetryProviderCatalogResolvesAliasesAndSortOrder);
        failed += Run("Usage provider catalog infers provider from path",
            TestUsageTelemetryProviderCatalogInfersProviderFromPath);
        failed += Run("Usage quick report scanner supports alias provider filters",
            TestUsageTelemetryQuickReportScannerSupportsAliasProviderFilters);
        failed += Run("Usage quick report scanner deduplicates Codex session copies across roots",
            TestUsageTelemetryQuickReportScannerDeduplicatesCodexSessionCopiesAcrossRoots);
        failed += Run("Usage quick report scanner reuses cached Codex duplicates safely",
            TestUsageTelemetryQuickReportScannerReusesCachedCodexDuplicatesSafely);
        failed += Run("Usage provider registry supports alias lookups",
            TestUsageTelemetryProviderRegistrySupportsAliasLookups);
        failed += Run("Usage telemetry scope summary explains Claude local vs online gap",
            TestUsageTelemetryScopeSummaryBuilderExplainsClaudeLocalVsOnlineGap);
        failed += Run("Provider limit forecasting handles Claude window durations",
            TestProviderLimitForecastingHandlesClaudeWindowDurations);
        failed += Run("Usage overview page model builder builds GitHub render model",
            TestUsageTelemetryOverviewPageModelBuilderBuildsGitHubRenderModel);
        failed += Run("Usage overview page model builder adds watched momentum when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsWatchedMomentumWhenProvided);
        failed += Run("Usage overview page model builder adds code churn when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsCodeChurnWhenProvided);
        failed += Run("Usage overview page model builder adds GitHub fork network when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubForkNetworkWhenProvided);
        failed += Run("Usage overview page model builder adds GitHub star correlation when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStarCorrelationWhenProvided);
        failed += Run("Usage overview page model builder adds GitHub repo cluster when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubRepoClusterWhenProvided);
        failed += Run("Usage overview page model builder adds GitHub stargazer audience when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStargazerAudienceWhenProvided);
        failed += Run("Usage overview page model builder adds GitHub stargazer coverage without overlap",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStargazerCoverageWithoutOverlap);
        failed += Run("Usage overview page model builder adds GitHub fork coverage without overlap",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubForkCoverageWithoutOverlap);
        failed += Run("Usage overview page model builder adds GitHub local alignment when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsGitHubLocalAlignmentWhenProvided);
        failed += Run("Usage overview page model builder adds top-level GitHub local alignment when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsTopLevelGitHubLocalAlignmentWhenProvided);
        failed += Run("Usage overview page model builder excludes GitHub provider from local alignment input",
            TestUsageTelemetryOverviewPageModelBuilderExcludesGitHubProviderFromLocalAlignmentInput);
        failed += Run("Usage overview page model builder adds churn usage correlation when provided",
            TestUsageTelemetryOverviewPageModelBuilderAddsChurnUsageCorrelationWhenProvided);
        failed += Run("Usage GitHub wrapped page model builder builds owner panels",
            TestUsageTelemetryGitHubWrappedPageModelBuilderBuildsOwnerPanels);
        failed += Run("Usage GitHub wrapped card page model builder builds metrics",
            TestUsageTelemetryGitHubWrappedCardPageModelBuilderBuildsMetrics);
        failed += Run("Usage GitHub wrapped card page model builder adds watched momentum when provided",
            TestUsageTelemetryGitHubWrappedCardPageModelBuilderAddsWatchedMomentumWhenProvided);
        failed += Run("Usage report bundle writer publishes shared assets",
            TestUsageTelemetryReportBundleWriterPublishesSharedAssets);
        failed += Run("IntelligenceX client emits turn completed telemetry",
            TestIntelligenceXClientEmitsTurnCompletedTelemetry);
        failed += Run("EasySession forwards telemetry labels", TestEasySessionForwardsTelemetryLabels);
        failed += Run("Internal IX usage recorder writes successful turns to ledger",
            TestInternalIxUsageRecorderWritesSuccessfulTurnsToLedger);
        failed += Run("Internal IX usage recorder classifies Copilot turns as Copilot provider",
            TestInternalIxUsageRecorderClassifiesCopilotTurnsAsCopilotProvider);
        failed += Run("Usage telemetry path resolver honors environment overrides",
            TestUsageTelemetryPathResolverHonorsEnvironmentOverrides);
        failed += Run("Usage telemetry path resolver disables when flag off",
            TestUsageTelemetryPathResolverDisablesWhenFlagOff);
        failed += Run("Internal IX usage telemetry session persists turns to sqlite",
            TestInternalIxUsageTelemetrySessionPersistsTurnsToSqlite);
        failed += Run("Internal IX usage telemetry session classifies native transport as ChatGPT",
            TestInternalIxUsageTelemetrySessionClassifiesNativeTransportAsChatGpt);
        failed += Run("Internal IX usage telemetry session classifies appserver transport as Codex",
            TestInternalIxUsageTelemetrySessionClassifiesAppServerTransportAsCodex);
        failed += Run("Internal IX usage telemetry session classifies compatible-http LM Studio transport",
            TestInternalIxUsageTelemetrySessionClassifiesCompatibleHttpLmStudioTransport);
        failed += Run("Usage telemetry provider registry returns codex adapter", TestUsageTelemetryProviderRegistryReturnsCodexAdapter);
        failed += Run("Usage telemetry import coordinator registers and imports manual root",
            TestUsageTelemetryImportCoordinatorRegistersAndImportsManualRoot);
        failed += Run("Usage telemetry import coordinator imports codex direct file root",
            TestUsageTelemetryImportCoordinatorImportsCodexDirectFileRoot);
        failed += Run("Usage telemetry import coordinator resolves codex account from direct file root",
            TestUsageTelemetryImportCoordinatorResolvesCodexAccountFromDirectFileRoot);
        failed += Run("Usage telemetry import coordinator discovers codex root from environment",
            TestUsageTelemetryImportCoordinatorDiscoversCodexRootFromEnvironment);
        failed += Run("Usage telemetry import coordinator skips unchanged artifacts with raw artifact cache",
            TestUsageTelemetryImportCoordinatorSkipsUnchangedArtifactsWhenRawArtifactCacheIsPresent);
        failed += Run("Usage telemetry import coordinator force reimport bypasses artifact cache",
            TestUsageTelemetryImportCoordinatorForceReimportBypassesArtifactCache);
        failed += Run("Usage telemetry import coordinator resumes across artifact budgeted runs",
            TestUsageTelemetryImportCoordinatorCanResumeAcrossArtifactBudgetedRuns);
        failed += Run("Usage telemetry import coordinator recent-first budget prefers newest artifact",
            TestUsageTelemetryImportCoordinatorRecentFirstBudgetPrefersNewestArtifact);
        failed += Run("Usage telemetry import coordinator defers artifact cache commit until adapter succeeds",
            TestUsageTelemetryImportCoordinatorDoesNotCommitArtifactCacheWhenAdapterFails);
        failed += Run("LM Studio conversation usage adapter imports selected assistant generations",
            TestLmStudioConversationUsageAdapterImportsSelectedAssistantGenerations);
        failed += Run("LM Studio default source root discovery uses environment root",
            TestLmStudioDefaultSourceRootDiscoveryUsesEnvironmentRoot);
        failed += Run("LM Studio default source root discovery includes recovered and WSL profiles",
            TestLmStudioDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles);
        failed += Run("Usage telemetry import coordinator discovers LM Studio root from environment",
            TestUsageTelemetryImportCoordinatorDiscoversLmStudioRootFromEnvironment);
        failed += Run("Usage quick report scanner supports LM Studio alias provider filters",
            TestUsageTelemetryQuickReportScannerSupportsLmStudioAliasProviderFilters);
        failed += Run("Copilot session usage adapter imports CLI turn activity",
            TestCopilotSessionUsageAdapterImportsCliTurnActivity);
        failed += Run("Copilot session usage adapter imports session shutdown usage metrics",
            TestCopilotSessionUsageAdapterImportsSessionShutdownUsageMetrics);
#if !NET472
        failed += Run("Copilot quota snapshot client parses direct quota snapshots",
            TestCopilotQuotaSnapshotClientParsesDirectQuotaSnapshots);
        failed += Run("Copilot quota snapshot client parses monthly quota fallback",
            TestCopilotQuotaSnapshotClientParsesMonthlyQuotaFallback);
        failed += Run("Usage telemetry CLI runner appends Copilot quota insight",
            TestUsageTelemetryCliRunnerAppendsCopilotQuotaInsight);
        failed += Run("Usage telemetry CLI runner propagates Copilot quota cancellation",
            TestUsageTelemetryCliRunnerPropagatesCopilotQuotaCancellation);
#endif
        failed += Run("Copilot default source root discovery includes recovered and WSL profiles",
            TestCopilotDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles);
        failed += Run("Usage telemetry import coordinator discovers Copilot root from current profile",
            TestUsageTelemetryImportCoordinatorDiscoversCopilotRootFromCurrentProfile);
        failed += Run("Claude session usage adapter deduplicates streaming chunks",
            TestClaudeSessionUsageAdapterDeduplicatesStreamingChunks);
        failed += Run("Claude session usage adapter imports alias provider file root",
            TestClaudeSessionUsageAdapterImportsAliasProviderFileRoot);
        failed += Run("Claude default source root discovery uses environment projects root",
            TestClaudeDefaultSourceRootDiscoveryUsesEnvironmentProjectsRoot);
        failed += Run("Claude default source root discovery includes recovered and WSL profiles",
            TestClaudeDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles);
        failed += Run("Usage telemetry import coordinator discovers claude root from environment",
            TestUsageTelemetryImportCoordinatorDiscoversClaudeRootFromEnvironment);
        failed += Run("Codex default source root discovery includes recovered and WSL profiles",
            TestCodexDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles);
        failed += Run("Codex session usage adapter imports exact usage and skips duplicate totals",
            TestCodexSessionUsageAdapterImportsExactUsageAndSkipsDuplicateTotals);
        failed += Run("Codex session usage adapter parses last token usage event",
            TestCodexSessionUsageAdapterParsesLastTokenUsageEvent);
        failed += Run("Codex session usage adapter falls back to total usage delta when last usage missing",
            TestCodexSessionUsageAdapterFallsBackToTotalUsageDeltaWhenLastUsageMissing);
        failed += Run("Codex session usage adapter skips locked files",
            TestCodexSessionUsageAdapterSkipsLockedFiles);
        failed += Run("Codex session usage adapter does not duplicate sessions-root artifacts",
            TestCodexSessionUsageAdapterDoesNotDuplicateSessionsRootArtifacts);
        failed += Run("Codex session usage adapter does not duplicate archived session copies",
            TestCodexSessionUsageAdapterDoesNotDuplicateArchivedSessionCopies);
#if !NET472
        failed += Run("GitHub owner scope resolver returns administered organizations with public repos", TestGitHubOwnerScopeResolverReturnsAdministeredOrganizationsWithPublicRepos);
        failed += Run("GitHub overview collector appends correlated owners for user runs", TestGitHubOverviewDataCollectorAppendsCorrelatedOwnersForUserRuns);
        failed += Run("GitHub overview collector supports owner-only runs", TestGitHubOverviewDataCollectorSupportsOwnerOnlyRuns);
        failed += Run("GitHub overview projector uses window end for current streak", TestGitHubOverviewSectionProjectorUsesWindowEndForCurrentStreak);
        failed += Run("GitHub repository observability mapper builds snapshot", TestGitHubRepositoryObservabilityMapperBuildsSnapshot);
        failed += Run("GitHub repository fork scoring ranks recent popular forks first", TestGitHubRepositoryForkScoringRanksRecentPopularForksFirst);
        failed += Run("GitHub repository fork discovery handles partial pages with next page", TestGitHubRepositoryForkDiscoveryHandlesPartialPagesWithNextPage);
        failed += Run("Usage options parse account id", TestUsageOptionsParseAccountId);
        failed += Run("Usage options parse by-surface", TestUsageOptionsParseBySurface);
        failed += Run("Usage options parse daily breakdown", TestUsageOptionsParseDailyBreakdown);
        failed += Run("Heatmap help routes", TestHeatmapHelpRoutes);
        failed += Run("Heatmap usage json routes telemetry from sqlite", TestHeatmapUsageJsonRoutesTelemetryFromSqlite);
        failed += Run("Telemetry help routes", TestTelemetryHelpRoutes);
        failed += Run("Telemetry usage help routes", TestTelemetryUsageHelpRoutes);
        failed += Run("Telemetry GitHub help routes", TestTelemetryGitHubHelpRoutes);
        failed += Run("Telemetry GitHub watches add and list json", TestTelemetryGitHubWatchesAddAndListJson);
        failed += Run("Telemetry GitHub watches sync and snapshots list json", TestTelemetryGitHubWatchesSyncAndSnapshotsListJson);
        failed += Run("Telemetry GitHub watches sync can record forks json", TestTelemetryGitHubWatchesSyncCanRecordForksJson);
        failed += Run("Telemetry GitHub watches sync can record stargazers json", TestTelemetryGitHubWatchesSyncCanRecordStargazersJson);
        failed += Run("Telemetry GitHub watches sync marks empty fork and stargazer captures fresh",
            TestTelemetryGitHubWatchesSyncMarksEmptyForkAndStargazerCapturesFresh);
        failed += Run("Telemetry GitHub forks discover json", TestTelemetryGitHubForksDiscoverJson);
        failed += Run("Telemetry GitHub forks record and history json", TestTelemetryGitHubForksRecordAndHistoryJson);
        failed += Run("Telemetry GitHub stargazers capture and list json", TestTelemetryGitHubStargazersCaptureAndListJson);
        failed += Run("Telemetry GitHub dashboard json", TestTelemetryGitHubDashboardJson);
        failed += Run("Telemetry usage accounts bind and list json", TestTelemetryUsageAccountsBindAndListJson);
        failed += Run("Telemetry usage roots add and list json", TestTelemetryUsageRootsAddAndListJson);
        failed += Run("Telemetry usage import and stats json", TestTelemetryUsageImportAndStatsJson);
        failed += Run("Telemetry usage import supports paths-only recovered path", TestTelemetryUsageImportSupportsPathsOnlyRecoveredPath);
        failed += Run("Telemetry usage overview json and export", TestTelemetryUsageOverviewJsonAndExport);
        failed += Run("Telemetry usage report auto imports and exports", TestTelemetryUsageReportAutoImportsAndExports);
        failed += Run("Telemetry usage report supports ad hoc recovered path", TestTelemetryUsageReportSupportsAdHocRecoveredPath);
        failed += Run("Telemetry usage report supports paths-only recovered path", TestTelemetryUsageReportSupportsPathsOnlyRecoveredPath);
        failed += Run("Telemetry usage report highlights quick scan duplicate collapse", TestTelemetryUsageReportHighlightsQuickScanDuplicateCollapse);
        failed += Run("Telemetry usage report supports ad hoc LM Studio path", TestTelemetryUsageReportSupportsAdHocLmStudioPath);
        failed += Run("Telemetry usage report supports ad hoc Copilot path", TestTelemetryUsageReportSupportsAdHocCopilotPath);
        failed += Run("Telemetry usage report full import supports ad hoc recovered path", TestTelemetryUsageReportFullImportSupportsAdHocRecoveredPath);
        failed += Run("Telemetry usage GitHub request planner supports owner-only runs", TestTelemetryUsageBuildGitHubSectionRequestsSupportsOwnerOnlyRuns);
        failed += Run("Usage surface summary json buckets", TestUsageSurfaceSummaryJsonBuckets);
        failed += Run("Usage surface summary json buckets include fast tier", TestUsageSurfaceSummaryJsonBucketsIncludeFastTier);
        failed += Run("OpenAI model catalog normalizes fast mode suffix", TestOpenAiModelCatalogNormalizesFastModeSuffix);
        failed += Run("OpenAI model catalog normalizes mini and nano model ids", TestOpenAiModelCatalogNormalizesMiniAndNanoModelIds);
        failed += Run("OpenAI model catalog baseline fallback includes mini and nano", TestOpenAiModelCatalogBaselineFallbackIncludesMiniAndNano);
        failed += Run("CLI auth sync-codex help options", TestCliAuthSyncCodexHelpSupportsOptions);
        failed += Run("CLI auth sync-codex missing provider value shows help", TestCliAuthSyncCodexMissingProviderValueShowsHelp);
        failed += Run("CLI models help routes", TestCliModelsHelpRoutes);
#endif
        failed += Run("Heatmap SVG renderer emits legend and tooltip", TestHeatmapSvgRendererEmitsLegendAndTooltip);
        failed += Run("Usage telemetry heatmap document builder builds telemetry document",
            TestUsageTelemetryHeatmapDocumentBuilderBuildsTelemetryDocument);
        failed += Run("Tool call parsing", TestToolCallParsing);
        failed += Run("Tool call invalid JSON", TestToolCallParsingInvalidJson);
        failed += Run("Tool output input", TestToolOutputInput);
        failed += Run("Tool call input compatibility fields", TestToolCallInputIncludesCompatibilityFields);
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
        failed += Run("Tool runner happy path chains outputs", TestToolRunnerHappyPathChainsOutputsAcrossRounds);
        failed += Run("Ensure ChatGPT login uses cache", TestEnsureChatGptLoginUsesCache);
        failed += Run("Ensure ChatGPT login triggers when missing", TestEnsureChatGptLoginTriggersLoginWhenMissing);
        failed += Run("Ensure ChatGPT login force triggers", TestEnsureChatGptLoginForceTriggersLogin);
        failed += Run("Ensure ChatGPT login cancellation propagates", TestEnsureChatGptLoginCancellationPropagates);
#if NET8_0_OR_GREATER
        failed += Run("Auth store invalid key throws", TestAuthStoreInvalidKeyThrows);
        failed += Run("Auth store encrypted roundtrip", TestAuthStoreEncryptedRoundtrip);
        failed += Run("Auth store decrypt with explicit key override", TestAuthStoreDecryptWithExplicitKeyOverride);
        failed += Run("Auth store list filters provider and orders accounts", TestAuthStoreListAsyncFiltersProviderAndOrdersAccounts);
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
        failed += Run("Native request body normalizes tool replay items", TestNativeRequestBodyNormalizesToolReplayInputItems);
        failed += Run("Native request body normalizes type-missing replay items", TestNativeRequestBodyNormalizesTypeMissingToolReplayItems);
        failed += Run("Native request body filters unpaired tool replay items", TestNativeRequestBodyFiltersUnpairedToolReplayItems);
        failed += Run("Native request body deduplicates replay pairs", TestNativeRequestBodyDeduplicatesReplayPairsAndStripsLegacyArguments);
        failed += Run("AppServer transport normalizes replay input items", TestAppServerTransportNormalizesReplayInputItems);
        failed += Run("Native input normalization converts function call to custom tool call",
            TestNativeInputNormalizationConvertsFunctionCallToCustomToolCall);
        failed += Run("Native input normalization converts function call output to custom output",
            TestNativeInputNormalizationConvertsFunctionCallOutputToCustomOutput);
        failed += Run("Native canonical request builder normalizes history tool calls",
            TestNativeBuildCanonicalRequestMessagesNormalizesHistoryToolCalls);
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
        failed += Run("Setup args include OpenAI model", TestSetupArgsIncludeOpenAiModel);
        failed += Run("Setup provider catalog returns recommended OpenAI models",
            TestSetupProviderCatalogReturnsRecommendedOpenAiModels);
        failed += Run("Setup provider catalog returns recommended Claude models",
            TestSetupProviderCatalogReturnsRecommendedClaudeModels);
        failed += Run("Setup provider catalog describes recommended model profiles",
            TestSetupProviderCatalogDescribesRecommendedModelProfiles);
        failed += Run("Setup args include Claude model and API key", TestSetupArgsIncludeClaudeModelAndApiKey);
        failed += Run("Setup args include review loop policy and strictness",
            TestSetupArgsIncludeReviewLoopPolicyAndStrictness);
        failed += Run("Setup args reject review vision path without vision policy",
            TestSetupArgsRejectsReviewVisionPathWithoutVisionPolicy);
        failed += Run("Setup review option context rejects without with-config",
            TestSetupReviewOptionContextRejectsWithoutWithConfig);
        failed += Run("Setup review option context rejects config override",
            TestSetupReviewOptionContextRejectsConfigOverride);
        failed += Run("Setup review option context rejects vision path without vision policy",
            TestSetupReviewOptionContextRejectsVisionPathWithoutVisionPolicy);
        failed += Run("Setup config rejects invalid OpenAI account rotation", TestSetupConfigRejectsInvalidOpenAiAccountRotation);
        failed += Run("Setup config rejects invalid review loop policy", TestSetupConfigRejectsInvalidReviewLoopPolicy);
        failed += Run("Setup config rejects review options with config override",
            TestSetupConfigRejectsReviewOptionsWithConfigOverride);
        failed += Run("Setup config rejects empty merge blocker sections", TestSetupConfigRejectsEmptyMergeBlockerSections);
        failed += Run("Setup config rejects review vision path without vision policy",
            TestSetupConfigRejectsReviewVisionPathWithoutVisionPolicy);
        failed += Run("Setup config rejects missing review vision path",
            TestSetupConfigRejectsMissingReviewVisionPath);
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
        failed += Run("Setup config build includes reviewer runtime policy defaults",
            TestSetupBuildConfigJsonIncludesReviewerRuntimePolicyDefaults);
        failed += Run("Setup config build includes Claude provider defaults",
            TestSetupBuildConfigJsonIncludesClaudeProviderDefaults);
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
        failed += Run("Setup config merge switches OpenAI seed to Claude and clears OpenAI fields",
            TestSetupBuildConfigJsonMergeSwitchesOpenAiSeedToClaudeAndClearsOpenAiFields);
        failed += Run("Setup config build includes vision loop policy defaults",
            TestSetupBuildConfigJsonIncludesVisionLoopPolicyDefaults);
        failed += Run("Setup config build normalizes todo-only loop policy alias",
            TestSetupBuildConfigJsonNormalizesTodoOnlyLoopPolicyAlias);
        failed += Run("Setup config build includes vision inference from file",
            TestSetupBuildConfigJsonIncludesVisionInferenceFromFile);
        failed += Run("Setup config merge preserves review loop settings",
            TestSetupBuildConfigJsonMergePreservesReviewLoopSettings);
        failed += Run("Setup config merge refreshes managed reviewer defaults when enabling analysis",
            TestSetupBuildConfigJsonMergeRefreshesManagedReviewerDefaultsWhenEnablingAnalysis);
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
        failed += Run("Setup workflow upgrade renames legacy reusable workflow reference",
            TestSetupWorkflowUpgradeRenamesLegacyReusableWorkflowReference);
        failed += Run("Setup workflow upgrade preserves local reusable workflow reference",
            TestSetupWorkflowUpgradePreservesLocalReusableWorkflowReference);
        failed += Run("Setup workflow upgrade preserves outside managed block verbatim",
            TestSetupWorkflowUpgradePreservesOutsideManagedBlockVerbatim);
        failed += Run("Setup workflow upgrade resets model when switching to Claude provider",
            TestSetupWorkflowUpgradeResetsModelWhenSwitchingToClaudeProvider);
        failed += Run("Setup workflow template includes OpenAI account routing pass-through",
            TestSetupWorkflowTemplateIncludesOpenAiAccountRoutingPassThrough);
        failed += Run("Setup workflow template includes OpenAI model pass-through",
            TestSetupWorkflowTemplateIncludesOpenAiModelPassThrough);
        failed += Run("Review workflows keep dispatch on wrapper and resilient defaults in reusable workflow",
            TestReviewReusableWorkflowDispatchIncludesOpenAiModelInput);
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
        failed += Run("Web setup static assets serve combined wizard script",
            TestWebSetupStaticAssetsServeCombinedWizardScript);
        failed += Run("Web setup static assets expose provider model quick picks",
            TestWebSetupStaticAssetsExposeProviderModelQuickPicks);
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
        failed += Run("Web setup args propagate OpenAI model", TestWebSetupBuildSetupArgsPropagatesOpenAiModel);
        failed += Run("Web setup args propagate Claude model and API key",
            TestWebSetupBuildSetupArgsPropagatesClaudeModelAndApiKey);
        failed += Run("Web setup args propagate analysis run strict", TestWebSetupBuildSetupArgsPropagatesAnalysisRunStrict);
        failed += Run("Web setup args propagate review config tweaks",
            TestWebSetupBuildSetupArgsPropagatesReviewConfigTweaks);
        failed += Run("Web setup args omit merge blocker booleans when unset",
            TestWebSetupBuildSetupArgsOmitsMergeBlockerBooleansWhenUnset);
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
        failed += Run("Web setup review config validation normalizes loop policy",
            TestWebSetupReviewConfigValidationNormalizesLoopPolicy);
        failed += Run("Web setup review config validation normalizes todo-only loop policy",
            TestWebSetupReviewConfigValidationNormalizesTodoOnlyLoopPolicy);
        failed += Run("Web setup review config validation rejects outside preset generation",
            TestWebSetupReviewConfigValidationRejectsOutsidePresetGeneration);
        failed += Run("Web setup review config validation rejects invalid loop policy",
            TestWebSetupReviewConfigValidationRejectsInvalidLoopPolicy);
        failed += Run("Web setup review config validation rejects vision path without vision policy",
            TestWebSetupReviewConfigValidationRejectsVisionPathWithoutVisionPolicy);
        failed += Run("Web setup post-apply verify skips callback on failed apply",
            TestWebSetupPostApplyVerifySkipsCallbackWhenApplyFails);
        failed += Run("Web setup resolves org-secret verification context", TestWebSetupResolveOrgSecretVerificationContext);
        failed += Run("Web setup resolves org-secret verification context per repo", TestWebSetupResolveOrgSecretVerificationContextPerRepo);
        failed += Run("Web setup subprocess timeout returns promptly", TestWebSetupRunProcessTimeoutReturnsPromptly);
        failed += Run("GitHub contribution calendar stitches non-overlapping windows",
            TestGitHubContributionCalendarClientStitchesNonOverlappingWindows);
        failed += Run("GitHub contribution calendar parses ISO dates deterministically",
            TestGitHubContributionCalendarClientParsesIsoDatesDeterministically);
        failed += Run("GitHub contribution calendar treats null user as not found",
            TestGitHubContributionCalendarClientTreatsNullUserAsNotFound);
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
        failed += Run("CI review-fail-open-summary updates existing comment", TestCiReviewFailOpenSummaryUpdatesExistingComment);
        failed += Run("CI review-fail-open-summary creates comment for manual PR run", TestCiReviewFailOpenSummaryCreatesCommentForManualPrRun);
        failed += Run("CI review-fail-open-summary prefers reviewer token over GitHub token", TestCiReviewFailOpenSummaryPrefersReviewerTokenOverGitHubToken);
        failed += Run("CI review-fail-open-summary skips when PR number unavailable", TestCiReviewFailOpenSummarySkipsWhenPrNumberUnavailable);
        failed += Run("Reviewer GraphQL mutation detection", TestReviewerGraphQlMutationDetection);
#endif

        // Reviewer tests are excluded from NET472 builds (no reviewer references there), and enforced for non-NET472
        // builds via `IntelligenceX.Tests/ReviewerSymbolGuard.cs` + `IntelligenceX.Tests/IntelligenceX.Tests.csproj`.
        failed += RunReviewerTests();


        Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
