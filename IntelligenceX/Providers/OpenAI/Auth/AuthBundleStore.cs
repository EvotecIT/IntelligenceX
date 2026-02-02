using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Abstraction for storing authentication bundles.
/// </summary>
public interface IAuthBundleStore {
    /// <summary>
    /// Retrieves a bundle for the specified provider and account.
    /// </summary>
    /// <param name="provider">Provider identifier.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Saves an authentication bundle.
    /// </summary>
    /// <param name="bundle">Bundle to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default);
}
