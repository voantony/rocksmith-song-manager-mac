namespace MacSongManager.Import;

public sealed record ChartImportRequest(string SourceAudioPath, string DestinationLibraryPath);

public sealed record ChartImportResult(bool Succeeded, string Message, string? ProducedPackagePath);

/// <summary>
/// Plug-in boundary for a future audio importer. v1 does not chart audio and
/// does not open song packages. See docs/roadmap.md. Packaging remains pending
/// legal review.
/// </summary>
public interface ISongChartImporter
{
    string ImporterId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    string UnavailableReason { get; }
    IReadOnlyList<string> SupportedExtensions { get; }

    Task<ChartImportResult> ImportAsync(
        ChartImportRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
