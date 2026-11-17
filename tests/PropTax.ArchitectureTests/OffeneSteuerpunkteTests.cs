using System.Text.RegularExpressions;

namespace PropTax.ArchitectureTests;

/// <summary>Jede TODO-STEUER-Markierung im Code muss in docs/steuerregeln.md als Punkt geführt sein.</summary>
public partial class OffeneSteuerpunkteTests
{
    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PropTax.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository-Wurzel nicht gefunden.");
    }

    [Fact]
    public void Alle_TODO_STEUER_Marker_sind_dokumentiert()
    {
        var wurzel = Wurzel();
        var doku = File.ReadAllText(Path.Combine(wurzel, "docs", "steuerregeln.md"));
        var dateien = Directory.EnumerateFiles(Path.Combine(wurzel, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".sql") || f.EndsWith(".json")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var marker = dateien.SelectMany(f => Marker().Matches(File.ReadAllText(f)).Select(m => (Datei: Path.GetRelativePath(wurzel, f), Id: m.Groups[1].Value))).ToList();

        Assert.NotEmpty(marker);
        var fehlend = marker.Where(m => !doku.Contains($"| {m.Id} |")).Select(m => $"{m.Id} in {m.Datei}").Distinct().ToList();
        Assert.True(fehlend.Count == 0, "Nicht in docs/steuerregeln.md geführt: " + string.Join(", ", fehlend));
    }

    [Fact]
    public void Jeder_offene_Punkt_hat_einen_Status()
    {
        var doku = File.ReadAllText(Path.Combine(Wurzel(), "docs", "steuerregeln.md"));
        var zeilen = doku.Split('\n').Where(z => Punkt().IsMatch(z)).ToList();
        Assert.True(zeilen.Count >= 14);
        Assert.All(zeilen, z => Assert.Matches(@"\|\s*(offen|ungeprüft|validiert|geschlossen|\*\*offen\*\*)[^|]*\|\s*$", z.TrimEnd('\r')));
    }

    [GeneratedRegex(@"TODO-STEUER (O-\d{2})")]
    private static partial Regex Marker();

    [GeneratedRegex(@"^\| O-\d{2} \|")]
    private static partial Regex Punkt();
}
