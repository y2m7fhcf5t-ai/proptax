using System.Reflection;
using NetArchTest.Rules;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Abstraktionen;

namespace PropTax.ArchitectureTests;

public class SchichtenTests
{
    private static readonly Assembly Domain = typeof(Money).Assembly;
    private static readonly Assembly Engine = typeof(IRegelQuelle).Assembly;

    [Fact]
    public void Domain_hat_keine_Projektabhaengigkeiten() =>
        Assert.True(Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("PropTax.TaxEngine", "PropTax.Persistence", "PropTax.Reporting", "PropTax.App", "Avalonia")
            .GetResult().IsSuccessful);

    [Fact]
    public void Engine_kennt_weder_UI_noch_Persistenz_noch_Reporting() =>
        Assert.True(Types.InAssembly(Engine).ShouldNot()
            .HaveDependencyOnAny("PropTax.Persistence", "PropTax.Reporting", "PropTax.App", "Avalonia", "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite")
            .GetResult().IsSuccessful);

    [Theory]
    [MemberData(nameof(KernAssemblies))]
    public void Keine_Gleitkommatypen_in_Domain_und_Engine(string name)
    {
        var asm = name == "Domain" ? Domain : Engine;
        var verboten = new[] { typeof(double), typeof(float), typeof(double?), typeof(float?) };
        const BindingFlags alle = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var treffer = asm.GetTypes()
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .SelectMany(t =>
                t.GetFields(alle).Select(f => (t.Name + "." + f.Name, f.FieldType))
                .Concat(t.GetProperties(alle).Select(p => (t.Name + "." + p.Name, p.PropertyType)))
                .Concat(t.GetMethods(alle).SelectMany(m =>
                    m.GetParameters().Select(p => (t.Name + "." + m.Name + "(" + p.Name + ")", p.ParameterType))
                    .Append((t.Name + "." + m.Name + "→", m.ReturnType)))))
            .Where(x => verboten.Contains(x.Item2))
            .Select(x => x.Item1)
            .ToList();
        Assert.True(treffer.Count == 0, "Gleitkomma gefunden: " + string.Join(", ", treffer));
    }

    public static TheoryData<string> KernAssemblies => new() { "Domain", "Engine" };
}
