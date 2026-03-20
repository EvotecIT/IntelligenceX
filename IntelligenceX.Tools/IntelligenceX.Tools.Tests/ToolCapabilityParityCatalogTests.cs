using System;
using System.Linq;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolCapabilityParityCatalogTests {
    [Fact]
    public void ReadOnlyExpectations_ShouldBeWellFormedAndUseUniqueCapabilityIds() {
        var descriptors = ToolCapabilityParityCatalog.ComputerXReadOnlyExpectations
            .Concat(ToolCapabilityParityCatalog.EventViewerXReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.EventViewerXGovernedWriteExpectations)
            .Concat(ToolCapabilityParityCatalog.TestimoXCoreReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.TestimoXAnalyticsReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.AdMonitoringReadOnlyExpectations)
            .ToArray();

        Assert.NotEmpty(descriptors);
        Assert.Equal(
            descriptors.Length,
            descriptors.Select(static descriptor => descriptor.CapabilityId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.All(descriptors, static descriptor => {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.CapabilityId));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ToolName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.AssemblyName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.TypeName));

            switch (descriptor.SurfaceContractKind) {
                case ToolCapabilityParitySurfaceContractKind.ToolPresent:
                    Assert.True(string.IsNullOrWhiteSpace(descriptor.SurfaceParameterName));
                    break;
                case ToolCapabilityParitySurfaceContractKind.ToolParameterPresent:
                    Assert.False(string.IsNullOrWhiteSpace(descriptor.SurfaceParameterName));
                    break;
                default:
                    throw new InvalidOperationException("Unexpected surface contract kind.");
            }

            switch (descriptor.SourceContractKind) {
                case ToolCapabilityParitySourceContractKind.TypeExists:
                    Assert.True(string.IsNullOrWhiteSpace(descriptor.PropertyName));
                    Assert.Empty(descriptor.MethodNames);
                    break;
                case ToolCapabilityParitySourceContractKind.PublicInstanceProperty:
                    Assert.False(string.IsNullOrWhiteSpace(descriptor.PropertyName));
                    Assert.Empty(descriptor.MethodNames);
                    break;
                case ToolCapabilityParitySourceContractKind.PublicStaticMethod:
                    Assert.True(string.IsNullOrWhiteSpace(descriptor.PropertyName));
                    Assert.Single(descriptor.MethodNames);
                    break;
                case ToolCapabilityParitySourceContractKind.AnyPublicStaticMethod:
                case ToolCapabilityParitySourceContractKind.AllPublicStaticMethods:
                    Assert.True(string.IsNullOrWhiteSpace(descriptor.PropertyName));
                    Assert.NotEmpty(descriptor.MethodNames);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected source contract kind.");
            }
        });
    }

    [Fact]
    public void ComputerXExpectations_ShouldRequireRemoteComputerNameParameter() {
        Assert.All(ToolCapabilityParityCatalog.ComputerXReadOnlyExpectations, static descriptor => {
            Assert.Equal(ToolCapabilityParitySurfaceContractKind.ToolParameterPresent, descriptor.SurfaceContractKind);
            Assert.Equal(ToolCapabilityParityCatalog.RemoteComputerNameParameterName, descriptor.SurfaceParameterName);
        });
    }

    [Fact]
    public void NonComputerXExpectations_ShouldUsePlainToolPresence() {
        var descriptors = ToolCapabilityParityCatalog.EventViewerXGovernedWriteExpectations
            .Concat(ToolCapabilityParityCatalog.TestimoXCoreReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.TestimoXAnalyticsReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.AdMonitoringReadOnlyExpectations);

        Assert.All(descriptors, static descriptor => {
            Assert.Equal(ToolCapabilityParitySurfaceContractKind.ToolPresent, descriptor.SurfaceContractKind);
            Assert.True(string.IsNullOrWhiteSpace(descriptor.SurfaceParameterName));
        });
    }

    [Fact]
    public void EventViewerXReadOnlyExpectations_ShouldRequireMachineNameWhenRemote() {
        Assert.All(ToolCapabilityParityCatalog.EventViewerXReadOnlyExpectations, static descriptor => {
            if (descriptor.CapabilityId.StartsWith("remote_", StringComparison.OrdinalIgnoreCase)) {
                Assert.Equal(ToolCapabilityParitySurfaceContractKind.ToolParameterPresent, descriptor.SurfaceContractKind);
                Assert.Equal(ToolCapabilityParityCatalog.RemoteMachineNameParameterName, descriptor.SurfaceParameterName);
                return;
            }

            Assert.Equal(ToolCapabilityParitySurfaceContractKind.ToolPresent, descriptor.SurfaceContractKind);
        });
    }

    [Fact]
    public void EventViewerXReadOnlyExpectations_ShouldIncludeCollectorSubscriptionInventory() {
        Assert.Contains(
            ToolCapabilityParityCatalog.EventViewerXReadOnlyExpectations,
            static descriptor => string.Equals(descriptor.CapabilityId, "remote_collector_subscription_catalog", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(descriptor.ToolName, "eventlog_collector_subscriptions_list", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EventViewerXGovernedWriteExpectations_ShouldIncludeClassicLogCleanup() {
        Assert.Contains(
            ToolCapabilityParityCatalog.EventViewerXGovernedWriteExpectations,
            static descriptor => string.Equals(descriptor.CapabilityId, "classic_log_source_remove_write", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(descriptor.ToolName, "eventlog_classic_log_remove", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            ToolCapabilityParityCatalog.EventViewerXGovernedWriteExpectations,
            static descriptor => string.Equals(descriptor.CapabilityId, "classic_log_remove_write", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(descriptor.ToolName, "eventlog_classic_log_remove", StringComparison.OrdinalIgnoreCase));
    }
}
