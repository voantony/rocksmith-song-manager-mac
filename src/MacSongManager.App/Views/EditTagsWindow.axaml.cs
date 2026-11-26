using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MacSongManager.Views;

public partial class EditTagsWindow : Window
{
    public EditTagsWindow()
        : this("", "")
    {
    }

    public EditTagsWindow(string artist, string title)
    {
        InitializeComponent();
        ArtistBox.Text = artist;
        TitleBox.Text = title;
    }

    public string ArtistName => ArtistBox.Text?.Trim() ?? "";
    public string SongTitle => TitleBox.Text?.Trim() ?? "";

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
