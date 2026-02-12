using System;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    internal static Task<SetupPostApplyVerification> ResolvePostApplyVerificationForTests(
        SetupPostApplyContext context,
        Func<Task<SetupPostApplyVerification>> runVerificationAsync) {
        return ResolvePostApplyVerificationAsync(context, runVerificationAsync);
    }

    private static Task<SetupPostApplyVerification> ResolvePostApplyVerificationAsync(
        SetupPostApplyContext context,
        Func<Task<SetupPostApplyVerification>> runVerificationAsync) {
        if (!context.ExitSuccess) {
            return Task.FromResult(SetupPostApplyVerifier.CreateFailedApplySkipped(context));
        }

        return runVerificationAsync();
    }
}
