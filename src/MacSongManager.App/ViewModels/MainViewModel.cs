using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MacSongManager.Core.Actions;
using MacSongManager.Core.Library;
using MacSongManager.Core.Songs;
using MacSongManager.Core.Storage;
using MacSongManager.Import;

namespace MacSongManager.ViewModels;

public enum LibraryFilter
{
    All,
    Enabled,
    Disabled,
    PcFormat,
    Duplicates
}

public sealed class FilterChoice
{
    public FilterChoice(LibraryFilter filter, string label)
    {
        Filter = filter;
        Label = label;
    }

    public LibraryFilter Filter { get; }
    public string Label { get; }
}

public sealed class SongRow : INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public SongRow(SongRecord song, Action selectionChanged)
    {
        Song = song;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SongRecord Song { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged();
        }
    }

    public string Status => Song.FileKind switch
    {
        SongFileKind.MacEnabled => "Enabled",
        SongFileKind.MacDisabled => "Disabled",
        SongFileKind.PcFormat => "PC format",
        _ => "Other"
    };

    public string Artist => Song.Artist ?? "";
    public string Title => Song.Title ?? "";
    public string Version => Song.Version ?? "";
    public string Author => Song.Author ?? "";
    public string File => Song.RelativePath;
    public string Size => FormatSize(Song.SizeBytes);
    public string Hash => Song.ContentHash is { Length: > 0 } hash
        ? hash[..Math.Min(12, hash.Length)]
        : "";

    public string Duplicate => Song.DuplicateReasons.Count == 0
        ? ""
        : string.Join(", ", Song.DuplicateReasons.Select(reason => reason switch
        {
            DuplicateReason.ContentHash => "Same file",
            _ => "Same song"
        }));

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly string _settingsPath;
    private readonly string _trashRoot;
    private readonly LibrarySettings _settings;
    private readonly LibraryScanner _scanner;
    private readonly ChangeApplier _applier;
    private readonly IChangeLog _log;
    private readonly ISongChartImporter _importer;
    private readonly List<SongRow> _rows = [];
    private string _libraryPath;
    private string _searchText = "";
    private FilterChoice _selectedFilter;
    private string _statusMessage = "Choose the dlc folder and scan.";
    private bool _isBusy;
    private SongRow? _detailsRow;
    private string _sortKey = "Artist";
    private bool _sortDescending;

    public MainViewModel(
        string settingsPath,
        string trashRoot,
        LibrarySettings settings,
        LibraryScanner scanner,
        ChangeApplier applier,
        IChangeLog log,
        ISongChartImporter importer)
    {
        _settingsPath = settingsPath;
        _trashRoot = trashRoot;
        _settings = settings;
        _scanner = scanner;
        _applier = applier;
        _log = log;
        _importer = importer;
        _libraryPath = string.IsNullOrWhiteSpace(settings.LibraryPath)
            ? AppPaths.DefaultDlcFolder()
            : settings.LibraryPath;
        Filters =
        [
            new FilterChoice(LibraryFilter.All, "All songs"),
            new FilterChoice(LibraryFilter.Enabled, "Enabled"),
            new FilterChoice(LibraryFilter.Disabled, "Disabled"),
            new FilterChoice(LibraryFilter.PcFormat, "PC format"),
            new FilterChoice(LibraryFilter.Duplicates, "Duplicates")
        ];
        _selectedFilter = Filters[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppName => AppPaths.DisplayName;
    public string Tagline => "For Rocksmith 2014 custom songs";
    public string VersionLabel => "Version 0.1.0";
    public string Disclaimer => "Unofficial. Not affiliated with Ubisoft.";
    public string ImporterNote => _importer.UnavailableReason;
    public bool CanImportAudio => _importer.IsAvailable;
    public IReadOnlyList<FilterChoice> Filters { get; }
    public ObservableCollection<SongRow> VisibleRows { get; } = [];

    public string LibraryPath
    {
        get => _libraryPath;
        set => Set(ref _libraryPath, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                RefreshVisible();
        }
    }

    public FilterChoice SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (Set(ref _selectedFilter, value))
            {
                foreach (var row in _rows)
                    row.IsSelected = false;
                RefreshVisible();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(CanAct));
        }
    }

    public bool CanAct => !IsBusy;
    public bool HasSelection => VisibleRows.Any(row => row.IsSelected);
    public string SummaryLine { get; private set; } = "No songs scanned.";
    public string DetailText => BuildDetail(_detailsRow);
    public bool HasDetail => _detailsRow is not null;

    public async Task ScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Scanning file names…";
        try
        {
            _settings.LibraryPath = LibraryPath;
            SettingsStore.Save(_settingsPath, _settings);
            var progress = new Progress<ScanProgress>(update =>
                StatusMessage = $"Reading {update.Completed} of {update.Total}…");
            var result = await _scanner.ScanAsync(LibraryPath, CancellationToken.None, progress);
            ReplaceRows(result.Songs);
            var warning = result.Warnings.Count == 0 ? "" : " " + string.Join(" ", result.Warnings);
            StatusMessage = $"{result.Songs.Count} custom song file(s).{warning}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Scan failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ChangePlan PlanEnable() => ChangePlanner.PlanEnable(LibraryPath, SelectedSongs());
    public ChangePlan PlanDisable() => ChangePlanner.PlanDisable(LibraryPath, SelectedSongs());
    public ChangePlan PlanOrganize() => ChangePlanner.PlanOrganize(LibraryPath, SelectedSongs());

    public ChangePlan PlanTrash() => ChangePlanner.PlanTrash(
        LibraryPath,
        _trashRoot,
        DateTime.Now.ToString("yyyyMMdd-HHmmss"),
        SelectedSongs());

    public ChangePlan? PlanUndo(out string message)
    {
        var latest = _log.Latest();
        if (latest is null || latest.Changes.Count == 0)
        {
            message = "Nothing to undo.";
            return null;
        }

        message = $"Undo {latest.Changes.Count} {latest.Kind} change(s) from {latest.Timestamp.ToLocalTime():g}.";
        return ChangePlanner.PlanUndo(latest);
    }

    public ApplyResult Apply(ChangePlan plan, bool dryRun) =>
        _applier.Apply(LibraryPath, _trashRoot, plan, dryRun);

    public void SetVisibleSelection(bool selected)
    {
        foreach (var row in VisibleRows)
            row.IsSelected = selected;
    }

    public SongRow? SelectedRow
    {
        get => _detailsRow;
        set
        {
            if (ReferenceEquals(_detailsRow, value))
                return;
            _detailsRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(HasDetail));
        }
    }

    public void Sort(string key)
    {
        if (_sortKey == key)
            _sortDescending = !_sortDescending;
        else
        {
            _sortKey = key;
            _sortDescending = false;
        }

        RefreshVisible();
    }

    private IReadOnlyList<SongRecord> SelectedSongs() =>
        VisibleRows.Where(row => row.IsSelected).Select(row => row.Song).ToList();

    private void ReplaceRows(IReadOnlyList<SongRecord> songs)
    {
        _rows.Clear();
        foreach (var song in songs)
            _rows.Add(new SongRow(song, OnSelectionChanged));
        _detailsRow = null;
        RefreshVisible();
        UpdateSummary();
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(HasDetail));
    }

    private void RefreshVisible()
    {
        var matches = _rows.Where(Matches).ToList();
        matches.Sort(CompareRows);
        VisibleRows.Clear();
        foreach (var row in matches)
            VisibleRows.Add(row);
    }

    private bool Matches(SongRow row)
    {
        if (!MatchesFilter(row))
            return false;
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return Contains(row.Artist, query)
            || Contains(row.Title, query)
            || Contains(row.Author, query)
            || Contains(row.Version, query)
            || Contains(row.File, query)
            || Contains(row.Hash, query)
            || Contains(row.Song.ContentHash, query);
    }

    private bool MatchesFilter(SongRow row) => SelectedFilter.Filter switch
    {
        LibraryFilter.Enabled => row.Song.FileKind == SongFileKind.MacEnabled,
        LibraryFilter.Disabled => row.Song.FileKind == SongFileKind.MacDisabled,
        LibraryFilter.PcFormat => row.Song.FileKind == SongFileKind.PcFormat,
        LibraryFilter.Duplicates => row.Song.IsDuplicate,
        _ => true
    };

    private void UpdateSummary()
    {
        int Count(SongFileKind kind) => _rows.Count(row => row.Song.FileKind == kind);
        var dupes = _rows.Count(row => row.Song.IsDuplicate);
        SummaryLine = $"{_rows.Count} songs · {Count(SongFileKind.MacEnabled)} enabled · {Count(SongFileKind.MacDisabled)} disabled · {Count(SongFileKind.PcFormat)} PC · {dupes} duplicates";
        OnPropertyChanged(nameof(SummaryLine));
    }

    private void OnSelectionChanged() => OnPropertyChanged(nameof(HasSelection));

    private int CompareRows(SongRow left, SongRow right)
    {
        var result = string.Compare(SortValue(left), SortValue(right), StringComparison.OrdinalIgnoreCase);
        if (result == 0)
            result = string.Compare(left.File, right.File, StringComparison.OrdinalIgnoreCase);
        return _sortDescending ? -result : result;
    }

    private string SortValue(SongRow row) => _sortKey switch
    {
        "Status" => row.Status,
        "Title" => row.Title,
        "Version" => row.Version,
        "Author" => row.Author,
        "File" => row.File,
        "Size" => row.Song.SizeBytes.ToString("D12"),
        "Hash" => row.Hash,
        "Duplicate" => row.Duplicate,
        _ => row.Artist
    };

    private static string BuildDetail(SongRow? row)
    {
        if (row is null)
            return "Select a row to see the file name, version, and author parsed from it.";

        var song = row.Song;
        var lines = new List<string>
        {
            $"{Display(song.Artist)} — {Display(song.Title)}",
            $"Version {Display(song.Version)} · {Display(song.Author)}",
            $"{row.Status} · {row.Size}" + (row.Hash.Length > 0 ? $" · {song.ContentHash}" : ""),
            song.RelativePath,
            song.FullPath
        };

        if (song.FileKind == SongFileKind.PcFormat)
            lines.Add("This file name ends in _p.psarc. The Mac game will not load it.");
        if (song.IsDuplicate)
            lines.Add("Duplicate: " + row.Duplicate + ".");
        if (song.ReadError is not null)
            lines.Add(song.ReadError);

        lines.Add("Package contents are not read in this version.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
