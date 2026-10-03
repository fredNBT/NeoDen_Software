using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Trims a photo down to its actual subject, discarding a uniform border around it. A typical
/// component photo (stock photography, a datasheet crop, a phone photo on a table) leaves
/// generous background margin around the part - when that whole frame, margin included, gets
/// stretched to exactly fill a small footprint's real physical envelope on the board, the part
/// itself ends up rendering far smaller than the footprint actually is. Cropping to content first
/// means the part fills the frame, so stretching it into the footprint's box makes the part fill
/// that box too.
/// </summary>
public static class ImageCropper
{
    /// <summary>Detects the background color from the four corner pixels (not just "assume
    /// white" - works for any uniform background), then trims every edge inward to the first
    /// row/column containing a pixel that differs from it by more than a tolerant threshold
    /// (survives JPEG noise and anti-aliased edges). Keeps a small proportional padding around
    /// the detected content so the crop doesn't look uncomfortably tight. Returns the ORIGINAL
    /// bitmap unchanged if no real border is found, or the detected content would be implausibly
    /// small (under a tenth of either dimension) - never crops a full-bleed photo down to a
    /// sliver by mistake.</summary>
    public static BitmapSource CropToContent(BitmapSource source, double paddingFraction = 0.04)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width < 8 || height < 8) return source;

        var stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        (int B, int G, int R) At(int x, int y)
        {
            var i = y * stride + x * 4;
            return (pixels[i], pixels[i + 1], pixels[i + 2]);
        }

        var corners = new[] { At(0, 0), At(width - 1, 0), At(0, height - 1), At(width - 1, height - 1) };
        var bgB = corners.Average(c => c.B);
        var bgG = corners.Average(c => c.G);
        var bgR = corners.Average(c => c.R);

        const double threshold = 24;
        bool IsBackground(int x, int y)
        {
            var (b, g, r) = At(x, y);
            return Math.Abs(b - bgB) <= threshold && Math.Abs(g - bgG) <= threshold && Math.Abs(r - bgR) <= threshold;
        }

        bool RowHasContent(int y)
        {
            for (var x = 0; x < width; x++)
                if (!IsBackground(x, y)) return true;
            return false;
        }

        bool ColumnHasContent(int x)
        {
            for (var y = 0; y < height; y++)
                if (!IsBackground(x, y)) return true;
            return false;
        }

        var top = 0;
        while (top < height && !RowHasContent(top)) top++;
        var bottom = height - 1;
        while (bottom > top && !RowHasContent(bottom)) bottom--;
        var left = 0;
        while (left < width && !ColumnHasContent(left)) left++;
        var right = width - 1;
        while (right > left && !ColumnHasContent(right)) right--;

        if (top == 0 && bottom == height - 1 && left == 0 && right == width - 1) return source;

        var contentWidth = right - left + 1;
        var contentHeight = bottom - top + 1;
        if (contentWidth < width / 10 || contentHeight < height / 10) return source;

        var padX = (int)(contentWidth * paddingFraction);
        var padY = (int)(contentHeight * paddingFraction);
        var cropLeft = Math.Max(0, left - padX);
        var cropTop = Math.Max(0, top - padY);
        var cropWidth = Math.Min(width - cropLeft, contentWidth + 2 * padX);
        var cropHeight = Math.Min(height - cropTop, contentHeight + 2 * padY);

        var cropped = new CroppedBitmap(converted, new Int32Rect(cropLeft, cropTop, cropWidth, cropHeight));
        cropped.Freeze();
        return cropped;
    }
}
