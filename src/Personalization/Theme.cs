namespace Personalization;

public sealed record Theme(int Version, string Id, string Name, ThemeColors Colors, ThemeLayout Layout)
{
    public IconReferences Icons { get; init; } = new();
}

public sealed record ThemeColors(string Background, string Button, string Text, string Border);

// All dimensions are logical pixels at 96 DPI.
public sealed record ThemeLayout(int IconSize, int ButtonWidth, int Padding, int Gap, int BottomMargin);
