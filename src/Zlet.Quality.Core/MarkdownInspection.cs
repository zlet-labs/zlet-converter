namespace Zlet.Quality.Core;

public interface IMarkdownInspector
{
    string InspectorId { get; }

    string InspectorVersion { get; }

    bool Supports(string markdownPath);

    Task<MarkdownFactsDocument> InspectAsync(
        string markdownPath,
        CancellationToken cancellationToken = default);
}
