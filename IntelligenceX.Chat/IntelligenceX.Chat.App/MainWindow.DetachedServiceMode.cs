using System;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private static bool ResolveDetachedServiceMode() {
        var forceDetached = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_DETACHED_SERVICE"));
        if (forceDetached) {
            return true;
        }

        var forceAttached = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_ATTACHED_SERVICE"));
        if (forceAttached) {
            return false;
        }

        // Default to detached so pipe reconnects do not force sidecar process restarts.
        return true;
    }
}
