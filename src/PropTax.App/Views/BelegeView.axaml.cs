using Avalonia.Controls;
using Avalonia.Interactivity;
using PropTax.App.ViewModels;

namespace PropTax.App.Views;

public partial class BelegeView : UserControl
{
    public BelegeView() => InitializeComponent();

    private BelegeViewModel Vm => (BelegeViewModel)DataContext!;
    private IEnumerable<BelegZeileVm> Markiert => Liste.SelectedItems.OfType<BelegZeileVm>().ToList();

    private void MarkierteBuchen(object? sender, RoutedEventArgs e) => Vm.BucheMarkierte(Markiert);
    private void MarkierteKategorie(object? sender, RoutedEventArgs e) => Vm.KategorieFuerMarkierte(Markiert);
}
