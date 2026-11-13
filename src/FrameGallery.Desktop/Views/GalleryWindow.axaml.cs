using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Desktop.Views;

public partial class GalleryWindow : Window
{
    static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    readonly GalleryViewModel _vm;
    readonly WindowInteraction _ui;

    public GalleryWindow(TvRecord tv, IArtClient art)
    {
        InitializeComponent();
        _ui = new WindowInteraction(this);
        DataContext = _vm = new GalleryViewModel(tv, art, _ui);

        Opened += async (_, _) =>
        {
            await _vm.ReloadAsync();
            // A hook for testing the crop window and the upload progress without the system file dialog (demo mode only).
            if (art is Demo.DemoArtClient && Environment.GetEnvironmentVariable("FRAMEGALLERY_DEMO_ADD") is { Length: > 0 } file)
                await _vm.AddFilesAsync([file]);
        };
        Closing += (_, _) => _vm.Cancel();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Delete) await _vm.DeleteAsync(Selected());
        };

        // Dragging image files onto the window works like picking them with "Add photos…".
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.Classes.Set("on", false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    List<GalleryItemViewModel> Selected() => Tiles.SelectedItems?.OfType<GalleryItemViewModel>().ToList() ?? [];

    async void Refresh_Click(object? sender, RoutedEventArgs e) => await _vm.ReloadAsync();

    async void AddPhotos_Click(object? sender, RoutedEventArgs e)
    {
        var files = await _ui.PickImagesAsync();
        if (files.Count > 0) await _vm.AddFilesAsync(files);
    }

    void Menu_Opening(object? sender, CancelEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0 || _vm.IsBusy) { e.Cancel = true; return; }
        ShowOnTvItem.IsEnabled = selected.Count == 1;
        DeleteItem.IsEnabled = selected.Any(i => i.IsOwn);
    }

    async void ShowOnTv_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected() is [var item]) await _vm.ShowOnTvAsync(item);
    }

    async void Delete_Click(object? sender, RoutedEventArgs e) => await _vm.DeleteAsync(Selected());

    static string[] DroppedImages(DragEventArgs e) =>
        (e.DataTransfer.TryGetFiles() ?? [])
        .Select(f => f.TryGetLocalPath())
        .OfType<string>()
        .Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
        .ToArray();

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var accept = !_vm.IsBusy && DroppedImages(e).Length > 0;
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Classes.Set("on", accept);
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.Classes.Set("on", false);
        var files = DroppedImages(e);
        if (files.Length == 0 || _vm.IsBusy) return;
        TvLog.Write($"[UI] dropped {files.Length} file(s)");
        await _vm.AddFilesAsync(files);
    }
}
