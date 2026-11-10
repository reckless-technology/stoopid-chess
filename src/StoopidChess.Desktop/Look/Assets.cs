using System.Reflection;
using SkiaSharp;

namespace StoopidChess.Desktop.Look;

/// <summary>
/// The files embedded in the assembly from Assets/: the pieces, the icon and the fonts.
/// </summary>
internal static class Assets
{
    private const string Prefix = "StoopidChess.Desktop.Assets.";

    /// <summary>
    /// The embedded file <paramref name="name"/>, its path under Assets/ with dots for the folders
    /// ("Fonts.NotoSerif-Regular.ttf").
    /// </summary>
    public static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + name)
        ?? throw new FileNotFoundException($"No embedded asset {name}.", name);

    /// <summary>
    /// The embedded picture <paramref name="name"/>, decoded.
    /// </summary>
    public static SKImage Image(string name)
    {
        using Stream stream = Open(name);
        using var data = SKData.Create(stream);
        return SKImage.FromEncodedData(data)
            ?? throw new InvalidDataException($"The embedded picture {name} could not be decoded.");
    }
}
