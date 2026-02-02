using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Storage abstraction for auth bundles.
/// </summary>
public interface IAuthBundleStore {
    /// <summary>
    /// Loads a bundle for the provider (optionally by account id).
    /// </summary>
    Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Saves a bundle.
    /// </summary>
    Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default);
}
