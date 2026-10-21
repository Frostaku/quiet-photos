using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietPhotos
{
    public sealed class ImageData
    {
        public BitmapSource Bitmap;
        public int Width, Height;
        public string Details, Path;
        public bool FullResolution;
        public int FrameCount = 1, FrameIndex;
        public bool PreviewOnly;
        public string PreviewNote = "";
        public long Bytes { get { return ((long)Bitmap.PixelWidth * Bitmap.Format.BitsPerPixel + 7) / 8 * Bitmap.PixelHeight; } }
    }

    public sealed class NaturalOrder : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
        public int Compare(string a, string b) { return StrCmpLogicalW(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b)); }
    }

    public sealed class ImageLoader
    {
        public static readonly HashSet<string> Extensions = new HashSet<string>(new[] { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".hdp", ".webp", ".heic", ".heif", ".hif", ".avif", ".apng", ".svg", ".svgz", ".psd", ".psb", ".xcf", ".ora", ".kra", ".tga", ".targa", ".pcx", ".qoi", ".ppm", ".pgm", ".pbm", ".pnm", ".pam", ".dds", ".hdr", ".exr", ".jp2", ".j2k", ".jxl", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".raf", ".rw2", ".pef", ".srw", ".xmp" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> NativeExtensions = new HashSet<string>(new[] { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".hdp" }, StringComparer.OrdinalIgnoreCase);
        public static string OpenFilter { get { return "Supported images and previews|" + String.Join(";", Extensions.OrderBy(x => x).Select(x => "*" + x)) + "|All files|*.*"; } }
        public static bool EditorDocument(string path) { return new[] { ".psd", ".psb", ".xcf", ".ora", ".kra", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".raf", ".rw2", ".pef", ".srw", ".xmp" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
        private const long Budget = 96L * 1024 * 1024;
        private readonly SemaphoreSlim decodeGate = new SemaphoreSlim(1);
        private readonly SemaphoreSlim thumbGate = new SemaphoreSlim(1);
        private readonly Dictionary<string, ImageData> cache = new Dictionary<string, ImageData>();
        private readonly LinkedList<string> lru = new LinkedList<string>();
        private readonly object sync = new object();
        private readonly Dictionary<string, BitmapSource> thumbCache = new Dictionary<string, BitmapSource>();
        private readonly LinkedList<string> thumbLru = new LinkedList<string>();
        private long used;
        public long CacheBytes { get { lock (sync) return used; } }
        public static bool Supported(string path) { return Extensions.Contains(System.IO.Path.GetExtension(path)); }

        public async Task<ImageData> LoadAsync(string path, bool full, CancellationToken token, int frameIndex = 0)
        {
            token.ThrowIfCancellationRequested();
            var key = FileStamp(path) + "|" + full + "|" + frameIndex;
            lock (sync)
            {
                ImageData hit;
                if (cache.TryGetValue(key, out hit)) { lru.Remove(key); lru.AddFirst(key); return hit; }
            }
            await decodeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var data = await Task.Run(delegate { return Decode(path, full ? 0 : 2560, frameIndex, token); }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (data.Bytes <= Budget && !cache.ContainsKey(key))
                    {
                        while (lru.Count > 0 && (used + data.Bytes > Budget || lru.Count >= 6))
                        { var old = lru.Last.Value; used -= cache[old].Bytes; cache.Remove(old); lru.RemoveLast(); }
                        cache[key] = data; lru.AddFirst(key); used += data.Bytes;
                    }
                }
                return data;
            }
            finally { decodeGate.Release(); }
        }

        public async Task<BitmapSource> ThumbnailAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string key = FileStamp(path);
            lock (sync)
            {
                BitmapSource hit;
                if (thumbCache.TryGetValue(key, out hit)) { thumbLru.Remove(key); thumbLru.AddFirst(key); return hit; }
            }
            await thumbGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var bitmap = await Task.Run(delegate { return Decode(path, 144, 0, token).Bitmap; }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (!thumbCache.ContainsKey(key))
                    {
                        while (thumbLru.Count >= 64) { thumbCache.Remove(thumbLru.Last.Value); thumbLru.RemoveLast(); }
                        thumbCache[key] = bitmap; thumbLru.AddFirst(key);
                    }
                }
                return bitmap;
            }
            finally { thumbGate.Release(); }
        }

        private static string FileStamp(string path)
        {
            var fi = new FileInfo(path);
            string stamp = path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            if (System.IO.Path.GetExtension(path).Equals(".xmp", StringComparison.OrdinalIgnoreCase))
            {
                string source = ExtendedDecoder.Companion(path);
                if (source != null) { var original = new FileInfo(source); stamp += "|" + source + "|" + original.Length + "|" + original.LastWriteTimeUtc.Ticks; }
            }
            return stamp;
        }

        public static ImageData Decode(string path, int limit, int frameIndex = 0, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();
            if (!NativeExtensions.Contains(System.IO.Path.GetExtension(path)) || frameIndex != 0) return ExtendedDecoder.Read(path, limit, frameIndex, token);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                int width = frame.PixelWidth, height = frame.PixelHeight;
                if (limit == 0 && (long)width * height > 120000000)
                    throw new InvalidOperationException("This image is too large for full-resolution loading (120 megapixel limit). Fit view remains available.");
                var metadata = frame.Metadata as BitmapMetadata;
                int orientation = 1;
                try { if (metadata != null && metadata.ContainsQuery("/app1/ifd/{ushort=274}")) orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"));
                    else if (metadata != null && metadata.ContainsQuery("/ifd/{ushort=274}")) orientation = Convert.ToInt32(metadata.GetQuery("/ifd/{ushort=274}")); } catch (Exception) { }
                string camera = "", taken = "";
                try { if (metadata != null) { camera = ((metadata.CameraManufacturer ?? "") + " " + (metadata.CameraModel ?? "")).Trim(); taken = metadata.DateTaken; } } catch (Exception) { }
                file.Position = 0;
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = file;
                if (limit > 0 && Math.Max(width, height) > limit)
                { if (width >= height) image.DecodePixelWidth = limit; else image.DecodePixelHeight = limit; }
                image.EndInit(); image.Freeze();
                BitmapSource bitmap = image;
                Transform transform = OrientationTransform(orientation);
                if (transform != null) { bitmap = new TransformedBitmap(bitmap, transform); bitmap.Freeze(); }
                bool swap = orientation >= 5 && orientation <= 8;
                var info = new FileInfo(path);
                return new ImageData { Bitmap = bitmap, Width = swap ? height : width, Height = swap ? width : height,
                    Path = path, FrameCount = Math.Min(decoder.Frames.Count, 64), FrameIndex = 0, FullResolution = limit == 0 || Math.Max(width, height) <= limit,
                    Details = "DIMENSIONS\n" + (swap ? height : width) + " × " + (swap ? width : height) + " pixels\n\nFILE SIZE\n" + FormatBytes(info.Length) +
                        "\n\nFORMAT\n" + System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + (decoder.Frames.Count > 1 ? " · page / frame 1 of " + Math.Min(decoder.Frames.Count, 64) : "") +
                        "\n\nMODIFIED\n" + info.LastWriteTime.ToString("dd MMM yyyy, HH:mm") +
                        (String.IsNullOrWhiteSpace(camera) ? "" : "\n\nCAMERA\n" + camera) + (String.IsNullOrWhiteSpace(taken) ? "" : "\n\nDATE TAKEN\n" + taken) };
            }
        }
        private static Transform OrientationTransform(int orientation)
        {
            // EXIF transforms apply in source coordinates, before the viewer's own rotation.
            switch (orientation)
            {
                case 2: return new ScaleTransform(-1, 1);
                case 3: return new RotateTransform(180);
                case 4: return new ScaleTransform(1, -1);
                case 5: return new MatrixTransform(0, 1, 1, 0, 0, 0);
                case 6: return new RotateTransform(90);
                case 7: return new MatrixTransform(0, -1, -1, 0, 0, 0);
                case 8: return new RotateTransform(270);
                default: return null;
            }
        }
        public static string FormatBytes(long value)
        { return value >= 1048576 ? (value / 1048576.0).ToString("0.##") + " MB" : (value / 1024.0).ToString("0.#") + " KB"; }
    }
}
