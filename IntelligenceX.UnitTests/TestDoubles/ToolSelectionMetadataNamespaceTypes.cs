using System;

namespace IntelligenceX.UnitTests.TestDoubles {
    // Keep namespace inference fixtures out of production package namespaces
    // while preserving ".System"/".EventLog" substrings used by inference.
    internal static class ToolSelectionMetadataNamespaceTypes {
        internal static Type SystemDecoratorType => typeof(NamespaceBuckets.SystemCategory.FakeSystemDecorator);
        internal static Type EventLogDecoratorType => typeof(NamespaceBuckets.EventLogCategory.FakeEventLogDecorator);
        internal static Type ReviewerSetupDecoratorType => typeof(NamespaceBuckets.ReviewerSetupCategory.FakeReviewerSetupDecorator);
    }
}

namespace IntelligenceX.UnitTests.TestDoubles.NamespaceBuckets.SystemCategory {
    internal sealed class FakeSystemDecorator { }
}

namespace IntelligenceX.UnitTests.TestDoubles.NamespaceBuckets.EventLogCategory {
    internal sealed class FakeEventLogDecorator { }
}

namespace IntelligenceX.UnitTests.TestDoubles.NamespaceBuckets.ReviewerSetupCategory {
    internal sealed class FakeReviewerSetupDecorator { }
}
