using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PropTax.App.ViewModels;

namespace PropTax.App.Views;

public partial class MainWindow : Window, IDialoge
{
    public MainWindow() => InitializeComponent();

    public async Task<string?> DateiOeffnen(string titel, params string[] endungen)
    {
        var dateien = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titel,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(string.Join(", ", endungen.Select(e => e.ToUpperInvariant()))) { Patterns = endungen.Select(e => "*." + e).ToList() }],
        });
        return dateien.Count == 0 ? null : dateien[0].TryGetLocalPath();
    }

    public async Task<string?> OrdnerWaehlen(string titel)
    {
        var ordner = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titel, AllowMultiple = false });
        return ordner.Count == 0 ? null : ordner[0].TryGetLocalPath();
    }
}
