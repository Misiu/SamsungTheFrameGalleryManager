using Avalonia.Markup.Xaml;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.Markup;

/// <summary>XAML access to the localized text: <c>Text="{loc:Loc Main_Scan}"</c>. The language is fixed for the lifetime of the app.</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
