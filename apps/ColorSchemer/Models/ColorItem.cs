using System.Windows.Media;
using ColorSchemer.Core;

namespace ColorSchemer.Models;

/// <summary>配色 / 收藏列表中的一行。</summary>
public sealed class ColorItem
{
    public required string Label { get; init; }

    public required string Hex { get; init; }

    public required string Rgb { get; init; }

    public required Brush Brush { get; init; }

    public RgbColor Color { get; init; }
}

public sealed record HarmonyOption(string Name, HarmonyRule Rule);
