using Avalonia.Controls;
using Avalonia.Interactivity;
using MacSongManager.ViewModels;

namespace MacSongManager.Views;

public partial class TagReviewWindow : Window
{
    public TagReviewWindow()
        : this([])
    {
    }

    public TagReviewWindow(IReadOnlyList<ReviewRow> rows)
    {
        InitializeComponent();
        Rows.ItemsSource = rows;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
