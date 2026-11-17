namespace PropTax.Domain;

/// <summary>Bundeseinheitliche gesetzliche Feiertage (landesspezifische Feiertage sind nicht berücksichtigt).</summary>
public static class Feiertage
{
    /// <summary>Ostersonntag (gregorianisch, Algorithmus nach Meeus/Jones/Butcher).</summary>
    public static DateOnly Ostersonntag(int jahr)
    {
        int a = jahr % 19, b = jahr / 100, c = jahr % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, monat = (h + l - 7 * m + 114) / 31, tag = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(jahr, monat, tag);
    }

    public static IReadOnlyDictionary<DateOnly, string> Bundeseinheitlich(int jahr)
    {
        var ostern = Ostersonntag(jahr);
        return new Dictionary<DateOnly, string>
        {
            [new DateOnly(jahr, 1, 1)] = "Neujahr",
            [ostern.AddDays(-2)] = "Karfreitag",
            [ostern.AddDays(1)] = "Ostermontag",
            [new DateOnly(jahr, 5, 1)] = "Tag der Arbeit",
            [ostern.AddDays(39)] = "Christi Himmelfahrt",
            [ostern.AddDays(50)] = "Pfingstmontag",
            [new DateOnly(jahr, 10, 3)] = "Tag der Deutschen Einheit",
            [new DateOnly(jahr, 12, 25)] = "1. Weihnachtstag",
            [new DateOnly(jahr, 12, 26)] = "2. Weihnachtstag",
        };
    }

    public static bool IstWerktag(DateOnly d) => Grund(d) is null;

    /// <summary>Warum der Tag kein Werktag ist (Samstag, Sonntag, Feiertagsname) oder null.</summary>
    public static string? Grund(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday => "Sonntag",
        _ => Bundeseinheitlich(d.Year).TryGetValue(d, out var name) ? name : null,
    };
}

public sealed record Faelligkeit(DateOnly Gesetzlich, DateOnly Tatsaechlich, string? Verschiebungsgrund);

public static class Fristen
{
    public static DateOnly Ende(UstZeitraum z) => z.Art switch
    {
        ZeitraumArt.Monat => new DateOnly(z.Jahr, z.Nummer, 1).AddMonths(1).AddDays(-1),
        ZeitraumArt.Quartal => new DateOnly(z.Jahr, z.Nummer * 3, 1).AddMonths(1).AddDays(-1),
        _ => new DateOnly(z.Jahr, 12, 31),
    };

    /// <summary>
    /// Fälligkeit der Voranmeldung: <paramref name="fristTag"/>. Tag nach Ablauf des Zeitraums (§18 Abs. 1 UStG),
    /// bei Dauerfristverlängerung einen Monat später; fällt sie auf Sa/So/Feiertag → nächster Werktag (§108 Abs. 3 AO).
    /// </summary>
    public static Faelligkeit VaFaelligkeit(UstZeitraum z, int fristTag, bool dauerfristverlaengerung)
    {
        if (z.Art == ZeitraumArt.Jahr) throw new ArgumentException("Für Jahreszeiträume gibt es keine Voranmeldungsfrist.", nameof(z));
        var gesetzlich = Ende(z).AddDays(1).AddMonths(dauerfristverlaengerung ? 1 : 0).AddDays(fristTag - 1);
        var tatsaechlich = gesetzlich;
        var gruende = new List<string>();
        while (Feiertage.Grund(tatsaechlich) is { } g)
        {
            gruende.Add(g);
            tatsaechlich = tatsaechlich.AddDays(1);
        }
        return new Faelligkeit(gesetzlich, tatsaechlich, gruende.Count == 0 ? null : string.Join(", ", gruende));
    }
}
