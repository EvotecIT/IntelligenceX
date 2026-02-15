using System;
using System.Linq;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestRepositoryLabelManagerBuildManagedLabelSyncPlanAddsAndRemovesManagedOnly() {
        var desired = new[] {
            "ix/category:feature",
            "ix/tag:api",
            "ix/match:linked-issue"
        };
        var current = new[] {
            "ix/category:bug",
            "ix/tag:api",
            "ix/decision:defer",
            "bug",
            "help wanted"
        };

        var plan = IntelligenceX.Cli.Todo.RepositoryLabelManager.BuildManagedLabelSyncPlan(desired, current);

        AssertEqual(true, plan.LabelsToAdd.Contains("ix/category:feature", StringComparer.OrdinalIgnoreCase), "managed add includes desired missing label");
        AssertEqual(true, plan.LabelsToAdd.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "managed add includes desired match label");
        AssertEqual(true, plan.LabelsToRemove.Contains("ix/category:bug", StringComparer.OrdinalIgnoreCase), "managed remove includes stale category label");
        AssertEqual(true, plan.LabelsToRemove.Contains("ix/decision:defer", StringComparer.OrdinalIgnoreCase), "managed remove includes stale decision label");
        AssertEqual(false, plan.LabelsToRemove.Contains("bug", StringComparer.OrdinalIgnoreCase), "non-managed labels are preserved");
        AssertEqual(false, plan.LabelsToRemove.Contains("help wanted", StringComparer.OrdinalIgnoreCase), "non-managed labels are preserved");
    }

    private static void TestRepositoryLabelManagerBuildManagedLabelSyncPlanNoChangesWhenAligned() {
        var desired = new[] { "ix/category:feature", "ix/tag:api" };
        var current = new[] { "ix/category:feature", "ix/tag:api", "enhancement" };

        var plan = IntelligenceX.Cli.Todo.RepositoryLabelManager.BuildManagedLabelSyncPlan(desired, current);

        AssertEqual(0, plan.LabelsToAdd.Count, "no managed adds");
        AssertEqual(0, plan.LabelsToRemove.Count, "no managed removes");
    }
#endif
}
