using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MfgManager.App.Converters;
using MfgManager.App.Services;

namespace MfgManager.Tests.Components;

public class CoverImagesTests
{
    private static string MakeCover(TempFolder temp)
    {
        string path = Path.Combine(temp.Path, "cover.png");
        using var bitmap = new RenderTargetBitmap(new PixelSize(600, 900));
        using (DrawingContext context = bitmap.CreateDrawingContext())
            context.FillRectangle(Brushes.SteelBlue, new Rect(0, 0, 600, 900));
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    [Fact]
    public Task A_cover_decoded_in_the_background_is_the_one_the_card_draws() => TestApp.Run(async () =>
    {
        using var temp = new TempFolder();
        string file = MakeCover(temp);

        await CoverImages.PreloadAsync(file);
        Bitmap? decoded = CoverImages.Get(file);

        Assert.NotNull(decoded);
        // Decoded small: twice a card's width, not the file's full size.
        Assert.Equal(340, decoded.PixelSize.Width);
        Assert.Same(decoded, CoverImageConverter.Instance.Convert(file, typeof(object), null, CultureInfo.InvariantCulture));
    });

    [Fact]
    public Task A_missing_or_broken_file_shows_the_placeholder() => TestApp.Run(async () =>
    {
        using var temp = new TempFolder();
        string missing = Path.Combine(temp.Path, "missing.jpg");
        string broken = temp.File("broken.jpg", "not a picture");

        await CoverImages.PreloadAsync(missing);
        await CoverImages.PreloadAsync(broken);

        Assert.Null(CoverImages.Get(missing));
        Assert.Null(CoverImages.Get(broken));
        Assert.Null(CoverImageConverter.Instance.Convert(null, typeof(object), null, CultureInfo.InvariantCulture));
    });
}
