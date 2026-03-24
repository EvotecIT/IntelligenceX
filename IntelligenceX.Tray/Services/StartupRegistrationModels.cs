namespace IntelligenceX.Tray.Services;

public enum StartupRegistrationKind {
    Unsupported = 0,
    RegistryRunKey = 1,
    PackagedStartupTask = 2
}

public sealed record StartupRegistrationState(
    bool IsEnabled,
    bool CanChange,
    bool RequiresManualAction,
    StartupRegistrationKind Kind,
    string? Message = null);

public sealed record StartupRegistrationChangeResult(
    bool Applied,
    StartupRegistrationState State,
    string? Message = null);
