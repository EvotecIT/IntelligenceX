using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppLifecycle;

namespace IntelligenceX.Chat.App;

/// <summary>Uses the Windows lifecycle owner to redirect launches within one installation and UI mode.</summary>
internal static class ChatAppActivation {
    internal static event Action? OpenRequested;
    private static AppInstance? _instance;

    internal static bool RegisterOrRedirect() {
        var mode = ChatAppLaunchModeResolver.Resolve(Environment.GetEnvironmentVariable);
        // Development/preview copies must never activate an unrelated installed build.
        var directory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var key = "IntelligenceX.Chat." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory))) + "." + mode;
        var current = AppInstance.GetCurrent();
        _instance = AppInstance.FindOrRegisterForKey(key);
        if (!_instance.IsCurrent) {
            var activation = current.GetActivatedEventArgs();
            // RedirectActivationToAsync must not block an STA waiting for its own message pump.
            Task.Run(async () => await _instance.RedirectActivationToAsync(activation)).GetAwaiter().GetResult();
            return false;
        }
        _instance.Activated += (_, _) => OpenRequested?.Invoke();
        return true;
    }
}
