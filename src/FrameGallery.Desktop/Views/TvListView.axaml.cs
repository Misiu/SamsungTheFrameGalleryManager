using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Desktop.Views;

public partial class TvListView : UserControl
{
    public TvListView()
    {
        InitializeComponent();

        // A double-click on a TV (or Enter) opens its gallery.
        TvList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is TvItemViewModel tv) Vm.OpenCommand.Execute(tv);
        };
        KeyDown += (_, e) =>
        {
            if (TvList.SelectedItem is not TvItemViewModel tv) return;
            if (e.Key == Key.Enter) Vm.OpenCommand.Execute(tv);
            else if (e.Key == Key.Delete) Vm.RemoveCommand.Execute(tv);
        };
    }

    TvListViewModel Vm => (TvListViewModel)DataContext!;

    void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (TvList.SelectedItem is TvItemViewModel tv) Vm.RemoveCommand.Execute(tv);
    }
}
