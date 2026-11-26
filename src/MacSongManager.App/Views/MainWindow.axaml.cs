using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MacSongManager.Core.Actions;
using MacSongManager.Core.Library;
using MacSongManager.Core.Storage;
using MacSongManager.Import;
using MacSongManager.ViewModels;

namespace MacSongManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        var settingsPath = AppPaths.SettingsPath();
        var settings = SettingsStore.Load(settingsPath);
        var log = new JsonlChangeLog(AppPaths.ChangeLogPath());
        _model = new MainViewModel(
            settingsPath,
            AppPaths.TrashFolder(),
            settings,
            new LibraryScanner(),
            new ChangeApplier(log),
            log,
            ChartImporters.Default);
        DataContext = _model;
        InitializeComponent();
        Opened += async (_, _) => await _model.ScanAsync();
    }

    private async void OnScan(object? sender, RoutedEventArgs e)
    {
        _model.LibraryPath = PathBox.Text ?? "";
        await _model.ScanAsync();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the dlc folder",
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            _model.StatusMessage = "That folder is not available as a local path.";
            return;
        }

        _model.LibraryPath = path;
        await _model.ScanAsync();
    }

    private async void OnEnable(object? sender, RoutedEventArgs e) => await RunAsync(_model.PlanEnable());
    private async void OnDisable(object? sender, RoutedEventArgs e) => await RunAsync(_model.PlanDisable());
    private async void OnOrganize(object? sender, RoutedEventArgs e) => await RunAsync(_model.PlanOrganize());
    private async void OnTrash(object? sender, RoutedEventArgs e) => await RunAsync(_model.PlanTrash());

    private async void OnUndo(object? sender, RoutedEventArgs e)
    {
        var plan = _model.PlanUndo(out var message);
        if (plan is null)
        {
            _model.StatusMessage = message;
            return;
        }

        await RunAsync(plan, message);
    }

    private void OnSelectShown(object? sender, RoutedEventArgs e) => _model.SetVisibleSelection(true);
    private void OnClearSelection(object? sender, RoutedEventArgs e) => _model.SetVisibleSelection(false);

    private void OnSort(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            _model.Sort(key);
    }

    private async Task RunAsync(ChangePlan plan, string? note = null)
    {
        if (!_model.HasSelection && plan.Kind != ChangeKind.Undo)
        {
            _model.StatusMessage = "Select one or more songs first.";
            return;
        }

        var dialog = new PreviewWindow
        {
            DataContext = new PreviewViewModel(plan, note ?? "Confirm to write these changes. You can undo the batch afterward.")
        };
        var apply = await dialog.ShowDialog<bool>(this);
        if (!apply)
        {
            _model.StatusMessage = "Canceled. No files were changed.";
            return;
        }

        var result = _model.Apply(plan, dryRun: false);
        _model.StatusMessage = result.Message;
        if (result.Succeeded)
            await _model.ScanAsync();
    }
}
