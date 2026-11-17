using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PropTax.App.ViewModels;
using PropTax.App.Views;
using PropTax.Application;

namespace PropTax.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        // de-DE-Formate unabhängig von der Systemeinstellung (auch in Tests).
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        System.Globalization.CultureInfo.CurrentCulture = de;
        System.Globalization.CultureInfo.CurrentUICulture = de;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = de;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = de;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var ab = Arbeitsbereich.Oeffne(Konfiguration.Datenordner(), Environment.UserName);
            var fenster = new MainWindow();
            fenster.DataContext = new MainViewModel(ab, fenster);
            desktop.MainWindow = fenster;
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Merkt sich den gewählten Datenordner (eine Textzeile unter %APPDATA%\PropTax\datenordner.txt).</summary>
public static class Konfiguration
{
    private static string Datei => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PropTax", "datenordner.txt");

    public static string Datenordner()
    {
        try
        {
            if (File.Exists(Datei) && File.ReadAllText(Datei).Trim() is { Length: > 0 } pfad) return pfad;
        }
        catch (IOException) { }
        return Arbeitsbereich.StandardOrdner;
    }

    public static void SetzeDatenordner(string pfad)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Datei)!);
        File.WriteAllText(Datei, pfad);
    }
}
