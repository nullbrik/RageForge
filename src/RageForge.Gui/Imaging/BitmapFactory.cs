/* RAGEFORGE - Bitmap Factory */

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RageForge.Gui.Imaging;

public static class BitmapFactory
{
    public static WriteableBitmap FromRgba(byte[] rgba, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var buffer = bitmap.Lock();
        int srcStride = width * 4;
        for (int y = 0; y < height; y++)
            Marshal.Copy(rgba, y * srcStride, buffer.Address + y * buffer.RowBytes, srcStride);

        return bitmap;
    }
}
