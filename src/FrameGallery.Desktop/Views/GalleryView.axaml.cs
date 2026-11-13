using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Desktop.Views;

public partial class GalleryView : UserControl
{
    static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public GalleryView()
    {
        InitializeComponent();

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Delete) await Vm.DeleteAsync(Selected());
        };

        // Dragging image files onto the gallery works like picking them with "Add photos…".
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.Classes.Set("on", false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    GalleryViewModel Vm => (GalleryViewModel)DataContext!;

    List<GalleryItemViewModel> Selected() => Tiles.SelectedItems?.OfType<GalleryItemViewModel>().ToList() ?? [];

    async void Refresh_Click(object? sender, RoutedEventArgs e) => await Vm.ReloadAsync();

    void Menu_Opening(object? sender, CancelEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0 || Vm.IsBusy) { e.Cancel = true; return; }
        ShowOnTvItem.IsEnabled = selected.Count == 1;
        DeleteItem.IsEnabled = selected.Any(i => i.IsOwn);
    }

    async void ShowOnTv_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected() is [var item]) await Vm.ShowOnTvAsync(item);
    }

    async void Delete_Click(object? sender, RoutedEventArgs e) => await Vm.DeleteAsync(Selected());

    static string[] DroppedImages(DragEventArgs e) =>
        (e.DataTransfer.TryGetFiles() ?? [])
        .Select(f => f.TryGetLocalPath())
        .OfType<string>()
        .Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
        .ToArray();

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var accept = !Vm.IsBusy && DroppedImages(e).Length > 0;
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Classes.Set("on", accept);
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.Classes.Set("on", false);
        var files = DroppedImages(e);
        if (files.Length == 0 || Vm.IsBusy) return;
        TvLog.Write($"[UI] dropped {files.Length} file(s)");
        await Vm.AddFilesAsync(files);
    }
}
