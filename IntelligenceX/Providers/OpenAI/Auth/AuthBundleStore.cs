using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

public interface IAuthBundleStore {
    Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default);
    Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default);
}
