using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MacSongManager.Views;

public partial class NamePromptWindow : Window
{
    public NamePromptWindow() => InitializeComponent();

    public string EnteredName => NameBox.Text?.Trim() ?? "";

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
