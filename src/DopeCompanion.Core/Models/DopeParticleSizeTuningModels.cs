namespace DopeCompanion.Core.Models;

public sealed record DopeParticleSizeTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    string HotloadTargetKey,
    DopeParticleSizeTuningControl ParticleSizeMinimum,
    DopeParticleSizeTuningControl ParticleSizeMaximum);

public sealed record DopeParticleSizeTuningControl(
    string Id,
    string Label,
    double Value,
    double BaselineValue,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeJsonField);

public sealed record DopeParticleSizeTuningCompileResult(
    DopeParticleSizeTuningDocument Document,
    string CompactRuntimeConfigJson,
    string PrettyRuntimeConfigJson,
    string HotloadTargetKey);

