using System;
using System.Linq;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolCapabilityParityCatalogTests {
    [Fact]
    public void ReadOnlyExpectations_ShouldBeWellFormedAndUseUniqueCapabilityIds() {
        var descriptors = ToolCapabilityParityCatalog.ComputerXReadOnlyExpectations
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
        var descriptors = ToolCapabilityParityCatalog.TestimoXCoreReadOnlyExpectations
            .Concat(ToolCapabilityParityCatalog.TestimoXAnalyticsReadOnlyExpectations)
            .Concat(ToolCapabilityParityCatalog.AdMonitoringReadOnlyExpectations);

        Assert.All(descriptors, static descriptor => {
            Assert.Equal(ToolCapabilityParitySurfaceContractKind.ToolPresent, descriptor.SurfaceContractKind);
            Assert.True(string.IsNullOrWhiteSpace(descriptor.SurfaceParameterName));
        });
    }
}
