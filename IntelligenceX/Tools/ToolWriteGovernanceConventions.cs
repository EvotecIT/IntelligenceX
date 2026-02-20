using System;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared factories for common write-governance contract patterns.
/// </summary>
public static class ToolWriteGovernanceConventions {
    /// <summary>
    /// Creates a write-governance contract where write intent is a boolean flag set to true.
    /// </summary>
    public static ToolWriteGovernanceContract BooleanFlagTrue(
        string intentArgumentName,
        string? confirmationArgumentName = null) {
        if (string.IsNullOrWhiteSpace(intentArgumentName)) {
            throw new ArgumentException("Intent argument name is required.", nameof(intentArgumentName));
        }

        var intent = intentArgumentName.Trim();
        var confirmation = string.IsNullOrWhiteSpace(confirmationArgumentName)
            ? intent
            : confirmationArgumentName!.Trim();

        return new ToolWriteGovernanceContract {
            IsWriteCapable = true,
            RequiresGovernanceAuthorization = true,
            GovernanceContractId = ToolWriteGovernanceContract.DefaultContractId,
            IntentMode = ToolWriteIntentMode.BooleanFlagTrue,
            IntentArgumentName = intent,
            RequireExplicitConfirmation = true,
            ConfirmationArgumentName = confirmation
        };
    }

    /// <summary>
    /// Creates a write-governance contract where write intent is inferred by string-equals match.
    /// </summary>
    public static ToolWriteGovernanceContract StringEquals(
        string intentArgumentName,
        string intentStringValue,
        string confirmationArgumentName = "allow_write") {
        if (string.IsNullOrWhiteSpace(intentArgumentName)) {
            throw new ArgumentException("Intent argument name is required.", nameof(intentArgumentName));
        }
        if (string.IsNullOrWhiteSpace(intentStringValue)) {
            throw new ArgumentException("Intent string value is required.", nameof(intentStringValue));
        }
        if (string.IsNullOrWhiteSpace(confirmationArgumentName)) {
            throw new ArgumentException("Confirmation argument name is required.", nameof(confirmationArgumentName));
        }

        return new ToolWriteGovernanceContract {
            IsWriteCapable = true,
            RequiresGovernanceAuthorization = true,
            GovernanceContractId = ToolWriteGovernanceContract.DefaultContractId,
            IntentMode = ToolWriteIntentMode.StringEquals,
            IntentArgumentName = intentArgumentName.Trim(),
            IntentStringValue = intentStringValue.Trim(),
            RequireExplicitConfirmation = true,
            ConfirmationArgumentName = confirmationArgumentName.Trim()
        };
    }
}
