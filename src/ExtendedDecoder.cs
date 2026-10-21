using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Media.Imaging;

namespace QuietPhotos
{
    internal static class ExtendedDecoder
    {
        internal static string Companion(string path)
        {
            string stem = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            if (File.Exists(stem) && ImageLoader.Supported(stem) && !Path.GetExtension(stem).Equals(".xmp", StringComparison.OrdinalIgnoreCase)) return stem;
            foreach (string ext in new[] { ".dng", ".cr3", ".cr2", ".nef", ".arw", ".orf", ".raf", ".rw2", ".pef", ".srw", ".jpg", ".jpeg", ".tif", ".tiff", ".png" })
                if (File.Exists(stem + ext)) return stem + ext;
            return null;
        }
        public static ImageData Read(string path, int limit, int frameIndex, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".lrcat" || extension == ".lrdata")
                throw new NotSupportedException("Lightroom catalogs and preview-cache folders are not individual images. Open the original photograph or DNG, or export a preview from Lightroom.");
            if (extension == ".xmp")
            {
                string original = Companion(path);
                if (original == null) throw new NotSupportedException("XMP contains editing instructions, not image pixels. Keep it beside its matching original image or open it in Lightroom/Camera Raw.");
                var data = ImageLoader.Decode(original, limit, frameIndex, token);
                data.Path = path; data.PreviewOnly = true;
                data.PreviewNote = "XMP companion source preview; Lightroom/Camera Raw adjustments are not applied.";
                data.Details += "\n\nPREVIEW\n" + data.PreviewNote + "\n\nSOURCE IMAGE\n" + original;
                return data;
            }
            string worker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codecs", "QuietPhotos.Decode.exe");
            if (!File.Exists(worker)) throw new NotSupportedException("The portable decoder is missing. Keep the codecs folder beside QuietPhotos.exe.");
            var start = new ProcessStartInfo(worker, "\"" + Path.GetFullPath(path) + "\" " + limit + " " + frameIndex)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var output = Task.Run(delegate
                {
                    using (var buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[65536]; int read;
                        while ((read = process.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            if (buffer.Length + read > 64L * 1024 * 1024) throw new InvalidDataException("Decoded PNG exceeds the 64 MiB transfer limit. Use fit view or a smaller image.");
                            buffer.Write(chunk, 0, read);
                        }
                        return buffer.ToArray();
                    }
                });
                var errors = process.StandardError.ReadToEndAsync();
                // A faulted output reader must not leave the child blocked on its full pipe.
                var deadline = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (token.IsCancellationRequested) { process.Kill(); process.WaitForExit(); token.ThrowIfCancellationRequested(); }
                    if (output.IsFaulted || deadline.ElapsedMilliseconds > 30000)
                    { process.Kill(); process.WaitForExit(); throw new InvalidDataException(output.IsFaulted ? output.Exception.GetBaseException().Message : "The decoder exceeded its 30-second time limit."); }
                }
                string error = errors.GetAwaiter().GetResult().Trim();
                if (process.ExitCode != 0) throw new NotSupportedException(String.IsNullOrEmpty(error) ? "The decoder could not render this file." : error);
                using (var buffer = new MemoryStream(output.GetAwaiter().GetResult())) using (var reader = new BinaryReader(buffer, Encoding.UTF8))
                {
                    if (reader.ReadString() != "QPIMG1") throw new InvalidDataException("The decoder returned an invalid image response.");
                    int width = reader.ReadInt32(), height = reader.ReadInt32(), count = reader.ReadInt32(); string note = reader.ReadString();
                    if (width < 1 || height < 1 || count < 1 || frameIndex < 0 || frameIndex >= count) throw new InvalidDataException("The decoder returned invalid image dimensions or frame counts.");
                    var bitmap = new BitmapImage();
                    using (var png = new MemoryStream(reader.ReadBytes(checked((int)(buffer.Length - buffer.Position)))))
                    { bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = png; bitmap.EndInit(); bitmap.Freeze(); }
                    var info = new FileInfo(path);
                    return new ImageData { Bitmap = bitmap, Width = width, Height = height, FrameCount = count, FrameIndex = frameIndex,
                        FullResolution = limit == 0 || Math.Max(width, height) <= limit, Path = path,
                        PreviewOnly = ImageLoader.EditorDocument(path), PreviewNote = note,
                        Details = "DIMENSIONS\n" + width + " × " + height + " pixels\n\nFILE SIZE\n" + ImageLoader.FormatBytes(info.Length) +
                            "\n\nFORMAT\n" + extension.TrimStart('.').ToUpperInvariant() + "\n\nPAGE / FRAME\n" + (frameIndex + 1) + " / " + count +
                            "\n\nMODIFIED\n" + info.LastWriteTime.ToString("dd MMM yyyy, HH:mm") + (String.IsNullOrEmpty(note) ? "" : "\n\nPREVIEW\n" + note) };
                }
            }
        }
    }
}
