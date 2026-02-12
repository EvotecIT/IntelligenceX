using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service.Profiles;

/// <summary>
/// Storage for named <see cref="ServiceProfile"/> presets.
/// </summary>
internal interface IServiceProfileStore {
    Task<ServiceProfile?> GetAsync(string name, CancellationToken cancellationToken);
    Task UpsertAsync(string name, ServiceProfile profile, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken);
}

