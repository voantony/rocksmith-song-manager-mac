using System.Net.Http.Headers;
using System.Text.Json;

namespace MacSongManager.Core.Tags;

public sealed record TagSuggestion(string Artist, string Title, int Score);

public interface ITagLookup
{
    Task<TagSuggestion?> LookupAsync(string? artist, string? title, CancellationToken cancellationToken);
}

public sealed class RequestPacer
{
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public RequestPacer(TimeSpan interval, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _interval = interval;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        var now = _now();
        if (now < _next)
            await _delay(_next - now, cancellationToken).ConfigureAwait(false);
        _next = _now().Add(_interval);
    }
}

public sealed class MusicBrainzLookup : ITagLookup
{
    public const string UserAgent = "MacSongManager/0.3.0 (https://github.com/voantony/rocksmith-song-manager-mac)";

    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public MusicBrainzLookup(HttpClient http, RequestPacer? pacer = null)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _pacer = pacer ?? new RequestPacer(TimeSpan.FromSeconds(1));
    }

    public async Task<TagSuggestion?> LookupAsync(string? artist, string? title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        await _pacer.WaitTurnAsync(cancellationToken).ConfigureAwait(false);
        var query = string.IsNullOrWhiteSpace(artist)
            ? $"recording:\"{title}\""
            : $"recording:\"{title}\" AND artist:\"{artist}\"";
        var url = "https://musicbrainz.org/ws/2/recording/?fmt=json&limit=1&query=" + Uri.EscapeDataString(query);
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            return null;

        var recording = recordings[0];
        var score = recording.TryGetProperty("score", out var scoreElement) ? scoreElement.GetInt32() : 0;
        if (score < 80 || !recording.TryGetProperty("title", out var titleElement))
            return null;

        var foundTitle = titleElement.GetString();
        var foundArtist = recording.TryGetProperty("artist-credit", out var credit) && credit.GetArrayLength() > 0
            ? credit[0].TryGetProperty("name", out var name) ? name.GetString() : null
            : null;
        if (string.IsNullOrWhiteSpace(foundTitle) || string.IsNullOrWhiteSpace(foundArtist))
            return null;
        return new TagSuggestion(foundArtist, foundTitle, score);
    }
}
