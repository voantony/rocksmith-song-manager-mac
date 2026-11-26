using System.Text.Json;

namespace MacSongManager.Core.Library;

public sealed record PackageManifest(
    string? Artist,
    string? Title,
    string? Album,
    int? Year,
    string? Arrangements,
    string? Tuning,
    string? DlcKey);

/// <summary>
/// Reads song attributes from manifest JSON or HSAN text. It does not
/// interpret chart bytes.
/// </summary>
public static class ManifestMetadata
{
    private static readonly string[] ArrangementOrder = ["Lead", "Rhythm", "Bass", "Combo", "Vocals"];

    public static PackageManifest? Read(IEnumerable<byte[]> documents)
    {
        string? artist = null;
        string? title = null;
        string? album = null;
        string? dlcKey = null;
        int? year = null;
        string? leadTuning = null;
        string? anyTuning = null;
        var arrangements = new List<string>();

        foreach (var documentBytes in documents)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(documentBytes);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                foreach (var attributes in EnumerateAttributes(document.RootElement))
                {
                    artist ??= Text(attributes, "ArtistName");
                    title ??= Text(attributes, "SongName");
                    album ??= Text(attributes, "AlbumName");
                    dlcKey ??= Text(attributes, "DLCKey") ?? Text(attributes, "SongKey");
                    if (year is null && attributes.TryGetProperty("SongYear", out var yearElement)
                        && yearElement.TryGetInt32(out var yearValue) && yearValue > 0)
                        year = yearValue;

                    var arrangement = Text(attributes, "ArrangementName");
                    if (!string.IsNullOrWhiteSpace(arrangement))
                        arrangements.Add(arrangement);

                    if (attributes.TryGetProperty("Tuning", out var tuningElement))
                    {
                        var described = DescribeTuning(tuningElement);
                        if (described is null)
                            continue;
                        if (arrangement?.Equals("Lead", StringComparison.OrdinalIgnoreCase) == true)
                            leadTuning = described;
                        anyTuning ??= described;
                    }
                }
            }
        }

        if (artist is null && title is null && album is null && arrangements.Count == 0)
            return null;

        return new PackageManifest(
            artist,
            title,
            album,
            year,
            JoinArrangements(arrangements),
            leadTuning ?? anyTuning,
            dlcKey);
    }

    private static IEnumerable<JsonElement> EnumerateAttributes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        if (root.TryGetProperty("Entries", out var entries) && entries.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in entries.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("Attributes", out var attributes)
                    && attributes.ValueKind == JsonValueKind.Object)
                    yield return attributes;
            }
        }

        if (root.TryGetProperty("Attributes", out var direct) && direct.ValueKind == JsonValueKind.Object)
            yield return direct;

        if (root.TryGetProperty("ArtistName", out _) || root.TryGetProperty("SongName", out _))
            yield return root;
    }

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal static string? DescribeTuning(JsonElement tuning)
    {
        if (tuning.ValueKind == JsonValueKind.String)
        {
            var text = tuning.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        if (tuning.ValueKind != JsonValueKind.Object)
            return null;

        var offsets = new int[6];
        var found = false;
        for (var i = 0; i < offsets.Length; i++)
        {
            if (tuning.TryGetProperty("string" + i, out var value) && value.TryGetInt32(out var offset))
            {
                offsets[i] = offset;
                found = true;
            }
        }

        if (!found)
            return null;
        if (offsets.All(offset => offset == 0))
            return "E Standard";
        if (offsets.All(offset => offset == -1))
            return "Eb Standard";
        if (offsets.All(offset => offset == -2))
            return "D Standard";
        if (offsets[0] == -2 && offsets.Skip(1).All(offset => offset == 0))
            return "Drop D";
        return string.Join(' ', offsets);
    }

    private static string? JoinArrangements(List<string> names)
    {
        var distinct = new List<string>();
        foreach (var name in names)
        {
            if (distinct.Exists(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            distinct.Add(name);
        }

        if (distinct.Count == 0)
            return null;

        distinct.Sort(static (left, right) =>
        {
            var leftRank = Rank(left);
            var rightRank = Rank(right);
            var compared = leftRank.CompareTo(rightRank);
            return compared != 0 ? compared : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        });
        return string.Join(", ", distinct);
    }

    private static int Rank(string name)
    {
        for (var i = 0; i < ArrangementOrder.Length; i++)
        {
            if (ArrangementOrder[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return ArrangementOrder.Length;
    }
}
