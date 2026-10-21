using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QuietPhotos
{
    public sealed class ImageSurface : FrameworkElement
    {
        public ImageData Data;
        public double Zoom = 1;
        public int Rotation;
        public bool IsFit = true;
        public int BackdropMode;
        private bool fitWidth;
        private Vector pan;
        private Point drag;
        private bool dragging;
        public event Action Changed;
        public event Action RequestFullResolution;
        public double PixelRatio { get { var s = PresentationSource.FromVisual(this); return s == null ? 1 : s.CompositionTarget.TransformToDevice.M11; } }
        public ImageSurface()
        {
            ClipToBounds = true; Focusable = true; Cursor = Cursors.Arrow;
            SizeChanged += delegate { if (IsFit) { if (fitWidth) FitWidth(); else Fit(); } else InvalidateVisual(); };
            MouseWheel += delegate(object sender, MouseWheelEventArgs e) { if (Data == null) return; SetZoom(Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), e.GetPosition(this)); e.Handled = true; };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Focus(); if (Data == null) return;
                if (e.ClickCount == 2) { if (IsFit) Actual(); else Fit(); return; }
                drag = e.GetPosition(this); dragging = true; CaptureMouse(); Cursor = Cursors.SizeAll;
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            { if (dragging) { var p = e.GetPosition(this); pan += p - drag; drag = p; InvalidateVisual(); } };
            MouseLeftButtonUp += delegate { dragging = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow; };
            LostMouseCapture += delegate { dragging = false; Cursor = Cursors.Arrow; };
        }
        public void SetImage(ImageData data, bool reset)
        { Data = data; if (reset) { Rotation = 0; Fit(); } else { InvalidateVisual(); Notify(); } }
        public void Fit()
        {
            fitWidth = false;
            IsFit = true; pan = new Vector();
            if (Data != null) { bool swap = Rotation % 180 != 0; double w = swap ? Data.Height : Data.Width, h = swap ? Data.Width : Data.Height;
                Zoom = Math.Max(0.00001, Math.Min(1 / PixelRatio, Math.Min(Math.Max(1, ActualWidth - 64) / w, Math.Max(1, ActualHeight - 64) / h))); }
            InvalidateVisual(); Notify();
        }
        public void Actual() { if (Data == null) return; SetZoom(1 / PixelRatio, new Point(ActualWidth / 2, ActualHeight / 2)); }
        public void FitWidth()
        {
            if (Data == null) return;
            fitWidth = true; IsFit = true; pan = new Vector();
            Zoom = Math.Max(0.00001, Math.Max(1, ActualWidth - 64) / (Rotation % 180 != 0 ? Data.Height : Data.Width));
            InvalidateVisual(); Notify();
            if (!Data.FullResolution && RequestFullResolution != null) RequestFullResolution();
        }
        public void ToggleSharpPixels()
        { RenderOptions.SetBitmapScalingMode(this, RenderOptions.GetBitmapScalingMode(this) == BitmapScalingMode.NearestNeighbor ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor); InvalidateVisual(); }
        public void ZoomBy(double multiplier) { SetZoom(Zoom * multiplier, new Point(ActualWidth / 2, ActualHeight / 2)); }
        public void SetZoom(double value, Point anchor)
        {
            if (Data == null) return;
            value = Math.Max(0.00001, Math.Min(32 / PixelRatio, value));
            Vector relative = anchor - new Point(ActualWidth / 2, ActualHeight / 2);
            pan = relative - (relative - pan) * (value / Zoom);
            Zoom = value; IsFit = false; InvalidateVisual(); Notify();
            if (!Data.FullResolution && Zoom * Math.Max(Data.Width, Data.Height) > Math.Max(Data.Bitmap.PixelWidth, Data.Bitmap.PixelHeight) * 1.1 && RequestFullResolution != null) RequestFullResolution();
        }
        public void Rotate(int direction) { if (Data == null) return; Rotation = (Rotation + direction + 360) % 360; Fit(); }
        public void CycleBackdrop() { BackdropMode = (BackdropMode + 1) % 3; InvalidateVisual(); }
        private void Notify() { if (Changed != null) Changed(); }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(21, 22, 24)), null, new Rect(RenderSize));
            if (Data == null) return;
            dc.PushTransform(new TranslateTransform(ActualWidth / 2 + pan.X, ActualHeight / 2 + pan.Y));
            dc.PushTransform(new ScaleTransform(Zoom, Zoom)); dc.PushTransform(new RotateTransform(Rotation));
            var imageRect = new Rect(-Data.Width / 2.0, -Data.Height / 2.0, Data.Width, Data.Height);
            if (BackdropMode == 1)
            {
                var tile = new DrawingGroup();
                using (var t = tile.Open())
                {
                    t.DrawRectangle(new SolidColorBrush(Color.FromRgb(72, 74, 79)), null, new Rect(0, 0, 2, 2));
                    var alternate = new SolidColorBrush(Color.FromRgb(95, 97, 103));
                    t.DrawRectangle(alternate, null, new Rect(0, 0, 1, 1)); t.DrawRectangle(alternate, null, new Rect(1, 1, 1, 1));
                }
                var brush = new DrawingBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 20 / Zoom, 20 / Zoom) }; brush.Freeze();
                dc.DrawRectangle(brush, null, imageRect);
            }
            else if (BackdropMode == 2) dc.DrawRectangle(Brushes.WhiteSmoke, null, imageRect);
            dc.DrawImage(Data.Bitmap, imageRect);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
