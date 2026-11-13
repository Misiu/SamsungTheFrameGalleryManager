using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FrameGallery.Desktop.Controls;

/// <summary>
/// A text with a file name in it that is cut only when it does not fit: the end of the name goes ("long-name-of-a-ph....jpg") and the
/// extension stays, where a normal trimmed TextBlock would cut the extension off. The text is <see cref="Format"/> with the name in
/// place of {0}. Give it a width to fit in (a star column, or MaxWidth).
/// </summary>
public sealed class FileNameTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> FileNameProperty = AvaloniaProperty.Register<FileNameTextBlock, string?>(nameof(FileName));
    public static readonly StyledProperty<string> FormatProperty = AvaloniaProperty.Register<FileNameTextBlock, string>(nameof(Format), "{0}");

    public string? FileName { get => GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FileNameProperty || change.Property == FormatProperty || change.Property == BoundsProperty
            || change.Property == FontSizeProperty || change.Property == FontWeightProperty || change.Property == FontFamilyProperty)
            Fit();
    }

    void Fit()
    {
        var name = FileName ?? "";
        var format = string.IsNullOrEmpty(Format) ? "{0}" : Format;
        var room = Bounds.Width;
        if (room <= 0 || name.Length == 0)
        {
            Text = string.Format(format, name);   // not laid out yet: show everything, it is fitted when the size is known
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        bool Fits(string text) =>
            new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection, typeface, FontSize, Foreground).Width <= room;

        var text = string.Format(format, FileNames.Fit(format, name, Fits));
        if (Text != text) Text = text;
    }
}
