using System.Collections.ObjectModel;
using System.Windows.Media;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.ViewModels;

public sealed class GitHubViewModel : ViewModelBase {
    private string _login = "";
    private string _usernameInput = "";
    private bool _hasToken;
    private int _ownerCount;
    private int _totalContributions;
    private int _totalCommits;
    private int _totalPRs;
    private int _totalReviews;
    private int _totalIssues;
    private int _totalStars;
    private int _totalForks;
    private int _ownedRepositories;
    private int _organizationRepositories;
    private bool _hasData;
    private bool _isLoading;
    private string _errorMessage = "";

    public GitHubViewModel() {
        Owners.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOwners));
    }

    public string Login {
        get => _login;
        set {
            if (SetProperty(ref _login, value)) {
                OnPropertyChanged(nameof(ProfileUrl));
            }
        }
    }
    public string UsernameInput { get => _usernameInput; set => SetProperty(ref _usernameInput, value); }
    public bool HasToken {
        get => _hasToken;
        set {
            if (SetProperty(ref _hasToken, value)) {
                OnPropertyChanged(nameof(ShowUsernameInput));
                OnPropertyChanged(nameof(UsernameHelpText));
            }
        }
    }
    public bool ShowUsernameInput => true;
    public string UsernameHelpText => HasToken
        ? "Leave blank to load the authenticated account, or enter a username to inspect someone else."
        : "Enter a username to view public repos. Set GITHUB_TOKEN or GH_TOKEN for full contribution data.";
    public int OwnerCount { get => _ownerCount; set { if (SetProperty(ref _ownerCount, value)) OnPropertyChanged(nameof(OwnerCountFormatted)); } }
    public string OwnerCountFormatted => FormatCount(OwnerCount);
    public int TotalContributions { get => _totalContributions; set { if (SetProperty(ref _totalContributions, value)) OnPropertyChanged(nameof(TotalContributionsFormatted)); } }
    public string TotalContributionsFormatted => FormatCount(TotalContributions);
    public int TotalCommits { get => _totalCommits; set { if (SetProperty(ref _totalCommits, value)) OnPropertyChanged(nameof(TotalCommitsFormatted)); } }
    public string TotalCommitsFormatted => FormatCount(TotalCommits);
    public int TotalPRs { get => _totalPRs; set { if (SetProperty(ref _totalPRs, value)) OnPropertyChanged(nameof(TotalPRsFormatted)); } }
    public string TotalPRsFormatted => FormatCount(TotalPRs);
    public int TotalReviews { get => _totalReviews; set { if (SetProperty(ref _totalReviews, value)) OnPropertyChanged(nameof(TotalReviewsFormatted)); } }
    public string TotalReviewsFormatted => FormatCount(TotalReviews);
    public int TotalIssues { get => _totalIssues; set { if (SetProperty(ref _totalIssues, value)) OnPropertyChanged(nameof(TotalIssuesFormatted)); } }
    public string TotalIssuesFormatted => FormatCount(TotalIssues);
    public int TotalStars { get => _totalStars; set { if (SetProperty(ref _totalStars, value)) OnPropertyChanged(nameof(TotalStarsFormatted)); } }
    public string TotalStarsFormatted => FormatCount(TotalStars);
    public int TotalForks { get => _totalForks; set { if (SetProperty(ref _totalForks, value)) OnPropertyChanged(nameof(TotalForksFormatted)); } }
    public string TotalForksFormatted => FormatCount(TotalForks);
    public int OwnedRepositories { get => _ownedRepositories; set { if (SetProperty(ref _ownedRepositories, value)) OnPropertyChanged(nameof(OwnedRepositoriesFormatted)); } }
    public string OwnedRepositoriesFormatted => FormatCount(OwnedRepositories);
    public int OrganizationRepositories { get => _organizationRepositories; set { if (SetProperty(ref _organizationRepositories, value)) OnPropertyChanged(nameof(OrganizationRepositoriesFormatted)); OnPropertyChanged(nameof(HasOrganizationRepositories)); } }
    public string OrganizationRepositoriesFormatted => FormatCount(OrganizationRepositories);
    public bool HasOrganizationRepositories => OrganizationRepositories > 0;
    public bool HasData {
        get => _hasData;
        set {
            if (SetProperty(ref _hasData, value)) {
                OnPropertyChanged(nameof(ShowUsernameInput));
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

    public ObservableCollection<GitHubContribBarViewModel> ContribBars { get; } = [];
    public ObservableCollection<GitHubOwnerViewModel> Owners { get; } = [];
    public ObservableCollection<GitHubRepoViewModel> TopRepos { get; } = [];

    public void ClearData() {
        Login = string.Empty;
        TotalContributions = 0;
        TotalCommits = 0;
        TotalPRs = 0;
        TotalReviews = 0;
        TotalIssues = 0;
        TotalStars = 0;
        TotalForks = 0;
        OwnerCount = 0;
        OwnedRepositories = 0;
        OrganizationRepositories = 0;
        ContribBars.Clear();
        Owners.Clear();
        TopRepos.Clear();
        HasData = false;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(ProfileUrl));
    }

    public void Apply(GitHubDashboardData data) {
        if (string.IsNullOrWhiteSpace(UsernameInput) && !string.IsNullOrWhiteSpace(data.Login)) {
            UsernameInput = data.Login;
        }

        Login = data.Login;
        OnPropertyChanged(nameof(ProfileUrl));
        var c = data.Contributions;
        TotalContributions = c.TotalContributions;
        TotalCommits = c.TotalCommits;
        TotalPRs = c.TotalPRs;
        TotalReviews = c.TotalReviews;
        TotalIssues = c.TotalIssues;

        var allRepos = data.AllRepos ?? data.TopRepos;
        var login = data.Login.Trim();
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

        OwnerCount = ownerGroups.Count;
        OwnedRepositories = allRepos.Count(repo => HasOwner(repo, login));
        OrganizationRepositories = Math.Max(0, allRepos.Count - OwnedRepositories);
        TotalStars = allRepos.Sum(r => r.Stars);
        TotalForks = allRepos.Sum(r => r.Forks);

        // Contribution bars (last 30 days)
        ContribBars.Clear();
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

        // Top repos
        TopRepos.Clear();
        foreach (var repo in data.TopRepos.Take(6)) {
            TopRepos.Add(new GitHubRepoViewModel {
                Name = repo.NameWithOwner.Contains('/') ? repo.NameWithOwner.Split('/')[1] : repo.NameWithOwner,
                FullName = repo.NameWithOwner,
                Stars = repo.Stars,
                Forks = repo.Forks,
                Language = repo.Language ?? "",
                LanguageBrush = ParseColorOrDefault(repo.LanguageColor, "#8b949e"),
                RepositoryUrl = $"https://github.com/{repo.NameWithOwner}"
            });
        }

        HasData = true;
        ErrorMessage = string.Empty;
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

    private static string FormatCount(int n) => n switch {
        >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
        >= 1_000 => $"{n / 1_000.0:F1}K",
        _ => n.ToString("N0")
    };
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
    public string RepositoryUrl { get; set; } = "";
    public int Stars { get; set; }
    public int Forks { get; set; }
    public string Language { get; set; } = "";
    public Brush LanguageBrush { get; set; } = Brushes.Gray;
    public string StarsFormatted => Stars >= 1000 ? $"{Stars / 1000.0:F1}K" : Stars.ToString("N0");
    public string ForksFormatted => Forks >= 1000 ? $"{Forks / 1000.0:F1}K" : Forks.ToString("N0");
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
