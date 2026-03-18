using System.Collections.Generic;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecycleToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_user_lifecycle"] = new[] {
                "provision joiners with initial group memberships, update user profile attributes, move users between organizational units, disable or offboard leavers with access cleanup, delete accounts, or reset passwords with dry-run-first governance",
                "prepare onboarding, mover, and leaver workflows with dry-run-first governance and approval-oriented execution"
            },
            ["ad_computer_lifecycle"] = new[] {
                "provision, rename, update, enable, disable, delete, or reset passwords for Active Directory computer accounts with dry-run-first governance"
            },
            ["ad_group_lifecycle"] = new[] {
                "provision, rename, delete, and manage memberships for Active Directory groups with dry-run-first governance",
                "prepare governed membership changes for onboarding, mover, and offboarding processes"
            },
            ["ad_ou_lifecycle"] = new[] {
                "create, rename, move, protect, update, or delete Active Directory organizational units with dry-run-first governance",
                "prepare quarantine, onboarding, and departmental OU changes without falling back to generic shell execution"
            }
        };
}
