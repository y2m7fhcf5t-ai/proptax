using System.Globalization;
using Avalonia;

namespace PropTax.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;

        // Smoke-Test für CI: startet ohne UI, prüft Kernabhängigkeiten.
        if (args.Contains("--selftest"))
            return SelfTest.Ausfuehren();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
