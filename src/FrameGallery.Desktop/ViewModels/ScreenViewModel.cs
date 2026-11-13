using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>
/// One screen of the single window (the list of TVs, a gallery, the crop screen). The <see cref="ShellViewModel"/> shows the current
/// one and keeps the screens behind it, so "Back" returns to the previous one.
/// </summary>
public abstract partial class ScreenViewModel : ObservableObject
{
    /// <summary>Shown after the application name in the window's title; empty for the first screen.</summary>
    public virtual string Title => "";

    /// <summary>False for the first screen, which has nowhere to go back to.</summary>
    public virtual bool HasBack => true;

    /// <summary>False while the screen is in the middle of something that must not be interrupted (an upload, encoding).</summary>
    public virtual bool CanLeave => true;

    /// <summary>The screen asks the shell to remove it (Back, or the screen is finished).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The screen has just been removed: stop what it was doing.</summary>
    public virtual void OnLeaving() { }

    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The Back button, Esc and Alt+Left.</summary>
    [RelayCommand]
    void GoBack()
    {
        if (HasBack && CanLeave) RequestClose();
    }
}
