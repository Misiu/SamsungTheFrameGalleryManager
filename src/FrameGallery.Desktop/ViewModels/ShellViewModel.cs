using CommunityToolkit.Mvvm.ComponentModel;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>The one window: a stack of screens, of which the top one is shown. The first screen stays at the bottom for good.</summary>
public sealed partial class ShellViewModel(string appTitle) : ObservableObject
{
    readonly List<(ScreenViewModel Screen, TaskCompletionSource Done)> _stack = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] ScreenViewModel? _current;

    /// <summary>"Frame Gallery", or "Frame Gallery › 65" The Frame" inside a gallery.</summary>
    public string Title => Current is { Title: { Length: > 0 } title } ? $"{appTitle} › {title}" : appTitle;

    /// <summary>Puts a screen on top. The task completes when the screen has been removed again.</summary>
    public Task ShowAsync(ScreenViewModel screen)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stack.Add((screen, done));
        screen.CloseRequested += OnCloseRequested;
        Current = screen;
        return done.Task;
    }

    /// <summary>Goes back one screen, unless the current one is the first or cannot be left right now.</summary>
    public void GoBack() => Current?.GoBackCommand.Execute(null);

    void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_stack.Count < 2 || !ReferenceEquals(_stack[^1].Screen, sender)) return;   // only the top screen can close, never the first one

        var (screen, done) = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        screen.CloseRequested -= OnCloseRequested;
        screen.OnLeaving();
        Current = _stack[^1].Screen;
        done.SetResult();
    }
}
