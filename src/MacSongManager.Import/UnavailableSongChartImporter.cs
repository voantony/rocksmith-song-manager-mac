namespace MacSongManager.Import;

/// <summary>
/// Placeholder registered by the app until a charting implementation exists.
/// </summary>
public sealed class UnavailableSongChartImporter : ISongChartImporter
{
    public string ImporterId => "unavailable";

    public string DisplayName => "Audio import";

    public bool IsAvailable => false;

    public string UnavailableReason =>
        "Importing FLAC or MP3 and charting it into a custom song is planned for a later version.";

    public IReadOnlyList<string> SupportedExtensions { get; } = [".flac", ".mp3"];

    public Task<ChartImportResult> ImportAsync(
        ChartImportRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ChartImportResult(false, UnavailableReason, null));
    }
}

public static class ChartImporters
{
    public static ISongChartImporter Default { get; } = new UnavailableSongChartImporter();
}
