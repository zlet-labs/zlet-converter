namespace Zlet.Quality.Core;

public sealed record FileArtifact(
    string RelativePath,
    string Sha256,
    long SizeBytes,
    string Format);

public sealed record PipelineStage(
    string Name,
    string Status,
    string ArtifactPath,
    string ArtifactSha256,
    long SizeBytes);

public sealed record CorpusIdentity(
    string Id,
    string Version);

public sealed record ConversionEngineIdentity(
    string Name,
    string? Version,
    string? BuildIdentifier,
    string? BinarySha256,
    string Profile,
    string? Configuration);

public sealed record RuntimeIdentity(
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    string FrameworkDescription);

public sealed record ComponentIdentity(
    string Id,
    string Version);

public sealed record QualityPipelineIdentity(
    string QualityCoreVersion,
    ComponentIdentity SourceInspector,
    ComponentIdentity MarkdownInspector,
    ComponentIdentity Comparator,
    string MatcherVersion,
    string MetricsVersion,
    string ReportSchemaVersion);

public sealed record QualityRunManifest(
    string SchemaVersion,
    string QualityCoreVersion,
    string RunId,
    string EvidenceKey,
    CorpusIdentity Corpus,
    ConversionEngineIdentity Engine,
    RuntimeIdentity Runtime,
    QualityPipelineIdentity Pipeline,
    FileArtifact Source,
    FileArtifact Markdown,
    IReadOnlyList<PipelineStage> Stages);

public sealed record QualityRunOptions(
    string? RunId = null,
    string CorpusId = "ad-hoc",
    string CorpusVersion = "unversioned",
    string EngineName = "unidentified",
    string? EngineVersion = null,
    string? EngineBuildIdentifier = null,
    string? EngineBinarySha256 = null,
    string EngineProfile = "unknown",
    string? EngineConfiguration = null);
