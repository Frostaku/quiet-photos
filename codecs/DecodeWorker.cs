using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using ImageMagick;

// Decoding lives in a short-lived process: the viewer imposes a wall-clock timeout.
// Never write to the supplied source and never pass user text as an ImageMagick filename.
internal static class DecodeWorker
{
    internal static readonly Dictionary<string, string> Formats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        {".jpg","Jpeg"},{".jpeg","Jpeg"},{".jpe","Jpeg"},{".jfif","Jpeg"},
        {".png","Png"},{".apng","Png"},{".bmp","Bmp"},{".dib","Bmp"},
        {".gif","Gif"},{".tif","Tiff"},{".tiff","Tiff"},{".webp","WebP"},
        {".avif","Avif"},{".heic","Heic"},{".heif","Heic"},{".hif","Heic"},{".jxl","Jxl"},
        {".ico","Ico"},{".cur","Cur"},{".tga","Tga"},{".targa","Tga"},{".pcx","Pcx"},
        {".ppm","Ppm"},{".pgm","Pgm"},{".pbm","Pbm"},{".pnm","Pnm"},{".pam","Pam"},
        {".jp2","Jp2"},{".j2k","J2k"},{".jpf","Jp2"},{".jpx","Jp2"},
        {".dds","Dds"},{".hdr","Hdr"},{".exr","Exr"},{".qoi","Qoi"},
        {".psd","Psd"},{".psb","Psb"},{".xcf","Xcf"},{".svg","Msvg"},{".svgz","Msvg"},
        {".dng","Dng"},{".cr2","Cr2"},{".cr3","Cr3"},{".nef","Nef"},
        {".arw","Arw"},{".raf","Raf"},{".orf","Orf"},{".rw2","Rw2"},{".pef","Pef"},{".srw","Srw"}
    };
    private const long MaxInput = 512L * 1024 * 1024;
    private const long MaxMerged = 128L * 1024 * 1024;
    private static int Main(string[] args)
    {
        try {
            if (args.Length != 3) throw new ArgumentException("Expected path, maximum edge, and page index.");
            int edge = int.Parse(args[1]), frame = int.Parse(args[2]);
            if ((edge != 0 && edge < 32) || edge > 8192 || frame < 0 || frame >= 64) throw new ArgumentException("Preview bounds are invalid.");
            string path = Path.GetFullPath(args[0]);
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Only local files are supported.");
            string ext = Path.GetExtension(path), format;
            bool archive = ext.Equals(".ora", StringComparison.OrdinalIgnoreCase) || ext.Equals(".kra", StringComparison.OrdinalIgnoreCase);
            if (!archive && !Formats.TryGetValue(ext, out format)) throw new ArgumentException("Unsupported image extension.");
            format = archive ? "Png" : Formats[ext];
            var info = new FileInfo(path);
            if (info.Length < 1 || info.Length > MaxInput) throw new IOException("Source exceeds the 512 MiB preview limit.");
            MagickNET.Initialize(AppDomain.CurrentDomain.BaseDirectory);
            ResourceLimits.Memory = 512UL * 1024 * 1024;
            ResourceLimits.Disk = 0;
            ResourceLimits.Width = 40000;
            ResourceLimits.Height = 40000;
            ResourceLimits.Area = 100000000;
            ResourceLimits.ListLength = 64;
            ResourceLimits.Thread = 2;
            ResourceLimits.Time = 20;
            ResourceLimits.MaxProfileSize = 16UL * 1024 * 1024;
            using (var source = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (archive) {
                    using (var merged = ReadMerged(source)) Decode(merged, "Png", edge, frame, "Embedded flattened preview; layers are not edited.", ext);
                } else if (format == "Msvg") {
                    if (ext.Equals(".svgz", StringComparison.OrdinalIgnoreCase)) {
                        using (var gzip = new GZipStream(source, CompressionMode.Decompress, true))
                        using (var safe = SafeSvg(gzip)) Decode(safe, format, edge, frame, "SVG raster preview; external resources disabled.", ext);
                    } else using (var safe = SafeSvg(source)) Decode(safe, format, edge, frame, "SVG raster preview; external resources disabled.", ext);
                } else {
                    string note = (format == "Psd" || format == "Psb") ? "Photoshop saved composite; layers are not edited."
                        : format == "Xcf" ? "GIMP layer composite; advanced effects may differ."
                        : IsRaw(ext) ? "Camera RAW rendering; Lightroom develop edits are not applied." : "";
                    Decode(source, format, edge, frame, note, ext);
                }
            }
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
    private static bool IsRaw(string ext) {
        return ".dng.cr2.cr3.nef.arw.raf.orf.rw2.pef.srw".IndexOf(ext.ToLowerInvariant(), StringComparison.Ordinal) >= 0;
    }
    private static void Decode(Stream input, string name, int edge, int frame, string note, string ext)
    {
        var settings = new MagickReadSettings { Format = (MagickFormat)Enum.Parse(typeof(MagickFormat), name, true) };
        // Photoshop's first image is the composite saved for compatibility.
        if (name == "Psd" || name == "Psb") { settings.FrameIndex = 0; settings.FrameCount = 1; settings.SetDefine(MagickFormat.Psd, "skip-layers", true); }
        using (var images = new MagickImageCollection()) {
            images.Read(input, settings);
            if (images.Count == 0) throw new IOException("No image preview is stored in this file.");
            if (name == "Gif") images.Coalesce();
            if (frame >= images.Count || (name == "Xcf" && frame != 0)) throw new ArgumentException("Page index is outside the image.");
            if (name == "Xcf") {
                // The XCF coder returns layers in compositing order (bottom first).
                using (var merged = images.Flatten()) Output(merged, edge, 1, note);
            } else Output(images[frame], edge, images.Count, note);
        }
    }
    private static void Output(IMagickImage<byte> image, int edge, int count, string note)
    {
        image.AutoOrient();
        int width = checked((int)image.Width), height = checked((int)image.Height);
        if ((long)width * height > 100000000) throw new IOException("Image exceeds the 100 megapixel preview limit.");
        if (edge != 0 && (width > edge || height > edge)) image.Resize(new MagickGeometry((uint)edge, (uint)edge) { Greater = true });
        image.Strip();
        // XCF can report a 32-bit sample depth despite this Q8 pipeline. Emit
        // explicit 8-bit PNG samples rather than inheriting project precision.
        image.Depth = 8;
        byte[] png = image.ToByteArray(MagickFormat.Png);
        using (var writer = new BinaryWriter(Console.OpenStandardOutput(), Encoding.UTF8)) {
            writer.Write("QPIMG1"); writer.Write(width); writer.Write(height); writer.Write(count); writer.Write(note); writer.Write(png);
        }
    }
    private static MemoryStream ReadMerged(Stream source)
    {
        using (var zip = new ZipArchive(source, ZipArchiveMode.Read, true)) {
            if (zip.Entries.Count > 10000) throw new IOException("Archive contains too many entries.");
            ZipArchiveEntry found = null;
            foreach (var e in zip.Entries) if (e.FullName == "mergedimage.png") {
                if (found != null) throw new IOException("Ambiguous merged preview.");
                found = e;
            }
            if (found == null) throw new IOException("This project does not contain mergedimage.png. Save a merged preview in its editor.");
            if (found.Length < 1 || found.Length > MaxMerged) throw new IOException("Embedded preview exceeds 128 MiB.");
            var result = new MemoryStream();
            using (var entry = found.Open()) {
                byte[] chunk = new byte[65536]; int read;
                while ((read = entry.Read(chunk, 0, chunk.Length)) != 0) {
                    if (result.Length + read > MaxMerged) { result.Dispose(); throw new IOException("Embedded preview exceeds 128 MiB."); }
                    result.Write(chunk, 0, read);
                }
            }
            result.Position = 0; return result;
        }
    }
    private static MemoryStream SafeSvg(Stream source)
    {
        if (source.CanSeek && source.Length > 8 * 1024 * 1024) throw new IOException("SVG exceeds 8 MiB.");
        var xml = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 })) xml.Load(reader);
        if (xml.DocumentElement == null || xml.DocumentElement.LocalName != "svg") throw new IOException("Not an SVG document.");
        if (xml.SelectNodes("//processing-instruction()").Count != 0) throw new IOException("SVG processing instructions are disabled.");
        foreach (XmlNode node in xml.SelectNodes("//*")) {
            string tag = node.LocalName.ToLowerInvariant();
            if (tag == "script" || tag == "foreignobject" || tag == "image" || tag == "style") throw new IOException("SVG active content, embedded images and style sheets are disabled.");
            foreach (XmlAttribute a in node.Attributes) {
                string value = a.Value.Trim();
                if (a.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.LocalName == "base" ||
                    ((a.LocalName == "href" || a.LocalName == "src") && !value.StartsWith("#", StringComparison.Ordinal)) ||
                    value.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("@import", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf('\\') >= 0)
                    throw new IOException("SVG external references and URL paints are disabled.");
            }
        }
        var result = new MemoryStream(Encoding.UTF8.GetBytes(xml.OuterXml)); return result;
    }
}
