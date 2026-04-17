using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.Core.Tests;

public sealed class StudyShellCatalogLoaderTests
{
    [Fact]
    public async Task LoadAsync_MapsStudyShellManifestAndDefinition()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            var loader = new StudyShellCatalogLoader();
            var catalog = await loader.LoadAsync(root);

            Assert.Equal("Public study shells", catalog.Source.Label);
            Assert.Equal("dope-projected-feed-colorama", catalog.LaunchOptions.StartupStudyId);
            Assert.True(catalog.LaunchOptions.LockToStartupStudy);
            var study = Assert.Single(catalog.Studies);
            Assert.Equal("dope-projected-feed-colorama", study.Id);
            Assert.Equal("com.tillh.dynamicoscillatorypatternentrainment", study.App.PackageId);
            Assert.False(study.App.AllowManualSelection);
            Assert.True(study.App.LaunchInKioskMode);
            Assert.NotNull(study.App.VerificationBaseline);
            Assert.Equal("14", study.App.VerificationBaseline!.SoftwareVersion);
            Assert.Equal("2921110053000610", study.App.VerificationBaseline.BuildId);
            Assert.Equal("2", study.Controls.RecenterCommandActionId);
            Assert.Equal("14", study.Controls.StartBreathingCalibrationActionId);
            Assert.Equal("20", study.Controls.SetBreathingModeControllerVolumeActionId);
            Assert.Equal("46", study.Controls.SetBreathingModeAutomaticCycleActionId);
            Assert.Equal("47", study.Controls.StartAutomaticBreathingActionId);
            Assert.Equal("48", study.Controls.PauseAutomaticBreathingActionId);
            Assert.Contains("signal01.mock_pacer_breathing", study.Monitoring.AutomaticBreathingValueKeys);
            Assert.Contains("debug.oculus.gpuLevel", study.DeviceProfile.Properties.Keys);
            Assert.Contains("connection.lsl.connected_count", study.Monitoring.LslConnectivityKeys);
            Assert.Contains("signal01.coherence_lsl", study.Monitoring.LslValueKeys);
            Assert.Contains("study.performance.fps", study.Monitoring.PerformanceFpsKeys);
            Assert.Equal(0.2d, study.Monitoring.RecenterDistanceThresholdUnits);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ResolvesRelativeApkPathAgainstDefinitionDirectory()
    {
        var root = CreateStudyShellCatalog(withRelativeApkPath: true);

        try
        {
            var loader = new StudyShellCatalogLoader();
            var catalog = await loader.LoadAsync(root);
            var study = Assert.Single(catalog.Studies);

            Assert.True(Path.IsPathRooted(study.App.ApkPath));
            Assert.EndsWith(Path.Combine("payload", "Dope.apk"), study.App.ApkPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsStudyShellWithMissingPinnedPackageId()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "dope.json"),
                """
                {
                  "id": "dope-projected-feed-colorama",
                  "label": "Dope University",
                  "partner": "University of Dope",
                  "description": "Controller breathing study shell.",
                  "app": {
                    "label": "Dope Controller Study APK",
                    "packageId": "",
                    "apkPath": "payload/Dope.apk",
                    "launchComponent": "com.tillh.dynamicoscillatorypatternentrainment/com.unity3d.player.UnityPlayerGameActivity",
                    "sha256": "ABC123",
                    "versionName": "0.1.0",
                    "notes": "Bundled Dope APK.",
                    "allowManualSelection": false,
                    "launchInKioskMode": true
                  },
                  "deviceProfile": {
                    "id": "dope-study-profile",
                    "label": "Dope Study Device Profile",
                    "description": "Quest study settings.",
                    "properties": {
                      "debug.oculus.cpuLevel": "2"
                    }
                  },
                  "monitoring": {
                    "expectedLslStreamName": "HRV_Biofeedback",
                    "expectedLslStreamType": "HRV"
                  },
                  "controls": {
                    "recenterCommandActionId": "2",
                    "particleVisibleOnActionId": "39",
                    "particleVisibleOffActionId": "40",
                    "startBreathingCalibrationActionId": "14",
                    "resetBreathingCalibrationActionId": "41",
                    "startExperimentActionId": "42",
                    "endExperimentActionId": "43",
                    "setBreathingModeControllerVolumeActionId": "20",
                    "setBreathingModeAutomaticCycleActionId": "46",
                    "startAutomaticBreathingActionId": "47",
                    "pauseAutomaticBreathingActionId": "48"
                  }
                }
                """);

            var loader = new StudyShellCatalogLoader();
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync(root));
            Assert.Contains("app.packageId", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsStudyShellWithMissingRequiredCommandId()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "dope.json"),
                """
                {
                  "id": "dope-projected-feed-colorama",
                  "label": "Dope University",
                  "partner": "University of Dope",
                  "description": "Controller breathing study shell.",
                  "app": {
                    "label": "Dope Controller Study APK",
                    "packageId": "com.tillh.dynamicoscillatorypatternentrainment",
                    "apkPath": "payload/Dope.apk",
                    "launchComponent": "com.tillh.dynamicoscillatorypatternentrainment/com.unity3d.player.UnityPlayerGameActivity",
                    "sha256": "ABC123",
                    "versionName": "0.1.0",
                    "notes": "Bundled Dope APK.",
                    "allowManualSelection": false,
                    "launchInKioskMode": true
                  },
                  "deviceProfile": {
                    "id": "dope-study-profile",
                    "label": "Dope Study Device Profile",
                    "description": "Quest study settings.",
                    "properties": {
                      "debug.oculus.cpuLevel": "2"
                    }
                  },
                  "monitoring": {
                    "expectedLslStreamName": "HRV_Biofeedback",
                    "expectedLslStreamType": "HRV"
                  },
                  "controls": {
                    "recenterCommandActionId": "",
                    "particleVisibleOnActionId": "39",
                    "particleVisibleOffActionId": "40",
                    "startBreathingCalibrationActionId": "14",
                    "resetBreathingCalibrationActionId": "41",
                    "startExperimentActionId": "42",
                    "endExperimentActionId": "43",
                    "setBreathingModeControllerVolumeActionId": "20",
                    "setBreathingModeAutomaticCycleActionId": "46",
                    "startAutomaticBreathingActionId": "47",
                    "pauseAutomaticBreathingActionId": "48"
                  }
                }
                """);

            var loader = new StudyShellCatalogLoader();
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync(root));
            Assert.Contains("controls.recenterCommandActionId", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateStudyShellCatalog(bool withRelativeApkPath = false)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var payloadDirectory = Path.Combine(root, "payload");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(payloadDirectory);

        File.WriteAllText(
            Path.Combine(root, "manifest.json"),
            """
            {
              "label": "Public study shells",
              "startupStudyId": "dope-projected-feed-colorama",
              "lockToStartupStudy": true,
              "studies": [
                { "file": "dope.json" }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "dope.json"),
            $$"""
            {
              "id": "dope-projected-feed-colorama",
              "label": "Dope University",
              "partner": "University of Dope",
              "description": "Controller breathing study shell.",
              "app": {
                "label": "Dope Controller Study APK",
                "packageId": "com.tillh.dynamicoscillatorypatternentrainment",
                "apkPath": "payload/Dope.apk",
                "launchComponent": "com.tillh.dynamicoscillatorypatternentrainment/com.unity3d.player.UnityPlayerGameActivity",
                "sha256": "ABC123",
                "versionName": "0.1.0",
                "notes": "Bundled Dope APK.",
                "allowManualSelection": false,
                "launchInKioskMode": true,
                "verification": {
                  "apkSha256": "ABC123",
                  "softwareVersion": "14",
                  "buildId": "2921110053000610",
                  "displayId": "UP1A.231005.007.A1",
                  "deviceProfileId": "dope-study-profile",
                  "environmentHash": "CAFEBABE",
                  "verifiedAtUtc": "2026-03-29T10:15:00Z",
                  "verifiedBy": "tools/DopeCompanion.VerificationHarness"
                }
              },
              "deviceProfile": {
                "id": "dope-study-profile",
                "label": "Dope Study Device Profile",
                "description": "Quest study settings.",
                "properties": {
                  "debug.oculus.cpuLevel": "2",
                  "debug.oculus.gpuLevel": "5"
                }
              },
              "monitoring": {
                "expectedBreathingLabel": "",
                "expectedHeartbeatLabel": "",
                "expectedCoherenceLabel": "",
                "expectedLslStreamName": "{{HrvBiofeedbackStreamContract.StreamName}}",
                "expectedLslStreamType": "{{HrvBiofeedbackStreamContract.StreamType}}",
                "recenterDistanceThresholdUnits": 0.2,
                "lslConnectivityKeys": ["connection.lsl.connected_count"],
                "lslStreamNameKeys": ["showcase_lsl_in_stream_name"],
                "lslStreamTypeKeys": ["showcase_lsl_in_stream_type"],
                "lslValueKeys": ["signal01.coherence_lsl"],
                "controllerValueKeys": ["tracker.breathing.controller.volume01"],
                "controllerStateKeys": ["tracker.breathing.controller.state"],
                "controllerTrackingKeys": ["tracker.breathing.controller.active"],
                "automaticBreathingValueKeys": ["signal01.mock_pacer_breathing"],
                "heartbeatValueKeys": ["signal01.heartbeat_lsl"],
                "heartbeatStateKeys": ["routing.heartbeat.mode"],
                "coherenceValueKeys": ["signal01.coherence_lsl"],
                "coherenceStateKeys": ["routing.coherence.mode"],
                "performanceFpsKeys": ["study.performance.fps"],
                "performanceFrameTimeKeys": ["study.performance.frame_ms"],
                "performanceTargetFpsKeys": ["study.performance.target_fps"],
                "performanceRefreshRateKeys": ["study.performance.refresh_hz"],
                "recenterDistanceKeys": [],
                "particleVisibilityKeys": []
              },
              "controls": {
                "recenterCommandActionId": "2",
                "particleVisibleOnActionId": "39",
                "particleVisibleOffActionId": "40",
                "startBreathingCalibrationActionId": "14",
                "resetBreathingCalibrationActionId": "41",
                "startExperimentActionId": "42",
                "endExperimentActionId": "43",
                "setBreathingModeControllerVolumeActionId": "20",
                "setBreathingModeAutomaticCycleActionId": "46",
                "startAutomaticBreathingActionId": "47",
                "pauseAutomaticBreathingActionId": "48"
              }
            }
            """);

        File.WriteAllBytes(Path.Combine(payloadDirectory, "Dope.apk"), [0x50, 0x4B, 0x03, 0x04]);

        return root;
    }
}

