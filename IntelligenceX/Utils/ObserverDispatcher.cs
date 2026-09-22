using System;
using System.Collections.Generic;

namespace IntelligenceX.Utils;

/// <summary>Delivers observational notifications without allowing one subscriber to change an operation's outcome.</summary>
internal static class ObserverDispatcher {
    public static void Raise<T>(EventHandler<T>? observers, object sender, T args) {
        if (observers is null) return;
        foreach (EventHandler<T> observer in observers.GetInvocationList()) {
            try { observer(sender, args); }
            catch (Exception) { /* Observers do not own transport, cancellation, or completion semantics. */ }
        }
    }

    public static void Dispatch<T>(IEnumerable<Action<T>> observers, T value) {
        foreach (var observer in observers) {
            try { observer(value); }
            catch (Exception) { /* Continue delivery to internal waiters and remaining observers. */ }
        }
    }
}
