using System.Security.Cryptography;

namespace Zlet.Quality.Core;

public sealed record ArtifactVerification(
    string Path,
    string Status,
    string ExpectedSha256,
    string? ActualSha256,
    long ExpectedSizeBytes,
    long? ActualSizeBytes);

public sealed record QualityRunVerification(
    string SchemaVersion,
    string RunId,
    string EvidenceKey,
    string Status,
    IReadOnlyList<ArtifactVerification> Artifacts);

public sealed class QualityRunVerifier
{
    public async Task<QualityRunVerification> VerifyAsync(
        string runDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        var root = Path.GetFullPath(runDirectory);
        var manifestPath = Path.Combine(root, "run.json");

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Quality run manifest was not found.",
                manifestPath);
        }

        var manifestJson = await File.ReadAllTextAsync(
            manifestPath,
            cancellationToken);

        var manifest = QualityJson.Deserialize<QualityRunManifest>(manifestJson);
        var artifacts = new List<ArtifactVerification>();

        artifacts.Add(await VerifyArtifactAsync(
            root,
            manifest.Source.RelativePath,
            manifest.Source.Sha256,
            manifest.Source.SizeBytes,
            cancellationToken));

        artifacts.Add(await VerifyArtifactAsync(
            root,
            manifest.Markdown.RelativePath,
            manifest.Markdown.Sha256,
            manifest.Markdown.SizeBytes,
            cancellationToken));

        foreach (var stage in manifest.Stages)
        {
            artifacts.Add(await VerifyArtifactAsync(
                root,
                stage.ArtifactPath,
                stage.ArtifactSha256,
                stage.SizeBytes,
                cancellationToken));
        }

        var status = artifacts.All(item => item.Status == "PASS")
            ? "PASS"
            : "FAIL";

        return new QualityRunVerification(
            QualityCoreVersion.RunVerificationSchemaVersion,
            manifest.RunId,
            manifest.EvidenceKey,
            status,
            artifacts);
    }

    private static async Task<ArtifactVerification> VerifyArtifactAsync(
        string root,
        string relativePath,
        string expectedSha256,
        long expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveInsideRoot(root, relativePath);
        if (resolved is null)
        {
            return new ArtifactVerification(
                relativePath,
                "INVALID_PATH",
                expectedSha256,
                null,
                expectedSizeBytes,
                null);
        }

        if (!File.Exists(resolved))
        {
            return new ArtifactVerification(
                relativePath,
                "MISSING",
                expectedSha256,
                null,
                expectedSizeBytes,
                null);
        }

        var actualSize = new FileInfo(resolved).Length;
        var actualHash = await ComputeSha256Async(resolved, cancellationToken);
        var status =
            actualSize == expectedSizeBytes &&
            string.Equals(
                actualHash,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase)
                ? "PASS"
                : "MISMATCH";

        return new ArtifactVerification(
            relativePath,
            status,
            expectedSha256,
            actualHash,
            expectedSizeBytes,
            actualSize);
    }

    private static string? ResolveInsideRoot(
        string root,
        string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var localRelative = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);

        var candidate = Path.GetFullPath(
            Path.Combine(root, localRelative));

        var relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." ||
            relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return null;
        }

        return candidate;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
