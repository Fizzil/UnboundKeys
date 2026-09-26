using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UnboundKeys.Wpf;

// The illustration is Fizzil's own picture (Assets/voice-profile.png:
// black line art on white). It's shown through an OpacityMask rather
// than as an image: at load the picture becomes a mask whose alpha is
// how dark each pixel was, and the XAML paints that mask with the app's
// text color — so the strokes sit on the dark background with no white
// box, and re-color with the theme like every other line in the app.
public partial class VoiceDiagram
{
    private static BitmapSource? _mask;

    public VoiceDiagram()
    {
        InitializeComponent();
        ProfileMask.ImageSource = _mask ??= LoadMask();
    }


    // Lit while a tile is hovered: the waves come up to full strength, as
    // if she is speaking right now.
    public void SetSpeaking(bool speaking)
    {
        Waves.Opacity = speaking ? 1.0 : 0.45;
    }

    // Dark ink → opaque, white paper → transparent. Anything lighter than
    // 230/255 counts as paper, which also drops the faint noise a scanned
    // or compressed white background carries.
    private static BitmapSource LoadMask()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("voice-profile.png", StringComparison.OrdinalIgnoreCase));
        using var stream = name == null ? null : assembly.GetManifestResourceStream(name);
        if (stream == null)
            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[height * stride];
        source.CopyPixels(pixels, stride, 0);

        const int paper = 230;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int luminance = (pixels[i] * 114 + pixels[i + 1] * 587 + pixels[i + 2] * 299) / 1000; // B, G, R
            int alpha = Math.Clamp((paper - luminance) * 255 / paper, 0, 255);
            // A picture with a transparent background stores those pixels
            // as black-and-transparent; without this they'd read as ink.
            alpha = alpha * pixels[i + 3] / 255;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
            pixels[i + 3] = (byte)alpha;
        }

        var mask = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        mask.Freeze();
        return mask;
    }
}
