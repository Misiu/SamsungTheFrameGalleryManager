using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.Views;

/// <summary>A small modal dialog built in code: a message, optionally a text box, and one or two buttons.</summary>
sealed class DialogWindow : Window
{
    readonly TextBox? _input;

    DialogWindow(Window owner, string text, bool askText, (string Label, bool Result)[] buttons)
    {
        Title = "Frame Gallery";
        Icon = owner.Icon;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 380;
        MaxWidth = 560;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        if (askText)
        {
            _input = new TextBox { MinWidth = 300 };
            panel.Children.Add(_input);
            Opened += (_, _) => _input.Focus();
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (label, result) in buttons)
        {
            var button = new Button { Content = label, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (result) { button.Classes.Add("primary"); button.IsDefault = true; }
            else button.IsCancel = true;
            button.Click += (_, _) => Close(result);
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        Content = panel;
    }

    public static async Task MessageAsync(Window owner, string text) =>
        await new DialogWindow(owner, text, false, [("OK", true)]).ShowDialog<bool>(owner);

    public static async Task<bool> ConfirmAsync(Window owner, string text) =>
        await new DialogWindow(owner, text, false, [(Loc.Get("Ui_Yes"), true), (Loc.Get("Ui_No"), false)]).ShowDialog<bool>(owner);

    /// <returns>The typed text, or null when cancelled.</returns>
    public static async Task<string?> AskAsync(Window owner, string title, string prompt)
    {
        var dialog = new DialogWindow(owner, prompt, true, [("OK", true), (Loc.Get("Ui_Cancel"), false)]) { Title = title };
        return await dialog.ShowDialog<bool>(owner) ? dialog._input!.Text?.Trim() : null;
    }
}
