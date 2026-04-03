using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.ViewModels;

public enum GitHubRepoSortMode {
    Stars,
    Forks,
    Health,
    Recent
}

public sealed class GitHubViewModel : ViewModelBase {
    private string _login = "";
    private string _displayName = "";
    private string _bio = "";
    private string _company = "";
    private string _location = "";
    private string _websiteUrl = "";
    private string _usernameInput = "";
    private bool _hasToken;
    private int _ownerCount;
    private int _followers;
    private int _following;
    private int _totalContributions;
    private int _totalCommits;
    private int _totalPRs;
    private int _totalReviews;
    private int _totalIssues;
    private int _totalStars;
    private int _totalForks;
    private int _publicRepositories;
    private int _ownedRepositories;
    private int _organizationRepositories;
    private string _dominantLanguage = "Unknown";
    private bool _hasContributionData = true;
    private bool _showAccountSwitcher;
    private bool _hasData;
    private bool _isLoading;
    private string _errorMessage = "";
    private GitHubRepoSortMode _selectedRepoSort = GitHubRepoSortMode.Stars;
    private List<GitHubRepoInfo> _allRepositories = [];
    private int _watchCount;
    private int _trackedRepositoryCount;
    private int _historyReadyCount;
    private int _trackedStars;
    private int _trackedForks;
    private int _trackedWatchers;
    private int _positiveStarDelta;
    private int _positiveForkDelta;
    private int _positiveWatcherDelta;
    private int _changedTrackedRepositoryCount;
    private DateTimeOffset? _latestTrackedCaptureAtUtc;
    private string _positiveCorrelationPairText = "";
    private string _positiveCorrelationSummaryText = "";
    private string _negativeCorrelationPairText = "";
    private string _negativeCorrelationSummaryText = "";
    private string _positiveStarCorrelationPairText = "";
    private string _positiveStarCorrelationSummaryText = "";
    private string _negativeStarCorrelationPairText = "";
    private string _negativeStarCorrelationSummaryText = "";
    private string _positiveLocalAlignmentRepositoryText = "";
    private string _positiveLocalAlignmentSummaryText = "";
    private string _negativeLocalAlignmentRepositoryText = "";
    private string _negativeLocalAlignmentSummaryText = "";
    private string _repositoryClusterPairText = "";
    private string _repositoryClusterSummaryText = "";
    private string _stargazerAudienceHeadlineText = "Shared stargazer audiences appear once stargazer snapshots are captured.";
    private string _stargazerAudiencePairText = "";
    private string _stargazerAudienceSummaryText = "";
    private string _forkNetworkHeadlineText = "Shared fork networks appear once multiple watched repos attract the same forkers.";
    private string _forkNetworkCardLabelText = "SHARED FORKERS";
    private string _forkNetworkPairText = "";
    private string _forkNetworkSummaryText = "";
    private string _forkMomentumHeadlineText = "Useful fork movers appear once watched repos capture fork snapshots.";
    private string _forkMomentumPrimaryText = "";
    private string _forkMomentumSummaryText = "";

    public GitHubViewModel() {
        Owners.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOwners));
        Languages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLanguages));
        WatchedRepositories.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasWatchedRepositories));
            OnPropertyChanged(nameof(LeadingWatchedRepository));
            OnPropertyChanged(nameof(HasLeadingWatchedRepository));
        };
    }

    public string Login {
        get => _login;
        set {
            if (SetProperty(ref _login, value)) {
                OnPropertyChanged(nameof(ProfileUrl));
                OnPropertyChanged(nameof(CompactIdentityText));
            }
        }
    }
    public string DisplayName { get => _displayName; set { if (SetProperty(ref _displayName, value)) OnPropertyChanged(nameof(HasDisplayName)); } }
    public bool HasDisplayName => !string.IsNullOrWhiteSpace(DisplayName);
    public string Bio { get => _bio; set { if (SetProperty(ref _bio, value)) OnPropertyChanged(nameof(HasBio)); } }
    public bool HasBio => !string.IsNullOrWhiteSpace(Bio);
    public string Company { get => _company; set { if (SetProperty(ref _company, value)) OnPropertyChanged(nameof(HasCompany)); } }
    public bool HasCompany => !string.IsNullOrWhiteSpace(Company);
    public string Location { get => _location; set { if (SetProperty(ref _location, value)) OnPropertyChanged(nameof(HasLocation)); } }
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public string WebsiteUrl { get => _websiteUrl; set { if (SetProperty(ref _websiteUrl, value)) OnPropertyChanged(nameof(HasWebsiteUrl)); } }
    public bool HasWebsiteUrl => !string.IsNullOrWhiteSpace(WebsiteUrl);
    public string UsernameInput { get => _usernameInput; set => SetProperty(ref _usernameInput, value); }
    public bool HasToken {
        get => _hasToken;
        set {
            if (SetProperty(ref _hasToken, value)) {
                OnPropertyChanged(nameof(ShowUsernameInput));
                OnPropertyChanged(nameof(ShowCompactIdentityBar));
                OnPropertyChanged(nameof(UsernameHelpText));
                OnPropertyChanged(nameof(CompactIdentityText));
            }
        }
    }
    public bool ShowAccountSwitcher {
        get => _showAccountSwitcher;
        set {
            if (SetProperty(ref _showAccountSwitcher, value)) {
                OnPropertyChanged(nameof(ShowUsernameInput));
                OnPropertyChanged(nameof(ShowCompactIdentityBar));
                OnPropertyChanged(nameof(UsernameEditorTitle));
            }
        }
    }
    public bool ShowUsernameInput => !HasData || ShowAccountSwitcher;
    public bool ShowCompactIdentityBar => HasData && !ShowUsernameInput;
    public string UsernameEditorTitle => HasData ? "Switch GitHub profile" : "GitHub Username";
    public string UsernameHelpText => HasToken
        ? "Leave blank to load the authenticated account, or enter a username to inspect someone else."
        : "Enter a username to view public repos. Run 'gh auth login' or set GITHUB_TOKEN/GH_TOKEN for full contribution data.";
    public string CompactIdentityText => HasToken
        ? "Authenticated as " + (string.IsNullOrWhiteSpace(Login) ? "GitHub account" : Login)
        : "Viewing public profile " + (string.IsNullOrWhiteSpace(Login) ? string.Empty : Login);
    public int OwnerCount { get => _ownerCount; set { if (SetProperty(ref _ownerCount, value)) OnPropertyChanged(nameof(OwnerCountFormatted)); } }
    public string OwnerCountFormatted => FormatCount(OwnerCount);
    public int Followers { get => _followers; set { if (SetProperty(ref _followers, value)) OnPropertyChanged(nameof(FollowersFormatted)); } }
    public string FollowersFormatted => FormatCount(Followers);
    public int Following { get => _following; set { if (SetProperty(ref _following, value)) OnPropertyChanged(nameof(FollowingFormatted)); } }
    public string FollowingFormatted => FormatCount(Following);
    public int TotalContributions { get => _totalContributions; set { if (SetProperty(ref _totalContributions, value)) OnPropertyChanged(nameof(TotalContributionsFormatted)); } }
    public string TotalContributionsFormatted => HasContributionData ? FormatCount(TotalContributions) : "--";
    public int TotalCommits { get => _totalCommits; set { if (SetProperty(ref _totalCommits, value)) OnPropertyChanged(nameof(TotalCommitsFormatted)); } }
    public string TotalCommitsFormatted => HasContributionData ? FormatCount(TotalCommits) : "--";
    public int TotalPRs { get => _totalPRs; set { if (SetProperty(ref _totalPRs, value)) OnPropertyChanged(nameof(TotalPRsFormatted)); } }
    public string TotalPRsFormatted => HasContributionData ? FormatCount(TotalPRs) : "--";
    public int TotalReviews { get => _totalReviews; set { if (SetProperty(ref _totalReviews, value)) OnPropertyChanged(nameof(TotalReviewsFormatted)); } }
    public string TotalReviewsFormatted => HasContributionData ? FormatCount(TotalReviews) : "--";
    public int TotalIssues { get => _totalIssues; set { if (SetProperty(ref _totalIssues, value)) OnPropertyChanged(nameof(TotalIssuesFormatted)); } }
    public string TotalIssuesFormatted => HasContributionData ? FormatCount(TotalIssues) : "--";
    public int TotalStars { get => _totalStars; set { if (SetProperty(ref _totalStars, value)) OnPropertyChanged(nameof(TotalStarsFormatted)); } }
    public string TotalStarsFormatted => FormatCount(TotalStars);
    public int TotalForks { get => _totalForks; set { if (SetProperty(ref _totalForks, value)) OnPropertyChanged(nameof(TotalForksFormatted)); } }
    public string TotalForksFormatted => FormatCount(TotalForks);
    public int PublicRepositories { get => _publicRepositories; set { if (SetProperty(ref _publicRepositories, value)) OnPropertyChanged(nameof(PublicRepositoriesFormatted)); } }
    public string PublicRepositoriesFormatted => FormatCount(PublicRepositories);
    public int OwnedRepositories { get => _ownedRepositories; set { if (SetProperty(ref _ownedRepositories, value)) OnPropertyChanged(nameof(OwnedRepositoriesFormatted)); } }
    public string OwnedRepositoriesFormatted => FormatCount(OwnedRepositories);
    public int OrganizationRepositories { get => _organizationRepositories; set { if (SetProperty(ref _organizationRepositories, value)) OnPropertyChanged(nameof(OrganizationRepositoriesFormatted)); OnPropertyChanged(nameof(HasOrganizationRepositories)); } }
    public string OrganizationRepositoriesFormatted => FormatCount(OrganizationRepositories);
    public bool HasOrganizationRepositories => OrganizationRepositories > 0;
    public string DominantLanguage { get => _dominantLanguage; set => SetProperty(ref _dominantLanguage, value); }
    public bool HasContributionData {
        get => _hasContributionData;
        set {
            if (SetProperty(ref _hasContributionData, value)) {
                OnPropertyChanged(nameof(TotalContributionsFormatted));
                OnPropertyChanged(nameof(TotalCommitsFormatted));
                OnPropertyChanged(nameof(TotalPRsFormatted));
                OnPropertyChanged(nameof(TotalReviewsFormatted));
                OnPropertyChanged(nameof(TotalIssuesFormatted));
                OnPropertyChanged(nameof(ContributionAvailabilityText));
                OnPropertyChanged(nameof(ShowContributionFallbackMessage));
            }
        }
    }
    public bool ShowContributionFallbackMessage => HasData && !HasContributionData;
    public string ContributionAvailabilityText => HasContributionData
        ? "Authenticated GitHub contribution history."
        : "Public repo data loaded. Contribution history requires 'gh auth login', GITHUB_TOKEN, GH_TOKEN, or INTELLIGENCEX_GITHUB_TOKEN.";
    public bool HasData {
        get => _hasData;
        set {
            if (SetProperty(ref _hasData, value)) {
                OnPropertyChanged(nameof(ShowUsernameInput));
                OnPropertyChanged(nameof(ShowCompactIdentityBar));
                OnPropertyChanged(nameof(UsernameEditorTitle));
            }
        }
    }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string ErrorMessage {
        get => _errorMessage;
        set {
            if (SetProperty(ref _errorMessage, value)) {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }
    public string ProfileUrl => string.IsNullOrWhiteSpace(Login) ? string.Empty : $"https://github.com/{Login}";
    public bool HasOwners => Owners.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasLanguages => Languages.Count > 0;
    public bool HasTopRepos => TopRepos.Count > 0;
    public int WatchCount {
        get => _watchCount;
        set {
            if (SetProperty(ref _watchCount, value)) {
                OnPropertyChanged(nameof(WatchCountFormatted));
                OnPropertyChanged(nameof(HasObservabilitySummary));
                OnPropertyChanged(nameof(ObservabilityCoverageText));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
                OnPropertyChanged(nameof(ObservabilitySetupText));
                OnPropertyChanged(nameof(HasForkNetworkInsight));
                OnPropertyChanged(nameof(HasForkMomentum));
            }
        }
    }
    public string WatchCountFormatted => FormatCount(WatchCount);
    public int TrackedRepositoryCount {
        get => _trackedRepositoryCount;
        set {
            if (SetProperty(ref _trackedRepositoryCount, value)) {
                OnPropertyChanged(nameof(TrackedRepositoryCountFormatted));
                OnPropertyChanged(nameof(HasObservabilitySummary));
                OnPropertyChanged(nameof(ObservabilityCoverageText));
                OnPropertyChanged(nameof(ObservabilitySetupText));
            }
        }
    }
    public string TrackedRepositoryCountFormatted => FormatCount(TrackedRepositoryCount);
    public int HistoryReadyCount {
        get => _historyReadyCount;
        set {
            if (SetProperty(ref _historyReadyCount, value)) {
                OnPropertyChanged(nameof(HistoryReadyCountFormatted));
                OnPropertyChanged(nameof(HasObservabilityMomentum));
                OnPropertyChanged(nameof(ObservabilityCoverageText));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
            }
        }
    }
    public string HistoryReadyCountFormatted => FormatCount(HistoryReadyCount);
    public int TrackedStars {
        get => _trackedStars;
        set {
            if (SetProperty(ref _trackedStars, value)) {
                OnPropertyChanged(nameof(TrackedStarsFormatted));
            }
        }
    }
    public string TrackedStarsFormatted => FormatCount(TrackedStars);
    public int TrackedForks {
        get => _trackedForks;
        set {
            if (SetProperty(ref _trackedForks, value)) {
                OnPropertyChanged(nameof(TrackedForksFormatted));
            }
        }
    }
    public string TrackedForksFormatted => FormatCount(TrackedForks);
    public int TrackedWatchers {
        get => _trackedWatchers;
        set {
            if (SetProperty(ref _trackedWatchers, value)) {
                OnPropertyChanged(nameof(TrackedWatchersFormatted));
            }
        }
    }
    public string TrackedWatchersFormatted => FormatCount(TrackedWatchers);
    public int PositiveStarDelta {
        get => _positiveStarDelta;
        set {
            if (SetProperty(ref _positiveStarDelta, value)) {
                OnPropertyChanged(nameof(PositiveStarDeltaFormatted));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
            }
        }
    }
    public string PositiveStarDeltaFormatted => FormatSignedCount(PositiveStarDelta);
    public int PositiveForkDelta {
        get => _positiveForkDelta;
        set {
            if (SetProperty(ref _positiveForkDelta, value)) {
                OnPropertyChanged(nameof(PositiveForkDeltaFormatted));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
            }
        }
    }
    public string PositiveForkDeltaFormatted => FormatSignedCount(PositiveForkDelta);
    public int PositiveWatcherDelta {
        get => _positiveWatcherDelta;
        set {
            if (SetProperty(ref _positiveWatcherDelta, value)) {
                OnPropertyChanged(nameof(PositiveWatcherDeltaFormatted));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
            }
        }
    }
    public string PositiveWatcherDeltaFormatted => FormatSignedCount(PositiveWatcherDelta);
    public int ChangedTrackedRepositoryCount {
        get => _changedTrackedRepositoryCount;
        set {
            if (SetProperty(ref _changedTrackedRepositoryCount, value)) {
                OnPropertyChanged(nameof(ChangedTrackedRepositoryCountFormatted));
                OnPropertyChanged(nameof(ObservabilityMomentumText));
            }
        }
    }
    public string ChangedTrackedRepositoryCountFormatted => FormatCount(ChangedTrackedRepositoryCount);
    public DateTimeOffset? LatestTrackedCaptureAtUtc {
        get => _latestTrackedCaptureAtUtc;
        set {
            if (SetProperty(ref _latestTrackedCaptureAtUtc, value)) {
                OnPropertyChanged(nameof(ObservabilityLatestCaptureText));
            }
        }
    }
    public bool HasObservabilitySummary => WatchCount > 0 || TrackedRepositoryCount > 0;
    public bool HasObservabilityMomentum => HistoryReadyCount > 0;
    public string ObservabilityCoverageText => WatchCount switch {
        <= 0 => "No watched repositories are registered in the local telemetry store yet.",
        _ when TrackedRepositoryCount <= 0 => $"{WatchCountFormatted} watched repos configured, but no snapshots have been synced yet.",
        _ when HistoryReadyCount <= 0 => $"{TrackedRepositoryCountFormatted} watched repos have baseline snapshots. Run another sync to start delta tracking.",
        _ => $"{TrackedRepositoryCountFormatted} tracked repos • {HistoryReadyCountFormatted} with comparable history."
    };
    public string ObservabilityMomentumText => HistoryReadyCount switch {
        <= 0 when WatchCount > 0 => "Momentum appears after each watched repo has at least two synced snapshots.",
        <= 0 => "Watch repos to see star, fork, and watcher movement over time.",
        _ => $"{ChangedTrackedRepositoryCountFormatted} repos moved on the last sync • {PositiveStarDeltaFormatted} stars • {PositiveForkDeltaFormatted} forks • {PositiveWatcherDeltaFormatted} watchers"
    };
    public string ObservabilityLatestCaptureText => LatestTrackedCaptureAtUtc.HasValue
        ? "Latest capture " + LatestTrackedCaptureAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
        : "No synced watch snapshots yet.";
    public bool HasPositiveCorrelation => !string.IsNullOrWhiteSpace(PositiveCorrelationPairText);
    public string PositiveCorrelationPairText {
        get => _positiveCorrelationPairText;
        set {
            if (SetProperty(ref _positiveCorrelationPairText, value)) {
                OnPropertyChanged(nameof(HasPositiveCorrelation));
                OnPropertyChanged(nameof(HasCorrelationSignals));
            }
        }
    }
    public string PositiveCorrelationSummaryText { get => _positiveCorrelationSummaryText; set => SetProperty(ref _positiveCorrelationSummaryText, value); }
    public bool HasNegativeCorrelation => !string.IsNullOrWhiteSpace(NegativeCorrelationPairText);
    public string NegativeCorrelationPairText {
        get => _negativeCorrelationPairText;
        set {
            if (SetProperty(ref _negativeCorrelationPairText, value)) {
                OnPropertyChanged(nameof(HasNegativeCorrelation));
                OnPropertyChanged(nameof(HasCorrelationSignals));
            }
        }
    }
    public string NegativeCorrelationSummaryText { get => _negativeCorrelationSummaryText; set => SetProperty(ref _negativeCorrelationSummaryText, value); }
    public bool HasCorrelationSignals => HasPositiveCorrelation || HasNegativeCorrelation;
    public bool HasPositiveStarCorrelation => !string.IsNullOrWhiteSpace(PositiveStarCorrelationPairText);
    public string PositiveStarCorrelationPairText {
        get => _positiveStarCorrelationPairText;
        set {
            if (SetProperty(ref _positiveStarCorrelationPairText, value)) {
                OnPropertyChanged(nameof(HasPositiveStarCorrelation));
                OnPropertyChanged(nameof(HasStarCorrelationSignals));
            }
        }
    }
    public string PositiveStarCorrelationSummaryText { get => _positiveStarCorrelationSummaryText; set => SetProperty(ref _positiveStarCorrelationSummaryText, value); }
    public bool HasNegativeStarCorrelation => !string.IsNullOrWhiteSpace(NegativeStarCorrelationPairText);
    public string NegativeStarCorrelationPairText {
        get => _negativeStarCorrelationPairText;
        set {
            if (SetProperty(ref _negativeStarCorrelationPairText, value)) {
                OnPropertyChanged(nameof(HasNegativeStarCorrelation));
                OnPropertyChanged(nameof(HasStarCorrelationSignals));
            }
        }
    }
    public string NegativeStarCorrelationSummaryText { get => _negativeStarCorrelationSummaryText; set => SetProperty(ref _negativeStarCorrelationSummaryText, value); }
    public bool HasStarCorrelationSignals => HasPositiveStarCorrelation || HasNegativeStarCorrelation;
    public string StarCorrelationHeadlineText => HasStarCorrelationSignals
        ? "Watched repos compared only by shared daily star movement."
        : "Star-sync signals appear once watched repos build overlapping daily star history.";
    public bool HasPositiveLocalAlignment => !string.IsNullOrWhiteSpace(PositiveLocalAlignmentRepositoryText);
    public string PositiveLocalAlignmentRepositoryText {
        get => _positiveLocalAlignmentRepositoryText;
        set {
            if (SetProperty(ref _positiveLocalAlignmentRepositoryText, value)) {
                OnPropertyChanged(nameof(HasPositiveLocalAlignment));
                OnPropertyChanged(nameof(HasLocalAlignmentSignals));
            }
        }
    }
    public string PositiveLocalAlignmentSummaryText { get => _positiveLocalAlignmentSummaryText; set => SetProperty(ref _positiveLocalAlignmentSummaryText, value); }
    public bool HasNegativeLocalAlignment => !string.IsNullOrWhiteSpace(NegativeLocalAlignmentRepositoryText);
    public string NegativeLocalAlignmentRepositoryText {
        get => _negativeLocalAlignmentRepositoryText;
        set {
            if (SetProperty(ref _negativeLocalAlignmentRepositoryText, value)) {
                OnPropertyChanged(nameof(HasNegativeLocalAlignment));
                OnPropertyChanged(nameof(HasLocalAlignmentSignals));
            }
        }
    }
    public string NegativeLocalAlignmentSummaryText { get => _negativeLocalAlignmentSummaryText; set => SetProperty(ref _negativeLocalAlignmentSummaryText, value); }
    public bool HasLocalAlignmentSignals => HasPositiveLocalAlignment || HasNegativeLocalAlignment;
    public string LocalAlignmentHeadlineText => HasLocalAlignmentSignals
        ? "Watched repos compared with the recent local coding pulse."
        : "Local-vs-repo alignment appears once watched repo momentum overlaps with local churn and telemetry usage.";
    public bool HasRepositoryCluster => !string.IsNullOrWhiteSpace(RepositoryClusterPairText);
    public string RepositoryClusterPairText {
        get => _repositoryClusterPairText;
        set {
            if (SetProperty(ref _repositoryClusterPairText, value)) {
                OnPropertyChanged(nameof(HasRepositoryCluster));
                OnPropertyChanged(nameof(RepositoryClusterHeadlineText));
            }
        }
    }
    public string RepositoryClusterSummaryText { get => _repositoryClusterSummaryText; set => SetProperty(ref _repositoryClusterSummaryText, value); }
    public string RepositoryClusterHeadlineText => HasRepositoryCluster
        ? "Related watched repos with multiple audience and momentum signals lining up."
        : "Related-repo clusters appear once star sync plus shared stargazer or fork audiences start overlapping.";
    public bool HasStargazerAudienceInsight => !string.IsNullOrWhiteSpace(StargazerAudiencePairText) || !string.IsNullOrWhiteSpace(StargazerAudienceSummaryText);
    public string StargazerAudiencePairText {
        get => _stargazerAudiencePairText;
        set {
            if (SetProperty(ref _stargazerAudiencePairText, value)) {
                OnPropertyChanged(nameof(HasStargazerAudienceInsight));
            }
        }
    }
    public string StargazerAudienceSummaryText {
        get => _stargazerAudienceSummaryText;
        set {
            if (SetProperty(ref _stargazerAudienceSummaryText, value)) {
                OnPropertyChanged(nameof(HasStargazerAudienceInsight));
            }
        }
    }
    public string StargazerAudienceHeadlineText {
        get => _stargazerAudienceHeadlineText;
        set => SetProperty(ref _stargazerAudienceHeadlineText, value);
    }
    public bool HasForkNetworkInsight => WatchCount > 0 || !string.IsNullOrWhiteSpace(ForkNetworkPairText) || !string.IsNullOrWhiteSpace(ForkNetworkSummaryText);
    public string ForkNetworkCardLabelText {
        get => _forkNetworkCardLabelText;
        set => SetProperty(ref _forkNetworkCardLabelText, value);
    }
    public string ForkNetworkPairText {
        get => _forkNetworkPairText;
        set {
            if (SetProperty(ref _forkNetworkPairText, value)) {
                OnPropertyChanged(nameof(HasForkNetworkInsight));
            }
        }
    }
    public string ForkNetworkSummaryText {
        get => _forkNetworkSummaryText;
        set {
            if (SetProperty(ref _forkNetworkSummaryText, value)) {
                OnPropertyChanged(nameof(HasForkNetworkInsight));
            }
        }
    }
    public string ForkNetworkHeadlineText {
        get => _forkNetworkHeadlineText;
        set => SetProperty(ref _forkNetworkHeadlineText, value);
    }
    public bool HasForkMomentum => WatchCount > 0 || !string.IsNullOrWhiteSpace(ForkMomentumPrimaryText) || !string.IsNullOrWhiteSpace(ForkMomentumSummaryText);
    public string ForkMomentumHeadlineText {
        get => _forkMomentumHeadlineText;
        set => SetProperty(ref _forkMomentumHeadlineText, value);
    }
    public string ForkMomentumPrimaryText {
        get => _forkMomentumPrimaryText;
        set {
            if (SetProperty(ref _forkMomentumPrimaryText, value)) {
                OnPropertyChanged(nameof(HasForkMomentum));
            }
        }
    }
    public string ForkMomentumSummaryText {
        get => _forkMomentumSummaryText;
        set {
            if (SetProperty(ref _forkMomentumSummaryText, value)) {
                OnPropertyChanged(nameof(HasForkMomentum));
            }
        }
    }
    public string ObservabilitySetupText => WatchCount > 0
        ? "Tracked repos are read from the shared telemetry SQLite store used by the GitHub telemetry CLI. With GitHub auth available, the tray can auto-sync watched repos, useful forks, and stargazer audiences in the background."
        : "Use `intelligencex telemetry github watches add --repo owner/name` and `... watches sync --stargazers` to start tracking repo momentum, fork networks, and audience overlap.";
    public GitHubRepoSortMode SelectedRepoSort {
        get => _selectedRepoSort;
        set {
            if (!SetProperty(ref _selectedRepoSort, value)) {
                return;
            }

            OnPropertyChanged(nameof(IsRepoSortStarsSelected));
            OnPropertyChanged(nameof(IsRepoSortForksSelected));
            OnPropertyChanged(nameof(IsRepoSortHealthSelected));
            OnPropertyChanged(nameof(IsRepoSortRecentSelected));
            OnPropertyChanged(nameof(RepoSortLabel));
            OnPropertyChanged(nameof(RepoSortHint));
            RebuildTopRepos();
        }
    }
    public bool IsRepoSortStarsSelected => SelectedRepoSort == GitHubRepoSortMode.Stars;
    public bool IsRepoSortForksSelected => SelectedRepoSort == GitHubRepoSortMode.Forks;
    public bool IsRepoSortHealthSelected => SelectedRepoSort == GitHubRepoSortMode.Health;
    public bool IsRepoSortRecentSelected => SelectedRepoSort == GitHubRepoSortMode.Recent;
    public string RepoSortLabel => SelectedRepoSort switch {
        GitHubRepoSortMode.Stars => "Top by stars",
        GitHubRepoSortMode.Forks => "Top by forks",
        GitHubRepoSortMode.Health => "Top by health",
        GitHubRepoSortMode.Recent => "Most recent",
        _ => "Top repositories"
    };
    public string RepoSortHint => SelectedRepoSort switch {
        GitHubRepoSortMode.Stars => "Ranked by stars, then forks.",
        GitHubRepoSortMode.Forks => "Ranked by forks, then stars.",
        GitHubRepoSortMode.Health => "Ranked by recent push plus repository impact.",
        GitHubRepoSortMode.Recent => "Ranked by latest push timestamp.",
        _ => string.Empty
    };

    public ObservableCollection<GitHubContribBarViewModel> ContribBars { get; } = [];
    public ObservableCollection<GitHubLanguageViewModel> Languages { get; } = [];
    public ObservableCollection<GitHubOwnerViewModel> Owners { get; } = [];
    public ObservableCollection<GitHubRepoViewModel> TopRepos { get; } = [];
    public ObservableCollection<GitHubWatchedRepositoryViewModel> WatchedRepositories { get; } = [];
    public bool HasWatchedRepositories => WatchedRepositories.Count > 0;
    public GitHubWatchedRepositoryViewModel? LeadingWatchedRepository => WatchedRepositories.FirstOrDefault();
    public bool HasLeadingWatchedRepository => LeadingWatchedRepository is not null;

    public void ClearData() {
        ClearProfileData();
        ClearObservabilitySummary();
    }

    public void ClearProfileData() {
        Login = string.Empty;
        DisplayName = string.Empty;
        Bio = string.Empty;
        Company = string.Empty;
        Location = string.Empty;
        WebsiteUrl = string.Empty;
        Followers = 0;
        Following = 0;
        TotalContributions = 0;
        TotalCommits = 0;
        TotalPRs = 0;
        TotalReviews = 0;
        TotalIssues = 0;
        TotalStars = 0;
        TotalForks = 0;
        PublicRepositories = 0;
        OwnerCount = 0;
        OwnedRepositories = 0;
        OrganizationRepositories = 0;
        DominantLanguage = "Unknown";
        HasContributionData = true;
        ShowAccountSwitcher = false;
        ContribBars.Clear();
        Languages.Clear();
        Owners.Clear();
        TopRepos.Clear();
        _allRepositories = [];
        HasData = false;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(ProfileUrl));
    }

    public void ClearObservabilitySummary() {
        WatchCount = 0;
        TrackedRepositoryCount = 0;
        HistoryReadyCount = 0;
        TrackedStars = 0;
        TrackedForks = 0;
        TrackedWatchers = 0;
        PositiveStarDelta = 0;
        PositiveForkDelta = 0;
        PositiveWatcherDelta = 0;
        ChangedTrackedRepositoryCount = 0;
        LatestTrackedCaptureAtUtc = null;
        PositiveCorrelationPairText = string.Empty;
        PositiveCorrelationSummaryText = string.Empty;
        NegativeCorrelationPairText = string.Empty;
        NegativeCorrelationSummaryText = string.Empty;
        PositiveStarCorrelationPairText = string.Empty;
        PositiveStarCorrelationSummaryText = string.Empty;
        NegativeStarCorrelationPairText = string.Empty;
        NegativeStarCorrelationSummaryText = string.Empty;
        PositiveLocalAlignmentRepositoryText = string.Empty;
        PositiveLocalAlignmentSummaryText = string.Empty;
        NegativeLocalAlignmentRepositoryText = string.Empty;
        NegativeLocalAlignmentSummaryText = string.Empty;
        RepositoryClusterPairText = string.Empty;
        RepositoryClusterSummaryText = string.Empty;
        StargazerAudienceHeadlineText = "Shared stargazer audiences appear once stargazer snapshots are captured.";
        StargazerAudiencePairText = string.Empty;
        StargazerAudienceSummaryText = string.Empty;
        ForkNetworkHeadlineText = "Shared fork networks appear once multiple watched repos attract the same forkers.";
        ForkNetworkCardLabelText = "SHARED FORKERS";
        ForkNetworkPairText = string.Empty;
        ForkNetworkSummaryText = string.Empty;
        ForkMomentumHeadlineText = "Useful fork movers appear once watched repos capture fork snapshots.";
        ForkMomentumPrimaryText = string.Empty;
        ForkMomentumSummaryText = string.Empty;
        WatchedRepositories.Clear();
    }

    internal void ApplyObservabilitySummary(GitHubObservabilitySummaryData data) {
        data ??= GitHubObservabilitySummaryData.Empty;
        WatchCount = data.EnabledWatchCount;
        TrackedRepositoryCount = data.SnapshotRepositoryCount;
        HistoryReadyCount = data.ComparableRepositoryCount;
        TrackedStars = data.TotalStars;
        TrackedForks = data.TotalForks;
        TrackedWatchers = data.TotalWatchers;
        PositiveStarDelta = data.PositiveStarDelta;
        PositiveForkDelta = data.PositiveForkDelta;
        PositiveWatcherDelta = data.PositiveWatcherDelta;
        ChangedTrackedRepositoryCount = data.ChangedRepositoryCount;
        LatestTrackedCaptureAtUtc = data.LatestCaptureAtUtc;
        ApplyCorrelationSummary(
            data.StrongestPositiveCorrelation,
            data.StrongestNegativeCorrelation);
        ApplyStarCorrelationSummary(
            data.StrongestPositiveStarCorrelation,
            data.StrongestNegativeStarCorrelation);
        ApplyStargazerAudienceSummary(data);
        ApplyForkNetworkSummary(data);
        ApplyForkMomentumSummary(data);

        WatchedRepositories.Clear();
        foreach (var repository in data.FeaturedRepositories) {
            var slashIndex = repository.RepositoryNameWithOwner.IndexOf('/');
            var trendBars = BuildTrendBars(repository.TrendPoints);
            WatchedRepositories.Add(new GitHubWatchedRepositoryViewModel {
                FullName = repository.RepositoryNameWithOwner,
                Name = slashIndex > 0 ? repository.RepositoryNameWithOwner[(slashIndex + 1)..] : repository.RepositoryNameWithOwner,
                RepositoryUrl = "https://github.com/" + repository.RepositoryNameWithOwner,
                DeltaSummaryText = BuildObservedRepositoryDeltaText(repository),
                CurrentMetricsText = $"{FormatCount(repository.Stars)} stars • {FormatCount(repository.Forks)} forks • {FormatCount(repository.Watchers)} watchers",
                CaptureText = "captured " + repository.CurrentCapturedAtUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture),
                StatusText = ClassifyObservedRepository(repository),
                TrendBars = trendBars,
                TrendSummaryText = BuildTrendSummaryText(repository.TrendPoints)
            });
        }
    }

    internal void ApplyLocalActivityCorrelationSummary(GitHubLocalActivityCorrelationSummaryData data) {
        data ??= GitHubLocalActivityCorrelationSummaryData.Empty;
        if (data.StrongestPositiveCorrelation is not null) {
            PositiveLocalAlignmentRepositoryText = BuildLocalAlignmentRepositoryText(data.StrongestPositiveCorrelation);
            PositiveLocalAlignmentSummaryText = BuildLocalAlignmentSummaryText(data.StrongestPositiveCorrelation, divergent: false);
        } else {
            PositiveLocalAlignmentRepositoryText = string.Empty;
            PositiveLocalAlignmentSummaryText = string.Empty;
        }

        if (data.StrongestNegativeCorrelation is not null) {
            NegativeLocalAlignmentRepositoryText = BuildLocalAlignmentRepositoryText(data.StrongestNegativeCorrelation);
            NegativeLocalAlignmentSummaryText = BuildLocalAlignmentSummaryText(data.StrongestNegativeCorrelation, divergent: true);
        } else {
            NegativeLocalAlignmentRepositoryText = string.Empty;
            NegativeLocalAlignmentSummaryText = string.Empty;
        }

        OnPropertyChanged(nameof(LocalAlignmentHeadlineText));
    }

    internal void ApplyRepositoryClusterSummary(GitHubRepositoryClusterSummaryData data) {
        data ??= GitHubRepositoryClusterSummaryData.Empty;
        if (data.StrongestCluster is null) {
            RepositoryClusterPairText = string.Empty;
            RepositoryClusterSummaryText = string.Empty;
            return;
        }

        RepositoryClusterPairText = BuildRepositoryClusterPairText(data.StrongestCluster);
        RepositoryClusterSummaryText = BuildRepositoryClusterSummaryText(data.StrongestCluster);
    }

    private void ApplyForkNetworkSummary(GitHubObservabilitySummaryData data) {
        var overlap = data.StrongestForkNetworkOverlap;
        ForkNetworkHeadlineText = BuildForkNetworkHeadlineText(data, overlap);
        ForkNetworkCardLabelText = overlap is null ? "FORK COVERAGE" : "SHARED FORKERS";
        if (overlap is null) {
            ForkNetworkPairText = BuildForkCoveragePairText(data);
            ForkNetworkSummaryText = BuildForkCoverageText(data, includeTimestamp: true);
            return;
        }

        ForkNetworkPairText = BuildForkNetworkPairText(overlap);
        ForkNetworkSummaryText = BuildForkNetworkSummaryText(data, overlap);
    }

    private void ApplyForkMomentumSummary(GitHubObservabilitySummaryData data) {
        var change = data.StrongestForkChange;
        ForkMomentumHeadlineText = BuildForkMomentumHeadlineText(data, change);
        if (change is null) {
            ForkMomentumPrimaryText = BuildForkMomentumPendingText(data);
            ForkMomentumSummaryText = BuildForkCoverageText(data, includeTimestamp: true);
            return;
        }

        ForkMomentumPrimaryText = BuildForkMomentumPrimaryText(change);
        ForkMomentumSummaryText = BuildForkMomentumSummaryText(data, change);
    }

    private void ApplyStargazerAudienceSummary(GitHubObservabilitySummaryData data) {
        var overlap = data.StrongestStargazerAudienceOverlap;
        StargazerAudienceHeadlineText = BuildStargazerAudienceHeadlineText(data, overlap);
        if (overlap is null) {
            StargazerAudiencePairText = BuildStargazerAudienceCoveragePairText(data);
            StargazerAudienceSummaryText = BuildStargazerAudienceCoverageText(data, includeTimestamp: true);
            return;
        }

        StargazerAudiencePairText = BuildStargazerAudiencePairText(overlap);
        StargazerAudienceSummaryText = BuildStargazerAudienceSummaryText(data, overlap);
    }

    private void ApplyStarCorrelationSummary(
        GitHubObservedStarCorrelationData? positiveCorrelation,
        GitHubObservedStarCorrelationData? negativeCorrelation) {
        if (positiveCorrelation is null) {
            PositiveStarCorrelationPairText = string.Empty;
            PositiveStarCorrelationSummaryText = string.Empty;
        } else {
            PositiveStarCorrelationPairText = BuildStarCorrelationPairText(positiveCorrelation);
            PositiveStarCorrelationSummaryText = BuildStarCorrelationSummaryText(positiveCorrelation, divergent: false);
        }

        if (negativeCorrelation is null) {
            NegativeStarCorrelationPairText = string.Empty;
            NegativeStarCorrelationSummaryText = string.Empty;
        } else {
            NegativeStarCorrelationPairText = BuildStarCorrelationPairText(negativeCorrelation);
            NegativeStarCorrelationSummaryText = BuildStarCorrelationSummaryText(negativeCorrelation, divergent: true);
        }
    }

    private void ApplyCorrelationSummary(
        GitHubObservedCorrelationData? positiveCorrelation,
        GitHubObservedCorrelationData? negativeCorrelation) {
        if (positiveCorrelation is null) {
            PositiveCorrelationPairText = string.Empty;
            PositiveCorrelationSummaryText = string.Empty;
        } else {
            PositiveCorrelationPairText = BuildCorrelationPairText(positiveCorrelation);
            PositiveCorrelationSummaryText = BuildCorrelationSummaryText(positiveCorrelation, divergent: false);
        }

        if (negativeCorrelation is null) {
            NegativeCorrelationPairText = string.Empty;
            NegativeCorrelationSummaryText = string.Empty;
        } else {
            NegativeCorrelationPairText = BuildCorrelationPairText(negativeCorrelation);
            NegativeCorrelationSummaryText = BuildCorrelationSummaryText(negativeCorrelation, divergent: true);
        }
    }

    public void Apply(GitHubDashboardData data) {
        if (string.IsNullOrWhiteSpace(UsernameInput) && !string.IsNullOrWhiteSpace(data.Login)) {
            UsernameInput = data.Login;
        }

        ShowAccountSwitcher = false;

        Login = data.Login;
        OnPropertyChanged(nameof(ProfileUrl));
        var profile = data.Profile;
        DisplayName = NormalizeOptional(profile.DisplayName) ?? string.Empty;
        Bio = NormalizeOptional(profile.Bio) ?? string.Empty;
        Company = NormalizeOptional(profile.Company) ?? string.Empty;
        Location = NormalizeOptional(profile.Location) ?? string.Empty;
        WebsiteUrl = NormalizeOptional(profile.WebsiteUrl) ?? string.Empty;
        Followers = profile.Followers;
        Following = profile.Following;
        var c = data.Contributions;
        HasContributionData = data.HasContributionData;
        TotalContributions = c.TotalContributions;
        TotalCommits = c.TotalCommits;
        TotalPRs = c.TotalPRs;
        TotalReviews = c.TotalReviews;
        TotalIssues = c.TotalIssues;

        var allRepos = data.AllRepos ?? data.TopRepos;
        _allRepositories = allRepos
            .Where(static repo => !string.IsNullOrWhiteSpace(repo.NameWithOwner))
            .ToList();
        var login = data.Login.Trim();
        var languageGroups = allRepos
            .Select(static repo => NormalizeLanguage(repo.Language))
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .GroupBy(static language => language!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new {
                Language = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ownerGroups = allRepos
            .Select(static repo => new {
                Repo = repo,
                Owner = ExtractOwner(repo.NameWithOwner)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Owner))
            .GroupBy(static item => item.Owner!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new {
                Owner = group.Key,
                RepositoryCount = group.Count(),
                Stars = group.Sum(static item => item.Repo.Stars),
                Forks = group.Sum(static item => item.Repo.Forks),
                IsPrimaryOwner = string.Equals(group.Key, login, StringComparison.OrdinalIgnoreCase)
            })
            .OrderByDescending(static group => group.IsPrimaryOwner)
            .ThenByDescending(static group => group.RepositoryCount)
            .ThenByDescending(static group => group.Stars)
            .ThenBy(static group => group.Owner, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PublicRepositories = profile.PublicRepositories > 0 ? profile.PublicRepositories : allRepos.Count;
        OwnerCount = ownerGroups.Count;
        OwnedRepositories = allRepos.Count(repo => HasOwner(repo, login));
        OrganizationRepositories = Math.Max(0, allRepos.Count - OwnedRepositories);
        TotalStars = allRepos.Sum(r => r.Stars);
        TotalForks = allRepos.Sum(r => r.Forks);
        DominantLanguage = languageGroups.FirstOrDefault()?.Language ?? "Unknown";

        ContribBars.Clear();
        if (HasContributionData) {
            var last30 = c.DailyContributions
                .OrderByDescending(d => d.Date)
                .Take(30)
                .OrderBy(d => d.Date)
                .ToList();
            var maxContrib = last30.Count > 0 ? Math.Max(1, last30.Max(d => d.Count)) : 1;
            foreach (var day in last30) {
                var brush = ParseColorOrDefault(day.Color, "#40c463");
                ContribBars.Add(new GitHubContribBarViewModel {
                    Date = day.Date,
                    Count = day.Count,
                    BarHeight = Math.Max(2, 40.0 * day.Count / maxContrib),
                    BarBrush = brush,
                    DayLabel = day.Date.Day == 1 || day.Date == last30.First().Date
                        ? day.Date.ToString("MMM d") : ""
                });
            }
        }

        Owners.Clear();
        foreach (var owner in ownerGroups.Take(6)) {
            Owners.Add(new GitHubOwnerViewModel {
                Owner = owner.Owner,
                KindText = owner.IsPrimaryOwner ? "You" : "Org",
                RepositoryCount = owner.RepositoryCount,
                Stars = owner.Stars,
                Forks = owner.Forks,
                ProfileUrl = "https://github.com/" + owner.Owner
            });
        }

        Languages.Clear();
        var maxLanguageCount = languageGroups.Count > 0 ? languageGroups.Max(static group => group.Count) : 0;
        foreach (var language in languageGroups.Take(6)) {
            Languages.Add(new GitHubLanguageViewModel {
                Language = language.Language,
                RepositoryCount = language.Count,
                RepositoryCountFormatted = language.Count.ToString("N0"),
                Proportion = maxLanguageCount > 0 ? (double)language.Count / maxLanguageCount : 0d,
                BarBrush = ParseColorOrDefault(ResolveLanguageColor(language.Language), "#8b949e")
            });
        }

        RebuildTopRepos();
        HasData = true;
        ErrorMessage = string.Empty;
    }

    public void SetRepoSort(GitHubRepoSortMode sort) {
        SelectedRepoSort = sort;
    }

    private void RebuildTopRepos() {
        TopRepos.Clear();
        OnPropertyChanged(nameof(HasTopRepos));
        if (_allRepositories.Count == 0) {
            return;
        }

        IEnumerable<GitHubRepoInfo> ordered = SelectedRepoSort switch {
            GitHubRepoSortMode.Forks => _allRepositories
                .OrderByDescending(static repo => repo.Forks)
                .ThenByDescending(static repo => repo.Stars)
                .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase),
            GitHubRepoSortMode.Health => _allRepositories
                .OrderByDescending(static repo => ComputeHealthScore(repo))
                .ThenByDescending(static repo => repo.Stars)
                .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase),
            GitHubRepoSortMode.Recent => _allRepositories
                .OrderByDescending(static repo => repo.PushedAtUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(static repo => repo.Stars)
                .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase),
            _ => _allRepositories
                .OrderByDescending(static repo => repo.Stars)
                .ThenByDescending(static repo => repo.Forks)
                .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var repo in ordered.Take(6)) {
            var owner = ExtractOwner(repo.NameWithOwner) ?? Login;
            var pushedAtText = repo.PushedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "no recent push";
            TopRepos.Add(new GitHubRepoViewModel {
                Name = repo.NameWithOwner.Contains('/') ? repo.NameWithOwner.Split('/')[1] : repo.NameWithOwner,
                FullName = repo.NameWithOwner,
                Owner = owner,
                Stars = repo.Stars,
                Forks = repo.Forks,
                Watchers = repo.Watchers,
                OpenIssues = repo.OpenIssues,
                Description = NormalizeOptional(repo.Description) ?? "No description available.",
                Language = repo.Language ?? "",
                LanguageBrush = ParseColorOrDefault(repo.LanguageColor, "#8b949e"),
                RepositoryUrl = $"https://github.com/{repo.NameWithOwner}",
                RankMetricText = BuildRankMetricText(repo),
                StatusText = ClassifyRepositoryHealth(repo),
                PushedAtText = pushedAtText,
                HasRecentPush = repo.PushedAtUtc.HasValue,
                IsArchived = repo.IsArchived
            });
        }

        OnPropertyChanged(nameof(HasTopRepos));
    }

    private static SolidColorBrush ParseColorOrDefault(string? hex, string fallback) {
        try {
            var color = (Color)ColorConverter.ConvertFromString(hex ?? fallback);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        } catch {
            return Brushes.Gray;
        }
    }

    private static bool HasOwner(GitHubRepoInfo repo, string login) {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(repo.NameWithOwner)) {
            return false;
        }

        var slashIndex = repo.NameWithOwner.IndexOf('/');
        if (slashIndex <= 0) {
            return false;
        }

        var owner = repo.NameWithOwner[..slashIndex];
        return string.Equals(owner, login, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractOwner(string? nameWithOwner) {
        if (string.IsNullOrWhiteSpace(nameWithOwner)) {
            return null;
        }

        var slashIndex = nameWithOwner.IndexOf('/');
        return slashIndex <= 0 ? null : nameWithOwner[..slashIndex];
    }

    private static string? NormalizeLanguage(string? language) {
        var normalized = NormalizeOptional(language);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string BuildObservedRepositoryDeltaText(GitHubObservedRepositoryTrendData repository) {
        if (!repository.PreviousCapturedAtUtc.HasValue) {
            return "Baseline snapshot only";
        }

        return $"{FormatSignedCount(repository.StarDelta)} stars • {FormatSignedCount(repository.ForkDelta)} forks • {FormatSignedCount(repository.WatcherDelta)} watchers";
    }

    private static string ClassifyObservedRepository(GitHubObservedRepositoryTrendData repository) {
        if (!repository.PreviousCapturedAtUtc.HasValue) {
            return "Baseline";
        }

        if (repository.StarDelta > 0 || repository.ForkDelta > 0 || repository.WatcherDelta > 0) {
            return "Trending up";
        }

        if (repository.StarDelta < 0 || repository.ForkDelta < 0 || repository.WatcherDelta < 0) {
            return "Cooling";
        }

        return "Stable";
    }

    private static IReadOnlyList<GitHubRepoTrendBarViewModel> BuildTrendBars(IReadOnlyList<GitHubObservedTrendPointData> points) {
        if (points is null || points.Count == 0) {
            return Array.Empty<GitHubRepoTrendBarViewModel>();
        }

        var maxMagnitude = points
            .Select(static point => Math.Abs(point.Score))
            .DefaultIfEmpty(0d)
            .Max();
        if (maxMagnitude <= 0d) {
            maxMagnitude = 1d;
        }

        var positiveBrush = Brushes.MediumSeaGreen;
        var negativeBrush = Brushes.Goldenrod;
        var neutralBrush = Brushes.DimGray;

        return points
            .Select(point => new GitHubRepoTrendBarViewModel {
                Height = point.DayUtc == default
                    ? 4d
                    : 4d + (16d * Math.Abs(point.Score) / maxMagnitude),
                BarBrush = point.DayUtc == default
                    ? neutralBrush
                    : point.Score > 0d
                        ? positiveBrush
                        : point.Score < 0d
                            ? negativeBrush
                            : neutralBrush,
                ToolTipText = point.DayUtc == default
                    ? "No daily delta yet"
                    : point.DayUtc.ToString("MMM d", CultureInfo.CurrentCulture)
                      + " • "
                      + FormatSignedCount(point.StarDelta) + " stars • "
                      + FormatSignedCount(point.ForkDelta) + " forks • "
                      + FormatSignedCount(point.WatcherDelta) + " watchers"
            })
            .ToArray();
    }

    private static string BuildTrendSummaryText(IReadOnlyList<GitHubObservedTrendPointData> points) {
        if (points is null || points.Count == 0 || points.All(static point => point.DayUtc == default)) {
            return "7-day trend builds after daily snapshots accumulate.";
        }

        var activePoints = points.Where(static point => point.DayUtc != default).ToList();
        if (activePoints.Count == 0) {
            return "7-day trend builds after daily snapshots accumulate.";
        }

        var positiveDays = activePoints.Count(static point => point.Score > 0d);
        var negativeDays = activePoints.Count(static point => point.Score < 0d);
        var windowStart = activePoints.First().DayUtc.ToString("MMM d", CultureInfo.CurrentCulture);
        var windowEnd = activePoints.Last().DayUtc.ToString("MMM d", CultureInfo.CurrentCulture);
        return $"7-day pulse {windowStart} to {windowEnd} • {positiveDays} up days • {negativeDays} down days";
    }

    private static string BuildCorrelationPairText(GitHubObservedCorrelationData correlation) {
        return correlation.RepositoryANameWithOwner + " ↔ " + correlation.RepositoryBNameWithOwner;
    }

    private static string BuildCorrelationSummaryText(GitHubObservedCorrelationData correlation, bool divergent) {
        var label = divergent ? "diverges" : "syncs";
        var lead = divergent
            ? $"{correlation.OpposingDays} opposing days"
            : $"{correlation.SharedUpDays} up together";
        var trailing = divergent
            ? $"{correlation.SharedUpDays} up together"
            : $"{correlation.SharedDownDays} down together";
        return $"{label} r {correlation.Correlation:+0.00;-0.00;0.00} across {correlation.OverlapDays} shared days • {lead} • {trailing}";
    }

    private static string BuildStarCorrelationPairText(GitHubObservedStarCorrelationData correlation) {
        return correlation.RepositoryANameWithOwner + " ↔ " + correlation.RepositoryBNameWithOwner;
    }

    private static string BuildStarCorrelationSummaryText(GitHubObservedStarCorrelationData correlation, bool divergent) {
        var label = divergent ? "star divergence" : "star sync";
        var lead = divergent
            ? $"{correlation.OpposingDays} opposing star days"
            : $"{correlation.SharedGainDays} gain-together days";
        var trailing = divergent
            ? $"{FormatSignedCount(correlation.RepositoryARecentStarChange)} / {FormatSignedCount(correlation.RepositoryBRecentStarChange)} stars"
            : $"{correlation.SharedDropDays} drop-together days";
        return $"{label} r {correlation.Correlation:+0.00;-0.00;0.00} across {correlation.OverlapDays} shared days • {lead} • {trailing}";
    }

    private static string BuildLocalAlignmentRepositoryText(GitHubLocalActivityRepositoryCorrelationData correlation) {
        return correlation.RepositoryNameWithOwner + " • " + correlation.Correlation.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
    }

    private static string BuildLocalAlignmentSummaryText(GitHubLocalActivityRepositoryCorrelationData correlation, bool divergent) {
        var movementText = BuildObservedRepositoryDeltaText(
            new GitHubObservedRepositoryTrendData(
                correlation.RepositoryNameWithOwner,
                correlation.RecentStars,
                correlation.RecentForks,
                correlation.RecentWatchers,
                openIssues: 0,
                starDelta: correlation.StarDelta,
                forkDelta: correlation.ForkDelta,
                watcherDelta: correlation.WatcherDelta,
                openIssueDelta: 0,
                currentCapturedAtUtc: DateTimeOffset.UtcNow,
                previousCapturedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
                trendPoints: Array.Empty<GitHubObservedTrendPointData>()));
        var overlapText = divergent
            ? correlation.OpposingDays.ToString("N0", CultureInfo.CurrentCulture) + " repo-down local-active days"
            : correlation.AlignedDays.ToString("N0", CultureInfo.CurrentCulture) + " aligned active days";
        return movementText
               + " • "
               + overlapText
               + " • "
               + correlation.OverlapDays.ToString("N0", CultureInfo.CurrentCulture)
               + " overlap days";
    }

    private static string BuildForkNetworkPairText(GitHubObservedForkNetworkOverlapData overlap) {
        return overlap.RepositoryANameWithOwner + " ↔ " + overlap.RepositoryBNameWithOwner;
    }

    private static string BuildStargazerAudiencePairText(GitHubObservedStargazerAudienceOverlapData overlap) {
        return overlap.RepositoryANameWithOwner + " ↔ " + overlap.RepositoryBNameWithOwner;
    }

    private static string BuildStargazerAudienceSummaryText(
        GitHubObservabilitySummaryData data,
        GitHubObservedStargazerAudienceOverlapData overlap) {
        var sampleLogins = overlap.SampleSharedStargazers.Count > 0
            ? "shared: " + string.Join(", ", overlap.SampleSharedStargazers)
            : "shared stargazer audience";
        return overlap.SharedStargazerCount.ToString("N0", CultureInfo.CurrentCulture)
               + " shared stargazers • "
               + overlap.RepositoryAStargazerCount.ToString("N0", CultureInfo.CurrentCulture)
               + "/"
               + overlap.RepositoryBStargazerCount.ToString("N0", CultureInfo.CurrentCulture)
               + " observed stargazers • "
               + overlap.OverlapRatio.ToString("0%", CultureInfo.CurrentCulture)
               + " smaller-set overlap • "
               + sampleLogins
               + " • "
               + BuildStargazerAudienceCoverageText(data, includeTimestamp: true);
    }

    private static string BuildStargazerAudienceHeadlineText(
        GitHubObservabilitySummaryData data,
        GitHubObservedStargazerAudienceOverlapData? overlap) {
        if (overlap is not null) {
            return data.HasStaleStargazerCoverage
                ? "Watched repos sharing stargazers, with audience coverage behind the latest sync."
                : "Watched repos sharing the same stargazer audience.";
        }

        return data.HasAnyStargazerSnapshots
            ? "Audience capture is active, but no shared stargazer overlap is confirmed yet."
            : "Shared stargazer audiences appear once stargazer snapshots are captured.";
    }

    private static string BuildStargazerAudienceCoveragePairText(GitHubObservabilitySummaryData data) {
        if (!data.HasAnyStargazerSnapshots) {
            return "Audience capture pending";
        }

        return "No shared stargazers detected yet";
    }

    private static string BuildStargazerAudienceCoverageText(
        GitHubObservabilitySummaryData data,
        bool includeTimestamp) {
        var parts = new List<string> {
            data.StargazerSnapshotRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
            + "/"
            + data.EnabledWatchCount.ToString("N0", CultureInfo.CurrentCulture)
            + " watched repos captured"
        };
        if (data.MissingStargazerSnapshotRepositoryCount > 0) {
            parts.Add(data.MissingStargazerSnapshotRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " missing audience snapshots");
        }
        if (data.LaggingStargazerRepositoryCount > 0) {
            parts.Add(data.LaggingStargazerRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " behind latest repo sync");
        } else if (data.HasFreshStargazerCoverage) {
            parts.Add("aligned with latest repo sync");
        }
        if (includeTimestamp && data.LatestStargazerCaptureAtUtc.HasValue) {
            parts.Add("last audience sync "
                      + data.LatestStargazerCaptureAtUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture));
        }
        if (data.HasAnyStargazerSnapshots && data.ObservedStargazerCount > 0) {
            parts.Add(data.ObservedStargazerCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " distinct observed stargazers");
        }

        return string.Join(" • ", parts);
    }

    private static string BuildForkNetworkHeadlineText(
        GitHubObservabilitySummaryData data,
        GitHubObservedForkNetworkOverlapData? overlap) {
        if (overlap is not null) {
            return data.HasStaleForkCoverage
                ? "Watched repos sharing fork owners, with fork coverage behind the latest sync."
                : "Watched repos sharing the same fork-owner audience.";
        }

        return data.HasAnyForkSnapshots
            ? "Fork capture is active, but no shared fork-owner overlap is confirmed yet."
            : "Shared fork networks appear once multiple watched repos attract the same forkers.";
    }

    private static string BuildForkCoveragePairText(GitHubObservabilitySummaryData data) {
        if (!data.HasAnyForkSnapshots) {
            return "Fork capture pending";
        }

        return "No shared fork-owner overlap detected yet";
    }

    private static string BuildForkCoverageText(
        GitHubObservabilitySummaryData data,
        bool includeTimestamp) {
        var parts = new List<string> {
            data.ForkSnapshotRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
            + "/"
            + data.EnabledWatchCount.ToString("N0", CultureInfo.CurrentCulture)
            + " watched repos captured"
        };
        if (data.MissingForkSnapshotRepositoryCount > 0) {
            parts.Add(data.MissingForkSnapshotRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " missing fork snapshots");
        }
        if (data.LaggingForkRepositoryCount > 0) {
            parts.Add(data.LaggingForkRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " behind latest repo sync");
        } else if (data.HasFreshForkCoverage) {
            parts.Add("aligned with latest repo sync");
        }
        if (includeTimestamp && data.LatestForkCaptureAtUtc.HasValue) {
            parts.Add("last fork sync "
                      + data.LatestForkCaptureAtUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture));
        }
        if (data.HasAnyForkSnapshots && data.ObservedForkOwnerCount > 0) {
            parts.Add(data.ObservedForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " distinct observed fork owners");
        }

        return string.Join(" • ", parts);
    }

    private static string BuildForkNetworkSummaryText(
        GitHubObservabilitySummaryData data,
        GitHubObservedForkNetworkOverlapData overlap) {
        var sampleOwners = overlap.SampleSharedForkOwners.Count > 0
            ? "shared: " + string.Join(", ", overlap.SampleSharedForkOwners)
            : "shared fork-owner audience";
        return overlap.SharedForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture)
               + " shared fork owners • "
               + overlap.RepositoryAForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture)
               + "/"
               + overlap.RepositoryBForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture)
               + " observed owners • "
               + overlap.OverlapRatio.ToString("0%", CultureInfo.CurrentCulture)
               + " smaller-set overlap • "
               + sampleOwners
               + " • "
               + BuildForkCoverageText(data, includeTimestamp: true);
    }

    private static string BuildForkMomentumHeadlineText(
        GitHubObservabilitySummaryData data,
        GitHubRepositoryForkChange? change) {
        if (change is not null) {
            return data.HasStaleForkCoverage
                ? "Useful fork movers are visible, but some fork coverage still trails the latest sync."
                : "Useful fork movers inside the watched repo network.";
        }

        return data.HasAnyForkSnapshots
            ? "Fork capture is active, but no rising fork mover stands out yet."
            : "Useful fork movers appear once watched repos capture fork snapshots.";
    }

    private static string BuildForkMomentumPendingText(GitHubObservabilitySummaryData data) {
        return data.HasAnyForkSnapshots
            ? "No strong fork movers detected yet"
            : "Fork mover capture pending";
    }

    private static string BuildForkMomentumPrimaryText(GitHubRepositoryForkChange change) {
        return change.ForkRepositoryNameWithOwner + " ↗ " + change.ParentRepositoryNameWithOwner;
    }

    private static string BuildForkMomentumSummaryText(
        GitHubObservabilitySummaryData data,
        GitHubRepositoryForkChange change) {
        var parts = new List<string> {
            change.Status + " • " + change.Tier + " tier",
            change.Score.ToString("0.##", CultureInfo.CurrentCulture) + " score",
            change.ScoreDelta.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) + " score delta",
            FormatSignedCount(change.StarDelta) + " stars",
            FormatSignedCount(change.WatcherDelta) + " watchers",
            BuildForkCoverageText(data, includeTimestamp: true)
        };

        return string.Join(" • ", parts);
    }

    private static string BuildRepositoryClusterPairText(GitHubRepositoryClusterData cluster) {
        return cluster.RepositoryANameWithOwner + " ↔ " + cluster.RepositoryBNameWithOwner;
    }

    private static string BuildRepositoryClusterSummaryText(GitHubRepositoryClusterData cluster) {
        var parts = new List<string> {
            cluster.SupportingSignalCount.ToString("N0", CultureInfo.CurrentCulture) + " signals",
            "star sync " + cluster.StarCorrelation.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
        };
        if (cluster.SharedStargazerCount > 0) {
            parts.Add(cluster.SharedStargazerCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " shared stargazers");
        }
        if (cluster.SharedForkOwnerCount > 0) {
            parts.Add(cluster.SharedForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture)
                      + " shared forkers");
        }
        if (cluster.LocallyAlignedRepositoryCount == 2) {
            parts.Add("both aligned locally "
                      + cluster.LocalAlignmentAverageCorrelation.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture));
        }
        if (cluster.SampleSharedStargazers.Count > 0) {
            parts.Add("shared stars: " + string.Join(", ", cluster.SampleSharedStargazers));
        }
        if (cluster.SampleSharedForkOwners.Count > 0) {
            parts.Add("shared: " + string.Join(", ", cluster.SampleSharedForkOwners));
        }

        return string.Join(" • ", parts);
    }

    private string BuildRankMetricText(GitHubRepoInfo repo) {
        return SelectedRepoSort switch {
            GitHubRepoSortMode.Forks => $"{FormatCount(repo.Forks)} forks • {FormatCount(repo.Stars)} stars",
            GitHubRepoSortMode.Health => $"{ClassifyRepositoryHealth(repo)} • {FormatCount(repo.Stars)} stars • {FormatCount(repo.Forks)} forks",
            GitHubRepoSortMode.Recent => repo.PushedAtUtc.HasValue
                ? "pushed " + repo.PushedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
                : "no recent push data",
            _ => $"{FormatCount(repo.Stars)} stars • {FormatCount(repo.Forks)} forks"
        };
    }

    private static string ClassifyRepositoryHealth(GitHubRepoInfo repo) {
        if (repo.IsArchived) {
            return "Archived";
        }

        var daysOld = repo.PushedAtUtc.HasValue
            ? Math.Max(0, (DateTimeOffset.UtcNow.Date - repo.PushedAtUtc.Value.Date).Days)
            : int.MaxValue;

        if (daysOld <= 14 && repo.Stars >= 50) {
            return "Rising";
        }
        if (daysOld <= 30) {
            return "Active";
        }
        if (daysOld <= 90) {
            return "Established";
        }
        if (daysOld <= 180) {
            return "Warm";
        }

        return "Dormant";
    }

    private static double ComputeHealthScore(GitHubRepoInfo repo) {
        var impactScore = repo.Stars + (repo.Forks * 2) + repo.Watchers;
        if (repo.IsArchived) {
            return impactScore * 0.25d;
        }
        if (!repo.PushedAtUtc.HasValue) {
            return impactScore;
        }

        var daysOld = Math.Max(0, (DateTimeOffset.UtcNow.Date - repo.PushedAtUtc.Value.Date).Days);
        var recencyBoost = daysOld switch {
            <= 7 => 250d,
            <= 30 => 140d,
            <= 90 => 70d,
            <= 180 => 25d,
            _ => 0d
        };

        return impactScore + recencyBoost;
    }

    public void BeginAccountSwitch() {
        ShowAccountSwitcher = true;
    }

    public void EndAccountSwitch() {
        ShowAccountSwitcher = false;
    }

    private static string ResolveLanguageColor(string? language) => language?.ToLowerInvariant() switch {
        "powershell" => "#012456",
        "c#" => "#178600",
        "javascript" => "#f1e05a",
        "typescript" => "#3178c6",
        "python" => "#3572A5",
        "go" => "#00ADD8",
        "rust" => "#dea584",
        "java" => "#b07219",
        "html" => "#e34c26",
        "css" => "#563d7c",
        "ruby" => "#701516",
        "php" => "#4F5D95",
        "swift" => "#F05138",
        "kotlin" => "#A97BFF",
        "dart" => "#00B4AB",
        "shell" => "#89e051",
        _ => "#8b949e"
    };

    private static string FormatCount(int n) => n switch {
        >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
        >= 1_000 => $"{n / 1_000.0:F1}K",
        _ => n.ToString("N0")
    };

    private static string FormatSignedCount(int value) {
        if (value > 0) {
            return "+" + FormatCount(value);
        }

        if (value < 0) {
            return "-" + FormatCount(Math.Abs(value));
        }

        return "0";
    }
}

public sealed class GitHubContribBarViewModel {
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public double BarHeight { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
    public string DayLabel { get; set; } = "";
}

public sealed class GitHubRepoViewModel {
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Owner { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public int Stars { get; set; }
    public int Forks { get; set; }
    public int Watchers { get; set; }
    public int OpenIssues { get; set; }
    public string Language { get; set; } = "";
    public Brush LanguageBrush { get; set; } = Brushes.Gray;
    public string RankMetricText { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string PushedAtText { get; set; } = "";
    public bool HasRecentPush { get; set; }
    public bool IsArchived { get; set; }
    public string StarsFormatted => Stars >= 1000 ? $"{Stars / 1000.0:F1}K" : Stars.ToString("N0");
    public string ForksFormatted => Forks >= 1000 ? $"{Forks / 1000.0:F1}K" : Forks.ToString("N0");
    public string WatchersFormatted => Watchers >= 1000 ? $"{Watchers / 1000.0:F1}K" : Watchers.ToString("N0");
    public string OpenIssuesFormatted => OpenIssues >= 1000 ? $"{OpenIssues / 1000.0:F1}K" : OpenIssues.ToString("N0");
}

public sealed class GitHubWatchedRepositoryViewModel {
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string DeltaSummaryText { get; set; } = "";
    public string CurrentMetricsText { get; set; } = "";
    public string CaptureText { get; set; } = "";
    public string StatusText { get; set; } = "";
    public IReadOnlyList<GitHubRepoTrendBarViewModel> TrendBars { get; set; } = Array.Empty<GitHubRepoTrendBarViewModel>();
    public string TrendSummaryText { get; set; } = "";
    public bool HasTrendBars => TrendBars.Count > 0;
}

public sealed class GitHubRepoTrendBarViewModel {
    public double Height { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
    public string ToolTipText { get; set; } = "";
}

public sealed class GitHubLanguageViewModel {
    public string Language { get; set; } = "";
    public int RepositoryCount { get; set; }
    public string RepositoryCountFormatted { get; set; } = "";
    public double Proportion { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
}

public sealed class GitHubOwnerViewModel {
    public string Owner { get; set; } = "";
    public string KindText { get; set; } = "";
    public int RepositoryCount { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public string ProfileUrl { get; set; } = "";
    public string RepositoryCountFormatted => RepositoryCount.ToString("N0");
    public string StarsFormatted => Stars >= 1000 ? $"{Stars / 1000.0:F1}K" : Stars.ToString("N0");
    public string ForksFormatted => Forks >= 1000 ? $"{Forks / 1000.0:F1}K" : Forks.ToString("N0");
}
