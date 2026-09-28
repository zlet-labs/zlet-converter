namespace Zlet.Quality.Core;

public interface ISourceInspector
{
    string InspectorId { get; }

    string InspectorVersion { get; }

    bool Supports(string sourcePath);

    Task<SourceFactsDocument> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
