using System.Collections.Generic;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecycleToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_user_lifecycle"] = new[] {
                "provision users with initial group memberships, enable or disable accounts, offboard leavers with access cleanup, delete accounts, or reset passwords with dry-run-first governance"
            },
            ["ad_computer_lifecycle"] = new[] {
                "provision, update, enable, disable, delete, or reset passwords for Active Directory computer accounts with dry-run-first governance"
            },
            ["ad_group_lifecycle"] = new[] {
                "provision, update, delete, and manage memberships for Active Directory groups with dry-run-first governance"
            }
        };
}
