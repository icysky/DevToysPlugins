using ColorSchemer.Core;

namespace ColorSchemer.Controls;

public sealed class ColorPickedEventArgs : EventArgs
{
    public ColorPickedEventArgs(RgbColor color) => Color = color;

    public RgbColor Color { get; }
}
