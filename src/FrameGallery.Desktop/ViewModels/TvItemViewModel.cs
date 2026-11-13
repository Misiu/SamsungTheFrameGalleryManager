using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>One TV in the list.</summary>
public sealed class TvItemViewModel(TvRecord record)
{
    public TvRecord Record { get; } = record;

    public string Name => Record.Name;
    public string Details => string.IsNullOrEmpty(Record.Model) ? Record.Ip : $"{Record.Model} · {Record.Ip}";
    public bool IsFrame => Record.IsFrame;
    public bool IsPaired => Record.IsPaired;
    public string TypeText => Record.IsFrame ? Loc.Get("Main_TypeFrame") : Loc.Get("Main_TypeOther");
    public string PairedText => Loc.Get("Main_BadgePaired");
}
