using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolAuthenticationProbeValidatorTests {
    [Fact]
    public void Validate_ProbeNotRequired_ShouldSucceed() {
        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = false
            });

        Assert.True(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.None, result.Failure);
    }

    [Fact]
    public void Validate_MissingProbeId_ShouldFailAsRequired() {
        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeStore = new InMemoryToolAuthenticationProbeStore()
            });

        Assert.False(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.ProbeRequired, result.Failure);
    }

    [Fact]
    public void Validate_MissingStore_ShouldFailAsUnavailable() {
        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeId = "probe-1"
            });

        Assert.False(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.ProbeStoreUnavailable, result.Failure);
    }

    [Fact]
    public void Validate_ProbeNotFound_ShouldFailAsNotFound() {
        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeStore = new InMemoryToolAuthenticationProbeStore(),
                ProbeId = "missing"
            });

        Assert.False(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.ProbeNotFound, result.Failure);
    }

    [Fact]
    public void Validate_MismatchedTargetFingerprint_ShouldFailAsIncompatible() {
        var store = new InMemoryToolAuthenticationProbeStore();
        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-1",
            ToolName = "email_smtp_probe",
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = "server-a|587|user|auto|use_ssl=0",
            ProbedAtUtc = DateTimeOffset.UtcNow,
            IsSuccessful = true
        });

        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeStore = store,
                ProbeId = "probe-1",
                ExpectedProbeToolName = "email_smtp_probe",
                ExpectedAuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
                ExpectedTargetFingerprint = "server-b|587|user|auto|use_ssl=0",
                MaxAge = TimeSpan.FromMinutes(5),
                NowUtc = DateTimeOffset.UtcNow
            });

        Assert.False(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.ProbeIncompatible, result.Failure);
    }

    [Fact]
    public void Validate_ExpiredProbe_ShouldFailAsExpired() {
        var store = new InMemoryToolAuthenticationProbeStore();
        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-1",
            ToolName = "email_smtp_probe",
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = "server-a|587|user|auto|use_ssl=0",
            ProbedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
            IsSuccessful = true
        });

        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeStore = store,
                ProbeId = "probe-1",
                ExpectedProbeToolName = "email_smtp_probe",
                ExpectedAuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
                ExpectedTargetFingerprint = "server-a|587|user|auto|use_ssl=0",
                MaxAge = TimeSpan.FromMinutes(5),
                NowUtc = DateTimeOffset.UtcNow
            });

        Assert.False(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.ProbeExpired, result.Failure);
    }

    [Fact]
    public void Validate_MatchingProbe_ShouldSucceed() {
        var store = new InMemoryToolAuthenticationProbeStore();
        var record = new ToolAuthenticationProbeRecord {
            ProbeId = "probe-1",
            ToolName = "email_smtp_probe",
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = "server-a|587|user|auto|use_ssl=0",
            ProbedAtUtc = DateTimeOffset.UtcNow,
            IsSuccessful = true
        };
        store.Upsert(record);

        ToolAuthenticationProbeValidationResult result = ToolAuthenticationProbeValidator.Validate(
            new ToolAuthenticationProbeValidationRequest {
                RequireProbe = true,
                ProbeStore = store,
                ProbeId = "probe-1",
                ExpectedProbeToolName = "email_smtp_probe",
                ExpectedAuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
                ExpectedTargetFingerprint = "server-a|587|user|auto|use_ssl=0",
                MaxAge = TimeSpan.FromMinutes(5),
                NowUtc = DateTimeOffset.UtcNow
            });

        Assert.True(result.IsValid);
        Assert.Equal(ToolAuthenticationProbeValidationFailure.None, result.Failure);
        Assert.NotNull(result.ProbeRecord);
        Assert.Equal(record.ProbeId, result.ProbeRecord!.ProbeId);
    }
}
