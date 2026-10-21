using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietPhotos;

internal static class TestRunner
{
    private static int passed;
    private static string output;
    private static Application app;
    private static ViewerWindow window;
    [STAThread]
    public static int Main(string[] args)
    {
        output = args[0];
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using (var stream = typeof(ImageLoader).Assembly.GetManifestResourceStream("QuietPhotos.Theme.xaml"))
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        app.Startup += async delegate
        {
            try { await Run(); Console.WriteLine("PASS: " + passed + " checks"); app.Shutdown(0); }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); }
        };
        return app.Run();
    }
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception("FAIL: " + description); passed++; Console.WriteLine("PASS: " + description); }
    private static void Save(BitmapSource bitmap, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var f = File.Create(path)) encoder.Save(f); }
    private static BitmapSource Fixture(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(139, 183, 203), Color.FromRgb(238, 218, 188), 90), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 239, 198)), null, new Point(width * .7, height * .3), height * .105, height * .105);
            DrawPolygon(dc, Color.FromRgb(104, 144, 158), new[] { new Point(0, height * .64), new Point(width * .25, height * .25), new Point(width * .53, height * .66), new Point(width * .75, height * .42), new Point(width, height * .71), new Point(width, height), new Point(0, height) });
            DrawPolygon(dc, Color.FromRgb(54, 99, 111), new[] { new Point(0, height * .76), new Point(width * .3, height * .57), new Point(width * .6, height * .84), new Point(width * .9, height * .52), new Point(width, height * .6), new Point(width, height), new Point(0, height) });
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(100, 157, 171), Color.FromRgb(27, 74, 91), 90), null, new Rect(0, height * .77, width, height * .23));
            DrawPolygon(dc, Color.FromRgb(30, 57, 66), new[] { new Point(0, height * .85), new Point(width * .16, height * .8), new Point(width * .4, height), new Point(0, height) });
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    private static void DrawPolygon(DrawingContext dc, Color color, Point[] points)
    {
        var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        geometry.Freeze(); dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }
    private static async Task Run()
    {
        string fixtures = Path.Combine(output, "fixtures"); Directory.CreateDirectory(fixtures);
        string landscape = Path.Combine(fixtures, "Coast 02.png"), large = Path.Combine(fixtures, "Coast 10.png"), corrupt = Path.Combine(fixtures, "Damaged.png");
        Save(Fixture(3200, 2000), landscape); Save(Fixture(6000, 4000), large); File.WriteAllText(corrupt, "not an image");
        Save(Fixture(600, 400), Path.Combine(fixtures, "Coast 01.png"));
        var order = new[] { "photo10.png", "photo2.png", "photo1.png" }; Array.Sort(order, new NaturalOrder());
        Check(order[1] == "photo2.png", "Windows natural filename order");
        var loader = new ImageLoader(); var sw = Stopwatch.StartNew();
        var preview = await loader.LoadAsync(large, false, CancellationToken.None); sw.Stop();
        Check(preview.Width == 6000 && preview.Height == 4000 && preview.Bitmap.PixelWidth == 2560 && !preview.FullResolution, "Large image is downsampled while original dimensions survive");
        Console.WriteLine("MEASURE: 24MP PNG preview decode " + sw.ElapsedMilliseconds + " ms");
        using (var unlocked = new FileStream(large, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(unlocked.Length > 0, "Decoder releases original file handles");
        var cached = await loader.LoadAsync(large, false, CancellationToken.None);
        Check(Object.ReferenceEquals(preview, cached), "Repeated preview reuses bounded cache");
        var thumb1 = await loader.ThumbnailAsync(large, CancellationToken.None); var thumb2 = await loader.ThumbnailAsync(large, CancellationToken.None);
        Check(Object.ReferenceEquals(thumb1, thumb2) && thumb1.PixelWidth <= 144, "Filmstrip reuses small thumbnails");
        var canceled = new CancellationTokenSource(); canceled.Cancel(); bool canceledThrown = false;
        try { await loader.LoadAsync(large, true, canceled.Token); } catch (OperationCanceledException) { canceledThrown = true; }
        Check(canceledThrown, "Canceled image request does not decode");
        var full = await loader.LoadAsync(large, true, CancellationToken.None);
        Check(full.Bitmap.PixelWidth == 6000 && full.FullResolution, "Actual-pixel loading gets original resolution");
        Check(loader.CacheBytes <= 96L * 1024 * 1024, "Cache respects 96 MiB pixel budget");
        // Verify all EXIF orientations by checking the four corners, including reflected cases.
        byte[] pixels = { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 255, 255, 255, 255, 0, 255, 255, 255, 255, 0, 255 };
        var source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12); source.Freeze();
        int[][] expected = { new[] { 0, 2, 3, 5 }, new[] { 2, 0, 5, 3 }, new[] { 5, 3, 2, 0 }, new[] { 3, 5, 0, 2 }, new[] { 0, 3, 2, 5 }, new[] { 3, 0, 5, 2 }, new[] { 5, 2, 3, 0 }, new[] { 2, 5, 0, 3 } };
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            string path = Path.Combine(output, "exif" + orientation + ".tif"); var meta = new BitmapMetadata("tiff"); meta.SetQuery("/ifd/{ushort=274}", (ushort)orientation);
            var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None }; encoder.Frames.Add(BitmapFrame.Create(source, null, meta, null)); using (var file = File.Create(path)) encoder.Save(file);
            var decoded = ImageLoader.Decode(path, 0); bool swap = orientation >= 5;
            Check(decoded.Width == (swap ? 2 : 3) && decoded.Height == (swap ? 3 : 2), "EXIF " + orientation + " dimensions");
            var converted = new FormatConvertedBitmap(decoded.Bitmap, PixelFormats.Bgra32, null, 0); byte[] actual = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(actual, converted.PixelWidth * 4, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight; int[] corners = { 0, w - 1, (h - 1) * w, w * h - 1 };
            bool correct = true; for (int c = 0; c < 4; c++) for (int b = 0; b < 4; b++) if (actual[corners[c] * 4 + b] != pixels[expected[orientation - 1][c] * 4 + b]) correct = false;
            Check(correct, "EXIF " + orientation + " corner pixels");
        }
        byte[] originalHash; using (var sha = SHA256.Create()) using (var file = File.OpenRead(landscape)) originalHash = sha.ComputeHash(file);
        string export = Path.Combine(output, "rotated-copy.png"); var original = ImageLoader.Decode(landscape, 0); ViewerWindow.Export(original.Bitmap, 90, export);
        var rotated = ImageLoader.Decode(export, 0);
        Check(rotated.Width == 2000 && rotated.Height == 3200, "Rotated export keeps full resolution and swaps dimensions");
        ViewerWindow.Export(original.Bitmap, 180, export);
        Check(ImageLoader.Decode(export, 0).Width == 3200, "Saving over a copy atomically replaces it");
        using (var sha = SHA256.Create()) using (var file = File.OpenRead(landscape)) Check(originalHash.SequenceEqual(sha.ComputeHash(file)), "Export preserves source bytes");
        window = new ViewerWindow(new string[0]); window.Show(); await Task.Delay(200); Snapshot("empty-state.png");
        await window.OpenPathAsync(landscape); await Task.Delay(300);
        Check(window.Surface.Data != null && window.CurrentPath == landscape, "Window opens image and scans its folder");
        Check(!window.IsLoading && window.LastError == null, "Viewer leaves loading state after success");
        Snapshot("viewer.png");
        double canvasBeforeInfo = window.Surface.ActualWidth;
        Click("File information (I)"); await Task.Delay(100);
        Check(window.Surface.ActualWidth < canvasBeforeInfo, "Information command opens side panel and refits canvas");
        Snapshot("file-information.png"); Click("Close information"); await Task.Delay(30);
        Click("Fullscreen (F11)"); await Task.Delay(100);
        Check(window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized, "Fullscreen command uses borderless maximized window");
        // Header is hidden in fullscreen; invoke keyboard path via the window's routed key event.
        var sourceInput = PresentationSource.FromVisual(window);
        window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, sourceInput, 0, System.Windows.Input.Key.F11) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
        await Task.Delay(100); Check(window.WindowStyle == WindowStyle.SingleBorderWindow, "F11 restores window chrome");
        var anchor = new Point(window.Surface.ActualWidth * .7, window.Surface.ActualHeight * .4); window.Surface.SetZoom(window.Surface.Zoom * 1.2, anchor);
        Check(!window.Surface.IsFit, "Wheel-style anchored zoom leaves fit mode");
        window.Surface.Actual(); await Task.Delay(100);
        Check(!window.Surface.IsFit && Math.Abs(window.Surface.Zoom * window.Surface.PixelRatio - 1) < .001, "1:1 uses physical device pixels");
        window.Surface.Fit(); Check(window.Surface.IsFit && window.Surface.Zoom > 0, "Fit restores usable image scale");
        window.Surface.Rotate(90); Check(window.Surface.Rotation == 90, "View rotation responds without touching file");
        window.Surface.Rotate(-90);
        window.Surface.CycleBackdrop(); Check(window.Surface.BackdropMode == 1, "Transparency checkerboard can be selected"); window.Surface.CycleBackdrop(); window.Surface.CycleBackdrop();
        var requests = new[] { window.OpenPathAsync(large), window.OpenPathAsync(landscape), window.OpenPathAsync(large) }; await Task.WhenAll(requests);
        Check(window.CurrentPath == large && window.Surface.Data != null && window.Surface.Data.Width == 6000, "Rapid requests show the newest selection");
        await window.NavigateAsync(1); Check(window.LastError != null && window.Surface.Data == null && !window.IsLoading, "Corrupt file reports an error and clears stale image");
        await window.NavigateAsync(-1); Check(window.LastError == null && window.Surface.Data != null, "Navigation recovers after corrupt file");
        await ExtendedFormats(landscape);
        await TransparencyAndMenu();
        string report = "Startup to first image in in-process UI harness: " + window.FirstImageMilliseconds.ToString("0") + " ms\n24MP PNG decode: " + sw.ElapsedMilliseconds + " ms\nProcess working set (test harness, includes fixtures and prior full-size decodes): " + ImageLoader.FormatBytes(Process.GetCurrentProcess().WorkingSet64) + "\nChecks passed: " + passed + "\n";
        File.WriteAllText(Path.Combine(output, "results.txt"), report); Console.WriteLine(report); window.Close();
    }
    private static BitmapSource RenderElement(FrameworkElement element)
    {
        element.UpdateLayout();
        var rendered = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(element); return rendered;
    }
    private static Color Sample(BitmapSource image, int x, int y)
    {
        byte[] pixel = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static async Task TransparencyAndMenu()
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) dc.DrawRectangle(Brushes.Crimson, null, new Rect(0, 0, 100, 140));
        var png = new RenderTargetBitmap(200, 140, 96, 96, PixelFormats.Pbgra32); png.Render(drawing);
        string path = Path.Combine(output, "transparent.png"); Save(png, path); byte[] original = File.ReadAllBytes(path);
        await window.OpenPathAsync(path); window.Surface.BackdropMode = 0;
        var transparent = new Color[3]; var opaque = new Color[3]; var outside = new Color[3];
        for (int mode = 0; mode < 3; mode++)
        {
            var surface = window.Surface;
            var rendered = RenderElement(surface);
            int center = (int)(surface.ActualWidth / 2), y = (int)(surface.ActualHeight / 2);
            int offset = Math.Max(1, (int)(50 * surface.Zoom));
            transparent[mode] = Sample(rendered, center + offset, y);
            opaque[mode] = Sample(rendered, center - offset, y); outside[mode] = Sample(rendered, 1, 1);
            if (mode == 1) Snapshot("transparency-background.png");
            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.B) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
        }
        Check(transparent.Distinct().Count() == 3, "Dark/checker/light visibly change transparent PNG pixels through the B shortcut");
        Check(opaque.Distinct().Count() == 1 && opaque[0] == Colors.Crimson, "Transparency background preserves opaque image pixels");
        Check(outside.Distinct().Count() == 1, "Transparency background leaves surrounding canvas unchanged");
        Check(original.SequenceEqual(File.ReadAllBytes(path)), "Changing transparency background never rewrites the PNG");
        var more = FindButton(window, "More options"); more.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100); var menu = more.ContextMenu; menu.UpdateLayout();
        Check(menu.IsOpen && menu.Items.OfType<MenuItem>().Any(i => Object.Equals(i.Header, "Transparency background · Dark")), "More menu shows the current transparency background mode");
        var menuImage = RenderElement(menu); Save(menuImage, Path.Combine(output, "dark-menu.png"));
        var gutter = Sample(menuImage, 10, 18);
        Check(gutter.R < 100 && gutter.G < 100 && gutter.B < 100, "More menu left gutter renders dark instead of the Windows white icon strip");
        menu.IsOpen = false;
    }
    private static async Task ExtendedFormats(string source)
    {
        string folder = Path.Combine(output, "extended"); Directory.CreateDirectory(folder);
        string psd = Path.Combine(folder, "composite.psd");
        using (var writer = new BinaryWriter(File.Create(psd)))
        {
            writer.Write(new byte[] { 56, 66, 80, 83, 0, 1, 0, 0, 0, 0, 0, 0, 0, 3 });
            writer.Write(new byte[] { 0, 0, 0, 2, 0, 0, 0, 3, 0, 8, 0, 3 });
            writer.Write(new byte[14]); // Empty color/resource/layer sections and raw compression.
            writer.Write(new byte[] { 255, 255, 255, 0, 0, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0 });
        }
        byte[] sourceBytes = File.ReadAllBytes(psd);
        var decoded = ImageLoader.Decode(psd, 2560);
        Check(decoded.Width == 3 && decoded.Height == 2 && decoded.PreviewOnly, "Photoshop composite crosses the isolated decoder protocol as a read-only preview");
        Check(ImageLoader.Decode(psd, 0).FullResolution, "Full-resolution project preview accepts the viewer's zero-edge protocol");
        await window.OpenPathAsync(psd);
        Check(window.LastError == null && window.Surface.Data.PreviewOnly, "Viewer opens Photoshop document without an editor installed");
        Check(!FindButton(window, "Save a copy (Ctrl+Shift+S)").IsEnabled, "Editing/export control is disabled for original project documents");
        Snapshot("04-project-preview.png");
        Check(sourceBytes.SequenceEqual(File.ReadAllBytes(psd)), "Photoshop preview leaves source bytes unchanged");
        string pages = Path.Combine(folder, "pages.tiff");
        var encoder = new TiffBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Fixture(80, 60))); encoder.Frames.Add(BitmapFrame.Create(Fixture(30, 20)));
        using (var stream = File.Create(pages)) encoder.Save(stream);
        await window.OpenPathAsync(pages); Check(window.Surface.Data.FrameCount == 2, "TIFF page count appears in viewer");
        await window.ShowFrameAsync(1);
        Check(window.LastError == null && window.Surface.Data.FrameIndex == 1 && window.Surface.Data.Width == 30, "Next-page control decodes the selected TIFF page");
        Snapshot("05-page-navigation.png");
        await window.ShowFrameAsync(-1); Check(window.Surface.Data.FrameIndex == 0 && window.Surface.Data.Width == 80, "Previous-page control returns to TIFF page one");
        window.Surface.FitWidth(); Check(window.Surface.IsFit && window.Surface.Zoom > 0, "Fit-to-width has a usable scale");
        window.Surface.ToggleSharpPixels(); Check(RenderOptions.GetBitmapScalingMode(window.Surface) == BitmapScalingMode.NearestNeighbor, "Pixel-sharp scaling selects nearest-neighbor rendering");
        string xmp = Path.ChangeExtension(source, ".xmp"); File.WriteAllText(xmp, "<x:xmpmeta xmlns:x='adobe:ns:meta/'/>");
        var sidecar = ImageLoader.Decode(xmp, 2560);
        Check(sidecar.PreviewOnly && sidecar.Width == 3200 && sidecar.PreviewNote.Contains("not applied"), "XMP resolves its adjacent original and labels unapplied Lightroom adjustments");
        var sidecarLoader = new ImageLoader();
        var firstSidecar = await sidecarLoader.LoadAsync(xmp, false, CancellationToken.None);
        Check(Object.ReferenceEquals(firstSidecar, await sidecarLoader.LoadAsync(xmp, false, CancellationToken.None)), "Unchanged XMP companion preview reuses the bounded cache");
        string changedXmp = Path.Combine(folder, "changed.xmp"), changedSource = Path.Combine(folder, "changed.png");
        File.WriteAllText(changedXmp, "<xmp/>"); Save(Fixture(80, 60), changedSource);
        await sidecarLoader.LoadAsync(changedXmp, false, CancellationToken.None);
        Save(Fixture(30, 20), changedSource);
        Check((await sidecarLoader.LoadAsync(changedXmp, false, CancellationToken.None)).Width == 30, "XMP cache invalidates when its original image changes");
        bool missing = false;
        try { ImageLoader.Decode(Path.Combine(folder, "missing.xmp"), 2560); } catch (NotSupportedException e) { missing = e.Message.Contains("not image pixels"); }
        Check(missing, "XMP without an original is not advertised as a rendered image");
        await window.OpenPathAsync(source);
    }
    private static void Snapshot(string filename)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content); Save(bitmap, Path.Combine(output, filename));
    }
    private static void Click(string name)
    {
        var button = FindButton(window, name); if (button == null) throw new Exception("Button not found: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static Button FindButton(DependencyObject node, string name)
    {
        var button = node as Button; if (button != null && AutomationProperties.GetName(button) == name) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var found = FindButton(VisualTreeHelper.GetChild(node, i), name); if (found != null) return found; }
        return null;
    }
}
