#pragma warning disable CS9113 // Stub (Phase 2, test-first)
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.TaxEngine.Ermittlung;

public sealed class Steuerermittlung(IRegelwerk regelwerk)
{
    public SteuerErgebnis Ermittle(BelegEingabe beleg, Uebersteuerung? uebersteuerung = null) => throw new NotImplementedException();
}
