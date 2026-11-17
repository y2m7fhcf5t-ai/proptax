using Avalonia.Controls;

namespace PropTax.App.Views;

/// <summary>Passwortabfrage beim Start. <c>Pruefe</c> liefert null bei Erfolg, sonst eine Fehlermeldung. Max. 3 Versuche.</summary>
public partial class PasswortWindow : Window
{
    public const int MaxVersuche = 3;
    private int _versuche;

    public PasswortWindow() => InitializeComponent();

    public PasswortWindow(string datenordner, Func<string, string?> pruefe) : this()
    {
        Ordner.Text = $"Datenordner: {datenordner}";
        Abbrechen.Click += (_, _) => Close();
        Ok.Click += (_, _) =>
        {
            var fehler = pruefe(Passwort.Text ?? "");
            if (fehler is null) return;
            _versuche++;
            Passwort.Text = "";
            Meldung.Text = _versuche >= MaxVersuche ? $"{fehler} Zu viele Versuche – PropTax wird beendet." : $"{fehler} (Versuch {_versuche}/{MaxVersuche})";
            if (_versuche >= MaxVersuche) Close();
        };
        Opened += (_, _) => Passwort.Focus();
    }
}
