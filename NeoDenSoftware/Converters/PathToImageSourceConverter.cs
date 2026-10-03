using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NeoDenSoftware.Converters;

/// <summary>Binds an on-disk image file path directly to an <see cref="System.Windows.Controls.Image"/>'s
/// Source - used by the Component Library grid's "Draw" thumbnail column. Null/missing path (no
/// photo uploaded for that component, or the file's been moved) yields no image rather than
/// throwing, so one bad row never breaks the rest of the grid.</summary>
public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
