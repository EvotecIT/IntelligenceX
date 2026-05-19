using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Treatment;

/// <summary>
/// Executes generic treatment requests.
/// </summary>
public interface ITreatmentProvider {
    /// <summary>
    /// Runs a treatment request and returns provider output.
    /// </summary>
    Task<TreatmentResult> RunAsync(TreatmentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Small orchestration wrapper for treatment providers.
/// </summary>
public sealed class TreatmentEngine {
    private readonly ITreatmentProvider _provider;

    /// <summary>
    /// Initializes a new treatment engine.
    /// </summary>
    public TreatmentEngine(ITreatmentProvider provider) {
        _provider = provider ?? throw new System.ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Runs a treatment request after validating its generic contract.
    /// </summary>
    public Task<TreatmentResult> RunAsync(TreatmentRequest request, CancellationToken cancellationToken = default) {
        TreatmentPromptBuilder.Validate(request);
        return _provider.RunAsync(request, cancellationToken);
    }
}
