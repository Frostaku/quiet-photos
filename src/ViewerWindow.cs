using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QuietPhotos
{
    public sealed class ViewerWindow : Window
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string source, string destination, int flags);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenAsInfo { [MarshalAs(UnmanagedType.LPWStr)] public string File; [MarshalAs(UnmanagedType.LPWStr)] public string Class; public uint Flags; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);
        private readonly ImageLoader loader = new ImageLoader();
        internal readonly ImageSurface Surface = new ImageSurface();
        private readonly TextBlock filename = Text("Quiet Photos", 14), subtitle = Text("A little less between you and your photos.", 11);
        private readonly TextBlock status = Text("LOCAL FILES. JUST OPEN AND VIEW.", 11), position = Text("", 12), zoomLabel = Text("100%", 12);
        private readonly TextBlock detailsText = Text("", 12), detailsName = Text("", 17), messageText = Text("", 14), loading = Text("", 12);
        private readonly StackPanel thumbnails = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly List<Button> imageButtons = new List<Button>();
        private Border empty, infoPanel, filmstrip, errorPanel;
        private Border pageControls;
        private readonly TextBlock pageLabel = Text("", 12);
        private Button previousPage, nextPage;
        private int currentFrame;
        private Grid root;
        private FrameworkElement toolbar, statusbar;
        private Button prev, next, stripButton, slideshowButton, infoButton;
        private Button saveCopyButton;
        private MenuItem backdropMenuItem;
        private List<string> files = new List<string>();
        private int index = -1, folderVersion, loadVersion, thumbVersion;
        private CancellationTokenSource imageCancel = new CancellationTokenSource(), thumbCancel = new CancellationTokenSource();
        private bool fullLoading, fullscreen, stripVisible = true, disposed, exporting;
        private WindowState beforeFull;
        private readonly DispatcherTimer slideshow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private readonly Stopwatch startup = Stopwatch.StartNew();
        internal double FirstImageMilliseconds;
        internal string CurrentPath { get { return index >= 0 && index < files.Count ? files[index] : null; } }
        internal bool IsLoading { get; private set; }
        internal string LastError { get; private set; }
        internal long CacheBytes { get { return loader.CacheBytes; } }

        public ViewerWindow(string[] args)
        {
            Title = "Quiet Photos"; Width = 1180; Height = 790; MinWidth = 760; MinHeight = 480;
            using (var icon = typeof(ViewerWindow).Assembly.GetManifestResourceStream("QuietPhotos.Icon.ico"))
                Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("#151618"); Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI"); UseLayoutRounding = true; AllowDrop = true;
            BuildInterface();
            SourceInitialized += delegate { int dark = 1; try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); } catch (DllNotFoundException) { } };
            PreviewKeyDown += HandleKey;
            DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += async delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null && paths.Length > 0) await OpenPathAsync(paths[0]); };
            Surface.Changed += UpdateZoom;
            Surface.RequestFullResolution += async delegate { await EnsureFullAsync(); };
            slideshow.Tick += async delegate { if (IsLoading) return; if (index >= files.Count - 1) { StopSlideshow(); Toast("End of folder"); } else await NavigateAsync(1); };
            toastTimer.Tick += delegate { toastTimer.Stop(); RefreshStatus(); };
            Closed += delegate { disposed = true; imageCancel.Cancel(); thumbCancel.Cancel(); slideshow.Stop(); toastTimer.Stop(); };
            Loaded += async delegate
            {
                string path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
                if (!String.IsNullOrEmpty(path)) await OpenPathAsync(path);
                Surface.Focus();
            };
        }

        private static SolidColorBrush Brush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        private static TextBlock Text(string text, double size) { return new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center }; }
        private Button Button(string icon, string label, string hint, Action click, bool needsImage)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (!String.IsNullOrEmpty(icon)) content.Children.Add(new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            if (!String.IsNullOrEmpty(label)) content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(String.IsNullOrEmpty(icon) ? 0 : 8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = content, ToolTip = hint };
            AutomationProperties.SetName(b, hint);
            b.Click += delegate { click(); };
            if (needsImage) { imageButtons.Add(b); b.IsEnabled = false; }
            return b;
        }
        private static Border Divider() { return new Border { Width = 1, Height = 22, Background = Brush("#3A3B40"), Margin = new Thickness(9, 0, 9, 0) }; }
        private void BuildInterface()
        {
            root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var header = new Grid { Background = Brush("#202124"), Height = 64 };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameBlock = new StackPanel { Margin = new Thickness(22, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            filename.TextTrimming = TextTrimming.CharacterEllipsis; filename.FontWeight = FontWeights.SemiBold;
            subtitle.Foreground = Brush("#A4A6AE"); subtitle.Margin = new Thickness(0, 4, 0, 0); subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            nameBlock.Children.Add(filename); nameBlock.Children.Add(subtitle); header.Children.Add(nameBlock);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Button("\uE8E5", "Open", "Open image (Ctrl+O)", OpenDialog, false)); actions.Children.Add(Divider());
            actions.Children.Add(Button("\uE7AD", "", "Rotate clockwise (Ctrl+R)", delegate { Surface.Rotate(90); }, true));
            actions.Children.Add(Button("\uE8C8", "", "Copy image (Ctrl+C)", async delegate { await CopyAsync(); }, true));
            saveCopyButton = Button("\uE792", "", "Save a copy (Ctrl+Shift+S)", async delegate { await SaveCopyAsync(); }, true); actions.Children.Add(saveCopyButton);
            infoButton = Button("\uE946", "", "File information (I)", ToggleInfo, true); actions.Children.Add(infoButton);
            actions.Children.Add(Button("\uE740", "", "Fullscreen (F11)", ToggleFullscreen, false));
            var more = Button("\uE712", "", "More options", delegate { }, false);
            var menu = new ContextMenu();
            AddMenu(menu, "Open folder…", "Ctrl+Shift+O", OpenFolder);
            AddMenu(menu, "Show in File Explorer", "Ctrl+E", Reveal);
            AddMenu(menu, "Copy file path", "Ctrl+Shift+C", CopyPath);
            AddMenu(menu, "Open original with editor…", "Ctrl+Shift+E", OpenWithEditor);
            menu.Items.Add(new Separator());
            AddMenu(menu, "Toggle filmstrip", "T", ToggleStrip);
            AddMenu(menu, "Slideshow · 4 seconds", "F5", ToggleSlideshow);
            AddMenu(menu, "Rotate counterclockwise", "Ctrl+Shift+R", delegate { Surface.Rotate(-90); });
            backdropMenuItem = AddMenu(menu, "Transparency background · Dark", "B", CycleBackdrop);
            backdropMenuItem.ToolTip = "Cycles Dark, Checkerboard and Light behind transparent image pixels. Opaque photos and the surrounding canvas stay unchanged.";
            AddMenu(menu, "Fit to width", "W", delegate { Surface.FitWidth(); });
            AddMenu(menu, "Sharp pixels / smooth scaling", "P", delegate { Surface.ToggleSharpPixels(); Toast("Image interpolation changed"); });
            AddMenu(menu, "Previous page / frame", "[", async delegate { await ShowFrameAsync(-1); });
            AddMenu(menu, "Next page / frame", "]", async delegate { await ShowFrameAsync(1); });
            menu.Items.Add(new Separator()); AddMenu(menu, "Keyboard shortcuts", "F1", Help);
            more.ContextMenu = menu; more.Click += delegate { menu.PlacementTarget = more; menu.IsOpen = true; };
            actions.Children.Add(more); Grid.SetColumn(actions, 1); header.Children.Add(actions); root.Children.Add(header); toolbar = header;

            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(content, 1); root.Children.Add(content);
            var stage = new Grid { ClipToBounds = true }; stage.Children.Add(Surface); content.Children.Add(stage);
            var pageBar = new StackPanel { Orientation = Orientation.Horizontal };
            previousPage = Button("\uE76B", "", "Previous page or frame ([)", async delegate { await ShowFrameAsync(-1); }, false);
            nextPage = Button("\uE76C", "", "Next page or frame (])", async delegate { await ShowFrameAsync(1); }, false);
            pageLabel.Margin = new Thickness(10, 0, 10, 0); pageBar.Children.Add(previousPage); pageBar.Children.Add(pageLabel); pageBar.Children.Add(nextPage);
            pageControls = new Border { Child = pageBar, Background = Brush("#292A30"), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            empty = BuildEmpty(); stage.Children.Add(empty);
            loading.Foreground = Brush("#ABB0BC"); loading.Margin = new Thickness(20); loading.HorizontalAlignment = HorizontalAlignment.Right; loading.VerticalAlignment = VerticalAlignment.Top; loading.IsHitTestVisible = false; stage.Children.Add(loading);
            errorPanel = new Border { Background = Brush("#25262C"), Padding = new Thickness(28), CornerRadius = new CornerRadius(12), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var errorContent = new StackPanel(); errorContent.Children.Add(Text("This image couldn’t be opened", 20)); messageText.TextWrapping = TextWrapping.Wrap; messageText.Foreground = Brush("#BDC0CA"); messageText.Margin = new Thickness(0, 12, 0, 20); errorContent.Children.Add(messageText);
            errorContent.Children.Add(Button("\uE8E5", "Open another image", "Open another image", OpenDialog, false)); errorPanel.Child = errorContent; stage.Children.Add(errorPanel);
            prev = Button("\uE76B", "", "Previous image (Left arrow)", async delegate { await NavigateAsync(-1); }, false);
            next = Button("\uE76C", "", "Next image (Right arrow)", async delegate { await NavigateAsync(1); }, false);
            prev.HorizontalAlignment = HorizontalAlignment.Left; next.HorizontalAlignment = HorizontalAlignment.Right; prev.VerticalAlignment = next.VerticalAlignment = VerticalAlignment.Center;
            prev.Margin = next.Margin = new Thickness(12); prev.Background = next.Background = Brush("#292A30"); prev.Height = next.Height = 48;
            prev.Visibility = next.Visibility = Visibility.Collapsed; stage.Children.Add(prev); stage.Children.Add(next);
            stage.Children.Add(pageControls);
            infoPanel = new Border { Width = 278, Background = Brush("#202124"), BorderBrush = Brush("#33353B"), BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed, Padding = new Thickness(22) }; Grid.SetColumn(infoPanel, 1); content.Children.Add(infoPanel);
            var info = new StackPanel(); var infoTop = new DockPanel(); var closeInfo = Button("\uE8BB", "", "Close information", ToggleInfo, false); DockPanel.SetDock(closeInfo, Dock.Right); infoTop.Children.Add(closeInfo); infoTop.Children.Add(Text("File information", 15)); info.Children.Add(infoTop);
            detailsName.Margin = new Thickness(0, 24, 0, 20); detailsName.TextWrapping = TextWrapping.Wrap; info.Children.Add(detailsName);
            detailsText.TextWrapping = TextWrapping.Wrap; detailsText.Foreground = Brush("#B7BAC4"); detailsText.LineHeight = 21; info.Children.Add(detailsText);
            info.Children.Add(Button("\uE838", "Show in folder", "Show in File Explorer", Reveal, true)); infoPanel.Child = new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            filmstrip = new Border { Height = 94, Background = Brush("#1B1C1F"), BorderBrush = Brush("#2B2D32"), BorderThickness = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed };
            filmstrip.Child = new ScrollViewer { Content = thumbnails, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Center }; Grid.SetRow(filmstrip, 2); root.Children.Add(filmstrip);

            var footer = new Grid { Height = 46, Background = Brush("#202124") }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status.Foreground = Brush("#9DA0AA"); status.Margin = new Thickness(22, 0, 10, 0); status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status);
            position.Foreground = Brush("#9DA0AA"); position.Margin = new Thickness(10, 0, 18, 0); Grid.SetColumn(position, 1); footer.Children.Add(position);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            stripButton = Button("\uE8A9", "", "Filmstrip (T)", ToggleStrip, true); controls.Children.Add(stripButton);
            slideshowButton = Button("\uE768", "", "Slideshow (F5)", ToggleSlideshow, true); controls.Children.Add(slideshowButton); controls.Children.Add(Divider());
            controls.Children.Add(Button("\uE71F", "", "Zoom out (-)", delegate { Surface.ZoomBy(1 / 1.2); }, true));
            zoomLabel.MinWidth = 46; zoomLabel.TextAlignment = TextAlignment.Center; controls.Children.Add(zoomLabel);
            controls.Children.Add(Button("\uE8A3", "", "Zoom in (+)", delegate { Surface.ZoomBy(1.2); }, true));
            controls.Children.Add(Button("", "1:1", "Actual pixels (1)", delegate { Surface.Actual(); }, true));
            controls.Children.Add(Button("\uE9A6", "", "Fit to window (0)", delegate { Surface.Fit(); }, true));
            Grid.SetColumn(controls, 2); footer.Children.Add(controls); Grid.SetRow(footer, 3); root.Children.Add(footer); statusbar = footer;
        }
        private Border BuildEmpty()
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30) };
            var mark = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 0, 0, 22) };
            using (var artwork = typeof(ViewerWindow).Assembly.GetManifestResourceStream("QuietPhotos.Icon.xaml"))
                mark.Source = (DrawingImage)System.Windows.Markup.XamlReader.Load(artwork);
            panel.Children.Add(mark);
            var heading = Text("Just the photo.", 36); heading.FontWeight = FontWeights.Light; heading.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(heading);
            var desc = Text("Drop an image or a folder here to get started.", 15); desc.Foreground = Brush("#A5A8B2"); desc.Margin = new Thickness(0, 14, 0, 26); panel.Children.Add(desc);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var open = Button("\uE8E5", "Open image", "Open image (Ctrl+O)", OpenDialog, false); open.Background = Brush("#30435C"); open.Padding = new Thickness(20, 12, 20, 12); buttons.Children.Add(open);
            buttons.Children.Add(Button("\uE838", "Open folder", "Open folder (Ctrl+Shift+O)", OpenFolder, false)); panel.Children.Add(buttons);
            var keys = Text("← →  Browse     ·     Scroll  Zoom     ·     F11  Fullscreen", 12); keys.Foreground = Brush("#6F7480"); keys.Margin = new Thickness(0, 36, 0, 0); keys.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(keys);
            return new Border { Child = panel, Background = Brush("#151618") };
        }
        private static MenuItem AddMenu(ContextMenu menu, string title, string shortcut, Action action)
        { var item = new MenuItem { Header = title, InputGestureText = shortcut }; item.Click += delegate { action(); }; menu.Items.Add(item); return item; }

        private void CycleBackdrop()
        {
            Surface.CycleBackdrop();
            string name = new[] { "Dark", "Checkerboard", "Light" }[Surface.BackdropMode];
            backdropMenuItem.Header = "Transparency background · " + name;
            Toast("Transparency background: " + name + " · visible through transparent pixels");
        }

        private async void OpenDialog()
        {
            var dialog = new OpenFileDialog { Title = "Open image or project preview", Filter = ImageLoader.OpenFilter };
            if (dialog.ShowDialog(this) == true) await OpenPathAsync(dialog.FileName);
        }
        private async void OpenFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder of images", ShowNewFolderButton = false })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) await OpenPathAsync(dialog.SelectedPath);
        }
        public async Task OpenPathAsync(string path)
        {
            StopSlideshow(); int version = ++folderVersion;
            try
            {
                path = Path.GetFullPath(path);
                if (Directory.Exists(path))
                {
                    loading.Text = "Opening folder…";
                    var found = await Task.Run(delegate { return FindImages(path); });
                    if (disposed || version != folderVersion) return;
                    loading.Text = "";
                    if (found.Count == 0) { Toast("No supported images in this folder"); return; }
                    files = found; index = 0; await ShowCurrentAsync();
                }
                else
                {
                    if (!File.Exists(path)) throw new FileNotFoundException("The file could not be found.", path);
                    files = new List<string> { path }; index = 0;
                    // The requested file displays before the directory scan completes.
                    var scan = Task.Run(delegate { return FindImages(Path.GetDirectoryName(path)); });
                    await ShowCurrentAsync();
                    var found = await scan;
                    if (disposed || version != folderVersion) return;
                    if (!found.Contains(path, StringComparer.OrdinalIgnoreCase)) found.Add(path);
                    found.Sort(new NaturalOrder()); files = found; index = files.FindIndex(p => String.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    UpdateNavigation(); BuildThumbnails();
                }
            }
            catch (Exception e) { if (!disposed && version == folderVersion) { loading.Text = ""; Toast("Could not open: " + e.Message); } }
        }
        private static List<string> FindImages(string folder)
        { var list = Directory.EnumerateFiles(folder).Where(p => ImageLoader.Supported(p) && !Path.GetExtension(p).Equals(".xmp", StringComparison.OrdinalIgnoreCase)).ToList(); list.Sort(new NaturalOrder()); return list; }
        internal async Task NavigateAsync(int delta)
        {
            if (index < 0 || index + delta < 0 || index + delta >= files.Count) return;
            index += delta; await ShowCurrentAsync();
        }
        private async Task ShowCurrentAsync(bool resetFrame = true)
        {
            if (resetFrame) currentFrame = 0;
            imageCancel.Cancel(); imageCancel = new CancellationTokenSource(); var token = imageCancel.Token;
            int version = ++loadVersion; fullLoading = false; LastError = null; IsLoading = true;
            string path = CurrentPath; Surface.SetImage(null, true); errorPanel.Visibility = Visibility.Collapsed; empty.Visibility = Visibility.Collapsed;
            filename.Text = Path.GetFileName(path); filename.ToolTip = path; subtitle.Text = Path.GetDirectoryName(path); Title = filename.Text + " — Quiet Photos";
            detailsName.Text = filename.Text; detailsText.Text = "Loading…"; loading.Text = "Opening…";
            foreach (var b in imageButtons) b.IsEnabled = false;
            pageControls.Visibility = Visibility.Collapsed;
            UpdateNavigation(); thumbCancel.Cancel(); thumbnails.Children.Clear();
            try
            {
                var data = await loader.LoadAsync(path, false, token, currentFrame);
                if (disposed || version != loadVersion) return;
                Surface.SetImage(data, true); detailsText.Text = data.Details + "\n\nLOCATION\n" + path;
                foreach (var b in imageButtons) b.IsEnabled = true;
                saveCopyButton.IsEnabled = !data.PreviewOnly;
                UpdatePages();
                if (FirstImageMilliseconds == 0) FirstImageMilliseconds = startup.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (disposed || version != loadVersion) return;
                LastError = e.Message; messageText.Text = "The file may be damaged, unavailable, or need an image codec.\n\n" + e.Message;
                errorPanel.Visibility = Visibility.Visible; detailsText.Text = path; StopSlideshow();
            }
            finally { if (!disposed && version == loadVersion) { IsLoading = false; loading.Text = ""; RefreshStatus(); BuildThumbnails(); } }
        }
        internal async Task ShowFrameAsync(int delta)
        {
            if (Surface.Data == null || IsLoading) return;
            int requested = currentFrame + delta;
            if (requested < 0 || requested >= Surface.Data.FrameCount) return;
            StopSlideshow(); currentFrame = requested; await ShowCurrentAsync(false);
        }
        private void UpdatePages()
        {
            var data = Surface.Data;
            pageControls.Visibility = data != null && data.FrameCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (data == null) return;
            pageLabel.Text = "Page / frame " + (data.FrameIndex + 1) + " / " + data.FrameCount;
            previousPage.IsEnabled = data.FrameIndex > 0; nextPage.IsEnabled = data.FrameIndex + 1 < data.FrameCount;
        }
        private async Task EnsureFullAsync()
        {
            if (fullLoading || Surface.Data == null || Surface.Data.FullResolution) return;
            int version = loadVersion; var token = imageCancel.Token; string path = CurrentPath;
            fullLoading = true; loading.Text = "Loading full detail…";
            try { var data = await loader.LoadAsync(path, true, token, currentFrame); if (!disposed && version == loadVersion) Surface.SetImage(data, false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!disposed && version == loadVersion) Toast(e.Message); }
            finally { if (!disposed && version == loadVersion) { fullLoading = false; loading.Text = ""; } }
        }
        private void UpdateNavigation()
        {
            prev.Visibility = next.Visibility = files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            prev.IsEnabled = index > 0; next.IsEnabled = index >= 0 && index < files.Count - 1;
            position.Text = index < 0 ? "" : (index + 1) + " / " + files.Count;
            filmstrip.Visibility = stripVisible && index >= 0 && !fullscreen ? Visibility.Visible : Visibility.Collapsed;
        }
        private async void BuildThumbnails()
        {
            thumbCancel.Cancel(); thumbCancel = new CancellationTokenSource(); var token = thumbCancel.Token; int version = ++thumbVersion;
            thumbnails.Children.Clear(); if (!stripVisible || index < 0 || fullscreen) return;
            // Let the foreground image paint before starting background codec work.
            try { await Task.Delay(120, token); } catch (OperationCanceledException) { return; }
            if (disposed || version != thumbVersion) return;
            int start = Math.Max(0, Math.Min(index - 5, files.Count - 11)), end = Math.Min(files.Count, start + 11);
            var entries = new List<Tuple<string, Image, TextBlock>>();
            for (int n = start; n < end; n++)
            {
                int target = n;
                var holder = new Grid { Width = 64, Height = 54 };
                var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                var placeholder = Text("·", 20); placeholder.HorizontalAlignment = HorizontalAlignment.Center; placeholder.Foreground = Brush("#666B78");
                holder.Children.Add(placeholder); holder.Children.Add(image);
                var b = new Button { Content = holder, Padding = new Thickness(3), Margin = new Thickness(3, 8, 3, 8), BorderBrush = n == index ? Brush("#9BC5FF") : Brushes.Transparent, Background = n == index ? Brush("#303B4B") : Brushes.Transparent, ToolTip = Path.GetFileName(files[n]) };
                AutomationProperties.SetName(b, "View " + Path.GetFileName(files[n]));
                b.Click += async delegate { StopSlideshow(); if (target < files.Count) { index = target; await ShowCurrentAsync(); } };
                thumbnails.Children.Add(b); entries.Add(Tuple.Create(files[n], image, placeholder));
            }
            foreach (var entry in entries.OrderBy(e => String.Equals(e.Item1, CurrentPath, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                try { var bitmap = await loader.ThumbnailAsync(entry.Item1, token); if (disposed || version != thumbVersion) return; entry.Item2.Source = bitmap; entry.Item3.Visibility = Visibility.Collapsed; }
                catch (OperationCanceledException) { return; }
                catch (Exception) { if (!disposed && version == thumbVersion) entry.Item3.Text = "?"; }
            }
        }
        private void UpdateZoom() { zoomLabel.Text = Surface.Data == null ? "—" : (Surface.Zoom * Surface.PixelRatio * 100).ToString("0") + "%"; RefreshStatus(); }
        private void RefreshStatus()
        {
            if (toastTimer.IsEnabled) return;
            if (Surface.Data == null) { status.Text = index < 0 ? "LOCAL FILES. JUST OPEN AND VIEW." : "Image unavailable"; return; }
            var data = Surface.Data;
            status.Text = data.Width + " × " + data.Height + "   ·   " + Path.GetExtension(data.Path).TrimStart('.').ToUpperInvariant() + (data.PreviewOnly ? "   ·   Read-only project preview" : "") + (Surface.IsFit ? "   ·   Fit" : "") + (slideshow.IsEnabled ? "   ·   Slideshow" : "");
        }
        private void Toast(string message) { status.Text = message; toastTimer.Stop(); toastTimer.Start(); }
        private void ToggleInfo() { infoPanel.Visibility = infoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; infoButton.Background = infoPanel.Visibility == Visibility.Visible ? Brush("#35445B") : Brushes.Transparent; }
        private void ToggleStrip() { stripVisible = !stripVisible; UpdateNavigation(); BuildThumbnails(); }
        private void ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            if (fullscreen) { beforeFull = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
            else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = beforeFull; }
            toolbar.Visibility = statusbar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
            if (fullscreen) infoPanel.Visibility = Visibility.Collapsed;
            UpdateNavigation(); BuildThumbnails();
        }
        private void ToggleSlideshow() { if (slideshow.IsEnabled) StopSlideshow(); else if (files.Count > 1) { slideshow.Start(); slideshowButton.Background = Brush("#35445B"); RefreshStatus(); } }
        private void StopSlideshow() { slideshow.Stop(); if (slideshowButton != null) slideshowButton.Background = Brushes.Transparent; RefreshStatus(); }
        private void Reveal()
        {
            if (CurrentPath == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + CurrentPath + "\"") { UseShellExecute = true }); } catch (Exception e) { Toast(e.Message); }
        }
        private void CopyPath() { if (CurrentPath == null) return; try { Clipboard.SetText(CurrentPath); Toast("File path copied"); } catch (Exception e) { Toast(e.Message); } }
        private void OpenWithEditor()
        {
            if (CurrentPath == null) return;
            try { var info = new OpenAsInfo { File = CurrentPath, Flags = 4 }; int result = SHOpenWithDialog(new WindowInteropHelper(this).Handle, ref info); if (result < 0 && result != unchecked((int)0x800704C7)) Marshal.ThrowExceptionForHR(result); }
            catch (Exception e) { Toast("Could not open editor chooser: " + e.Message); }
        }
        private async Task CopyAsync()
        {
            if (Surface.Data == null || exporting) return;
            exporting = true; string path = CurrentPath; int rotation = Surface.Rotation, frame = currentFrame;
            try { var data = await loader.LoadAsync(path, true, CancellationToken.None, frame); if (disposed) return; Clipboard.SetImage(Rotated(data.Bitmap, rotation)); Toast("Image copied"); }
            catch (Exception e) { Toast("Could not copy: " + e.Message); }
            finally { exporting = false; }
        }
        private static BitmapSource Rotated(BitmapSource image, int rotation)
        { if (rotation == 0) return image; var result = new TransformedBitmap(image, new RotateTransform(rotation)); result.Freeze(); return result; }
        private async Task SaveCopyAsync()
        {
            if (Surface.Data == null || exporting) return;
            if (Surface.Data.PreviewOnly) { Toast("This is a read-only project preview. Use Open original with editor to edit it."); return; }
            string source = CurrentPath; int rotation = Surface.Rotation;
            int frame = currentFrame;
            var dialog = new SaveFileDialog { Title = "Save a copy", FileName = Path.GetFileNameWithoutExtension(source) + " - copy.png", Filter = "PNG image|*.png|JPEG image|*.jpg|TIFF image|*.tif|Bitmap image|*.bmp", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            if (String.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) { Toast("Choose a different name to preserve the original"); return; }
            exporting = true; Toast("Saving full-resolution copy…");
            try
            {
                var data = await loader.LoadAsync(source, true, CancellationToken.None, frame);
                await Task.Run(delegate { Export(data.Bitmap, rotation, dialog.FileName); });
                if (!disposed) Toast("Copy saved: " + Path.GetFileName(dialog.FileName));
            }
            catch (Exception e) { if (!disposed) Toast("Could not save: " + e.Message); }
            finally { exporting = false; }
        }
        public static void Export(BitmapSource bitmap, int rotation, string path)
        {
            BitmapEncoder encoder; string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext) { case ".jpg": case ".jpeg": encoder = new JpegBitmapEncoder { QualityLevel = 95 }; break; case ".tif": case ".tiff": encoder = new TiffBitmapEncoder(); break; case ".bmp": encoder = new BmpBitmapEncoder(); break; default: encoder = new PngBitmapEncoder(); break; }
            encoder.Frames.Add(BitmapFrame.Create(Rotated(bitmap, rotation)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write)) encoder.Save(file);
                // Same-directory rename commits the completed copy without rewriting destination ACLs.
                if (!MoveFileEx(temp, path, 1 | 8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private async void HandleKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            e.Handled = true;
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.O: if (shift) OpenFolder(); else OpenDialog(); break;
                    case Key.R: Surface.Rotate(shift ? -90 : 90); break;
                    case Key.C: if (shift) CopyPath(); else await CopyAsync(); break;
                    case Key.S: if (shift) await SaveCopyAsync(); else e.Handled = false; break;
                    case Key.E: if (shift) OpenWithEditor(); else Reveal(); break;
                    default: e.Handled = false; break;
                }
                return;
            }
            switch (e.Key)
            {
                case Key.Left: case Key.PageUp: StopSlideshow(); await NavigateAsync(-1); break;
                case Key.Right: case Key.PageDown: StopSlideshow(); await NavigateAsync(1); break;
                case Key.Home: StopSlideshow(); if (files.Count > 0) { index = 0; await ShowCurrentAsync(); } break;
                case Key.End: StopSlideshow(); if (files.Count > 0) { index = files.Count - 1; await ShowCurrentAsync(); } break;
                case Key.D0: case Key.NumPad0: Surface.Fit(); break;
                case Key.D1: case Key.NumPad1: Surface.Actual(); break;
                case Key.OemPlus: case Key.Add: Surface.ZoomBy(1.2); break;
                case Key.OemMinus: case Key.Subtract: Surface.ZoomBy(1 / 1.2); break;
                case Key.I: ToggleInfo(); break;
                case Key.T: ToggleStrip(); break;
                case Key.B: CycleBackdrop(); return;
                case Key.W: Surface.FitWidth(); break;
                case Key.P: Surface.ToggleSharpPixels(); break;
                case Key.OemOpenBrackets: await ShowFrameAsync(-1); break;
                case Key.OemCloseBrackets: await ShowFrameAsync(1); break;
                case Key.F11: ToggleFullscreen(); break;
                case Key.F5: ToggleSlideshow(); break;
                case Key.Escape: if (slideshow.IsEnabled) StopSlideshow(); else if (fullscreen) ToggleFullscreen(); else if (infoPanel.Visibility == Visibility.Visible) ToggleInfo(); break;
                case Key.F1: Help(); break;
                default: e.Handled = false; break;
            }
            RefreshStatus();
        }
        private void Help()
        {
            MessageBox.Show(this, "Ctrl+O      Open image\nCtrl+Shift+O      Open folder\nLeft / Right      Previous / next\nHome / End      First / last\nMouse wheel / + / −      Zoom\nDrag      Pan image\nDouble-click      Fit / actual pixels\n0 / 1      Fit / actual pixels\nCtrl+R / Ctrl+Shift+R      Rotate\nCtrl+C      Copy full-resolution image\nCtrl+Shift+C      Copy path\nCtrl+Shift+S      Save rotated copy\nCtrl+E      Show in folder\nCtrl+Shift+E      Open original with editor\nW      Fit to width\nP      Toggle sharp pixels\n[ / ]      Previous / next page or frame\nI / T      Information / filmstrip\nB      Transparency background: dark / checker / light\nF5      Slideshow (4 seconds)\nF11      Fullscreen\nEsc      Stop slideshow / exit fullscreen / close info\n\nExtra formats use the bundled portable decoder.\nPage/frame selection is manual; animations do not play.\nProjects and RAW files are read-only previews.", "Quiet Photos · Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
