using System.Collections.Generic;
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
    /// Lists known bundles for the specified provider.
    /// </summary>
    /// <param name="provider">Provider identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default);
    /// <summary>
    /// Saves an authentication bundle.
    /// </summary>
    /// <param name="bundle">Bundle to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default);

}

/// <summary>Optional account-removal capability for stores that support persistent sign-out.</summary>
public interface IRemovableAuthBundleStore : IAuthBundleStore {
    /// <summary>Removes only the specified provider and account. A null account selects only the provider's accountless entry.</summary>
    Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default);
}

/// <summary>Operations that require an optional authentication-store capability.</summary>
public static class AuthBundleStoreExtensions {
    /// <summary>Removes a stored account, or reports that the configured store does not support persistent sign-out.</summary>
    /// <exception cref="System.NotSupportedException">The store does not implement <see cref="IRemovableAuthBundleStore"/>.</exception>
    public static Task RemoveAsync(this IAuthBundleStore store, string provider, string? accountId, CancellationToken cancellationToken = default) {
        if (store is null) throw new System.ArgumentNullException(nameof(store));
        cancellationToken.ThrowIfCancellationRequested();
        if (store is not IRemovableAuthBundleStore removable)
            throw new System.NotSupportedException("The authentication store must implement IRemovableAuthBundleStore for persistent sign-out.");
        return removable.RemoveAsync(provider, accountId, cancellationToken);
    }
}
