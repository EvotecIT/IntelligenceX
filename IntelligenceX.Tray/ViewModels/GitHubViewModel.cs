using System.Collections.ObjectModel;
using System.Windows.Media;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.ViewModels;

public sealed class GitHubViewModel : ViewModelBase {
    private string _login = "";
    private string _usernameInput = "";
    private bool _hasToken;
    private int _totalContributions;
    private int _totalCommits;
    private int _totalPRs;
    private int _totalReviews;
    private int _totalIssues;
    private int _totalStars;
    private int _totalForks;
    private bool _hasData;
    private bool _isLoading;
    private string _errorMessage = "";

    public string Login { get => _login; set => SetProperty(ref _login, value); }
    public string UsernameInput { get => _usernameInput; set => SetProperty(ref _usernameInput, value); }
    public bool HasToken {
        get => _hasToken;
        set {
            if (SetProperty(ref _hasToken, value)) {
                OnPropertyChanged(nameof(NeedsUsername));
            }
        }
    }
    public bool NeedsUsername => !HasToken;
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
    public bool HasData {
        get => _hasData;
        set {
            if (SetProperty(ref _hasData, value)) {
                OnPropertyChanged(nameof(NeedsUsername));
            }
        }
    }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public ObservableCollection<GitHubContribBarViewModel> ContribBars { get; } = [];
    public ObservableCollection<GitHubRepoViewModel> TopRepos { get; } = [];

    public void Apply(GitHubDashboardData data) {
        if (string.IsNullOrWhiteSpace(UsernameInput) && !string.IsNullOrWhiteSpace(data.Login)) {
            UsernameInput = data.Login;
        }

        Login = data.Login;
        var c = data.Contributions;
        TotalContributions = c.TotalContributions;
        TotalCommits = c.TotalCommits;
        TotalPRs = c.TotalPRs;
        TotalReviews = c.TotalReviews;
        TotalIssues = c.TotalIssues;

        // Stars/forks totals
        TotalStars = data.TopRepos.Sum(r => r.Stars);
        TotalForks = data.TopRepos.Sum(r => r.Forks);

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

        // Top repos
        TopRepos.Clear();
        foreach (var repo in data.TopRepos.Take(6)) {
            TopRepos.Add(new GitHubRepoViewModel {
                Name = repo.NameWithOwner.Contains('/') ? repo.NameWithOwner.Split('/')[1] : repo.NameWithOwner,
                FullName = repo.NameWithOwner,
                Stars = repo.Stars,
                Forks = repo.Forks,
                Language = repo.Language ?? "",
                LanguageBrush = ParseColorOrDefault(repo.LanguageColor, "#8b949e")
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
    public int Stars { get; set; }
    public int Forks { get; set; }
    public string Language { get; set; } = "";
    public Brush LanguageBrush { get; set; } = Brushes.Gray;
    public string StarsFormatted => Stars >= 1000 ? $"{Stars / 1000.0:F1}K" : Stars.ToString("N0");
    public string ForksFormatted => Forks >= 1000 ? $"{Forks / 1000.0:F1}K" : Forks.ToString("N0");
}
