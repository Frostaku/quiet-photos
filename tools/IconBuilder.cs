using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class IconBuilder
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            DrawingImage source;
            using (var file = File.OpenRead(args[0])) source = (DrawingImage)XamlReader.Load(file);
            source.Freeze(); Directory.CreateDirectory(args[1]);
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256, 512 };
            var frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, size, size));
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var memory = new MemoryStream())
                {
                    encoder.Save(memory); byte[] bytes = memory.ToArray();
                    File.WriteAllBytes(Path.Combine(args[1], "QuietPhotos-" + size + ".png"), bytes);
                    if (size <= 256) frames.Add(size == 256 ? bytes : DibFrame(bitmap, size));
                }
            }
            using (var writer = new BinaryWriter(File.Create(Path.Combine(args[1], "QuietPhotos.ico"))))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int size = sizes[i]; writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
                }
                foreach (var frame in frames) writer.Write(frame);
            }
            Console.WriteLine("Created original icon PNGs and a nine-size Windows ICO."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static byte[] DibFrame(BitmapSource image, int size)
    {
        // MSI's legacy icon control requires DIB frames at small sizes; retain PNG at 256px.
        var straight = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = size * 4, maskStride = ((size + 31) / 32) * 4;
        byte[] pixels = new byte[stride * size]; straight.CopyPixels(pixels, stride, 0);
        using (var memory = new MemoryStream()) using (var writer = new BinaryWriter(memory))
        {
            writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(stride * size); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = size - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
            for (int y = size - 1; y >= 0; y--)
            {
                byte[] mask = new byte[maskStride];
                for (int x = 0; x < size; x++) if (pixels[y * stride + x * 4 + 3] == 0) mask[x / 8] |= (byte)(1 << (7 - x % 8));
                writer.Write(mask);
            }
            return memory.ToArray();
        }
    }
}
