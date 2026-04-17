using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.Core.Tests;

public sealed class StudyVerificationFingerprintTests
{
    [Fact]
    public void Compute_ReturnsStableHashForEquivalentValues()
    {
        var left = StudyVerificationFingerprint.Compute(
            "com.tillh.dynamicoscillatorypatternentrainment",
            "ABC123",
            "14",
            "2921110053000610",
            "dope-study-profile",
            "UP1A.231005.007.A1");
        var right = StudyVerificationFingerprint.Compute(
            " com.tillh.dynamicoscillatorypatternentrainment ",
            "abc123",
            " 14 ",
            "2921110053000610",
            "DOPE-STUDY-PROFILE",
            "up1a.231005.007.a1");

        Assert.Equal(left, right);
    }

    [Fact]
    public void Compute_ChangesWhenDisplayIdChanges()
    {
        var left = StudyVerificationFingerprint.Compute(
            "com.tillh.dynamicoscillatorypatternentrainment",
            "ABC123",
            "14",
            "2921110053000610",
            "dope-study-profile",
            "UP1A.231005.007.A1");
        var right = StudyVerificationFingerprint.Compute(
            "com.tillh.dynamicoscillatorypatternentrainment",
            "ABC123",
            "14",
            "2921110053000610",
            "dope-study-profile",
            "UP1A.231005.007.B1");

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Matches_UsesStoredBaselineIdentity()
    {
        var baseline = new StudyVerificationBaseline(
            ApkSha256: "ABC123",
            SoftwareVersion: "14",
            BuildId: "2921110053000610",
            DisplayId: "UP1A.231005.007.A1",
            DeviceProfileId: "dope-study-profile",
            EnvironmentHash: "",
            VerifiedAtUtc: DateTimeOffset.Parse("2026-03-29T10:15:00Z"),
            VerifiedBy: "test");

        Assert.True(StudyVerificationFingerprint.Matches(
            baseline,
            "com.tillh.dynamicoscillatorypatternentrainment",
            "ABC123",
            "14",
            "2921110053000610",
            "dope-study-profile",
            "UP1A.231005.007.A1"));
    }
}

