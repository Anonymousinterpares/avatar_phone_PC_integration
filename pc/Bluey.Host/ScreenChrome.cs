using System.Runtime.InteropServices;
using System.Windows.Interop;
using Media = System.Windows.Media;
using WpfControls = System.Windows.Controls;
using WpfShapes = System.Windows.Shapes;

namespace Bluey.Host;

/// <summary>Keeps a popup fully inside the monitor that holds the blueberry.</summary>
public static class ScreenPlace
{
    public static void Show(System.Windows.Window window, System.Windows.Rect host, System.Windows.DpiScale dpi)
    {
        var size = Measure(window);
        var spot = Beside(host, size.Width, size.Height, dpi);
        window.Left = spot.X;
        window.Top = spot.Y;
        window.Show();
        window.UpdateLayout();
        var w = window.ActualWidth > 1 ? window.ActualWidth : size.Width;
        var h = window.ActualHeight > 1 ? window.ActualHeight : size.Height;
        var fitted = Beside(host, w, h, dpi);
        window.Left = fitted.X;
        window.Top = fitted.Y;
    }

    public static System.Windows.Point Beside(System.Windows.Rect host, double popW, double popH, System.Windows.DpiScale dpi)
    {
        var work = WorkArea(host.Left + host.Width / 2, host.Top + host.Height / 2, dpi);
        const double gap = 10;
        const double margin = 8;
        var rightSide = host.Right + gap;
        var leftSide = host.Left - gap - popW;
        double x;
        if (rightSide + popW <= work.Right - margin)
            x = rightSide;
        else if (leftSide >= work.Left + margin)
            x = leftSide;
        else
        {
            x = work.Right - margin - popW;
            if (x < work.Left + margin) x = work.Left + margin;
        }
        double y = host.Top;
        if (y + popH > work.Bottom - margin) y = work.Bottom - margin - popH;
        if (y < work.Top + margin) y = work.Top + margin;
        return new System.Windows.Point(x, y);
    }

    public static System.Windows.Rect WorkArea(double dipX, double dipY, System.Windows.DpiScale dpi)
    {
        var scaleX = dpi.DpiScaleX < 0.5 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY < 0.5 ? 1 : dpi.DpiScaleY;
        var px = (int)Math.Round(dipX * scaleX);
        var py = (int)Math.Round(dipY * scaleY);
        var bounds = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(px, py)).WorkingArea;
        return new System.Windows.Rect(
            bounds.Left / scaleX,
            bounds.Top / scaleY,
            bounds.Width / scaleX,
            bounds.Height / scaleY);
    }

    private static System.Windows.Size Measure(System.Windows.Window window)
    {
        var sizeTo = window.SizeToContent;
        double w = sizeTo is System.Windows.SizeToContent.Width or System.Windows.SizeToContent.WidthAndHeight
            || double.IsNaN(window.Width) || window.Width <= 0
            ? double.NaN
            : window.Width;
        double h = sizeTo is System.Windows.SizeToContent.Height or System.Windows.SizeToContent.WidthAndHeight
            || double.IsNaN(window.Height) || window.Height <= 0
            ? double.NaN
            : window.Height;
        if (window.Content is System.Windows.FrameworkElement root)
        {
            root.Measure(new System.Windows.Size(double.IsNaN(w) ? double.PositiveInfinity : w, double.PositiveInfinity));
            if (double.IsNaN(w)) w = Math.Max(1, root.DesiredSize.Width);
            if (double.IsNaN(h)) h = Math.Max(1, root.DesiredSize.Height);
        }
        if (double.IsNaN(w) || w < 1) w = 160;
        if (double.IsNaN(h) || h < 1) h = 48;
        if (window.MinWidth > 0 && w < window.MinWidth) w = window.MinWidth;
        if (window.MinHeight > 0 && h < window.MinHeight) h = window.MinHeight;
        return new System.Windows.Size(w, h);
    }
}

/// <summary>Comic-book dialogue cloud. The tail points at the blueberry and the whole cloud stays on screen.</summary>
public sealed class BerryComicBubble : System.Windows.Window
{
    private readonly WpfControls.TextBlock _text;
    private readonly WpfShapes.Path _cloud;
    private readonly WpfControls.Canvas _canvas;

    public BerryComicBubble()
    {
        WindowStyle = System.Windows.WindowStyle.None;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        _text = new WpfControls.TextBlock
        {
            Foreground = Media.Brushes.Black,
            FontFamily = new Media.FontFamily("Comic Sans MS"),
            FontSize = 16,
            FontWeight = System.Windows.FontWeights.Bold,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
            MaxWidth = 240,
            IsHitTestVisible = false,
        };
        _cloud = new WpfShapes.Path
        {
            Fill = new Media.SolidColorBrush(Media.Color.FromRgb(255, 252, 240)),
            Stroke = Media.Brushes.Black,
            StrokeThickness = 3,
            StrokeLineJoin = Media.PenLineJoin.Round,
            StrokeStartLineCap = Media.PenLineCap.Round,
            StrokeEndLineCap = Media.PenLineCap.Round,
            IsHitTestVisible = false,
        };
        _canvas = new WpfControls.Canvas { IsHitTestVisible = false };
        _canvas.Children.Add(_cloud);
        _canvas.Children.Add(_text);
        Content = _canvas;
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    public void Place(string text, double anchorX, double anchorY, System.Windows.DpiScale dpi)
    {
        _text.Width = double.NaN;
        _text.Text = text;
        _text.Measure(new System.Windows.Size(240, double.PositiveInfinity));
        var textW = Math.Clamp(_text.DesiredSize.Width, 72, 240);
        var textH = Math.Max(22, _text.DesiredSize.Height);
        _text.Width = textW;
        const double padX = 18;
        const double padY = 14;
        var bodyW = Math.Max(120, textW + padX * 2);
        var bodyH = Math.Max(68, textH + padY * 2);
        var work = ScreenPlace.WorkArea(anchorX, anchorY, dpi);
        const double gap = 8;
        const double tail = 22;
        var needH = bodyH + tail + gap + 28;
        var needW = bodyW + tail + gap + 28;
        var above = anchorY - work.Top;
        var below = work.Bottom - anchorY;
        var roomLeft = anchorX - work.Left;
        var roomRight = work.Right - anchorX;
        int side;
        if (above >= needH) side = 0;
        else if (below >= needH) side = 1;
        else if (roomLeft >= roomRight && roomLeft >= needW) side = 3;
        else if (roomRight >= needW) side = 2;
        else if (above >= below) side = 0;
        else side = 1;

        var along = (side is 0 or 1 ? bodyW : bodyH) / 2;
        var drawn = Draw(bodyW, bodyH, side, tail, along);
        double left;
        double top;
        switch (side)
        {
            case 1:
                left = anchorX - drawn.TipX;
                top = anchorY + gap - drawn.TipY;
                break;
            case 2:
                left = anchorX + gap - drawn.TipX;
                top = anchorY - drawn.TipY;
                break;
            case 3:
                left = anchorX - gap - drawn.TipX;
                top = anchorY - drawn.TipY;
                break;
            default:
                left = anchorX - drawn.TipX;
                top = anchorY - gap - drawn.TipY;
                break;
        }
        var spot = Clamp(left, top, drawn.Width, drawn.Height, work);
        var aim = side is 0 or 1 ? anchorX - spot.X - drawn.OriginX : anchorY - spot.Y - drawn.OriginY;
        along = Math.Clamp(aim, drawn.AlongMin, drawn.AlongMax);
        drawn = Draw(bodyW, bodyH, side, tail, along);
        _cloud.Data = drawn.Geometry;
        WpfControls.Canvas.SetLeft(_text, drawn.TextX);
        WpfControls.Canvas.SetTop(_text, drawn.TextY);
        Width = drawn.Width;
        Height = drawn.Height;
        Left = spot.X;
        Top = spot.Y;
        if (!IsVisible) Show();
    }

    private static System.Windows.Point Clamp(double left, double top, double width, double height, System.Windows.Rect work)
    {
        const double margin = 8;
        var x = left;
        var y = top;
        if (x + width > work.Right - margin) x = work.Right - margin - width;
        if (x < work.Left + margin) x = work.Left + margin;
        if (y + height > work.Bottom - margin) y = work.Bottom - margin - height;
        if (y < work.Top + margin) y = work.Top + margin;
        return new System.Windows.Point(x, y);
    }

    private readonly record struct CloudDraw(
        Media.StreamGeometry Geometry,
        double OriginX,
        double OriginY,
        double TipX,
        double TipY,
        double TextX,
        double TextY,
        double Width,
        double Height,
        double AlongMin,
        double AlongMax);

    private CloudDraw Draw(double bodyW, double bodyH, int side, double tail, double along)
    {
        const double radius = 16;
        const double spread = 13;
        const double puff = 16;
        const double margin = 6;
        var originX = margin + (side == 2 ? tail : puff);
        var originY = margin + (side == 1 ? tail : puff);
        var right = originX + bodyW;
        var bottom = originY + bodyH;
        var edge = side is 0 or 1 ? bodyW : bodyH;
        var alongMin = radius + spread + 1;
        var alongMax = edge - radius - spread - 1;
        if (alongMax < alongMin)
        {
            alongMin = edge / 2;
            alongMax = alongMin;
        }
        along = Math.Clamp(along, alongMin, alongMax);
        var geometry = new Media.StreamGeometry();
        using (var pen = geometry.Open())
        {
            pen.BeginFigure(new System.Windows.Point(originX + radius, originY), true, true);
            Edge(pen, new System.Windows.Point(originX + radius, originY), new System.Windows.Point(right - radius, originY), side == 1, originX + along, tail, true);
            pen.ArcTo(new System.Windows.Point(right, originY + radius), new System.Windows.Size(radius, radius), 0, false, Media.SweepDirection.Clockwise, true, true);
            Edge(pen, new System.Windows.Point(right, originY + radius), new System.Windows.Point(right, bottom - radius), side == 3, originY + along, tail, true);
            pen.ArcTo(new System.Windows.Point(right - radius, bottom), new System.Windows.Size(radius, radius), 0, false, Media.SweepDirection.Clockwise, true, true);
            Edge(pen, new System.Windows.Point(right - radius, bottom), new System.Windows.Point(originX + radius, bottom), side == 0, originX + along, tail, false);
            pen.ArcTo(new System.Windows.Point(originX, bottom - radius), new System.Windows.Size(radius, radius), 0, false, Media.SweepDirection.Clockwise, true, true);
            Edge(pen, new System.Windows.Point(originX, bottom - radius), new System.Windows.Point(originX, originY + radius), side == 2, originY + along, tail, false);
            pen.ArcTo(new System.Windows.Point(originX + radius, originY), new System.Windows.Size(radius, radius), 0, false, Media.SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        var textW = _text.Width;
        var textH = _text.DesiredSize.Height;
        var extraRight = side == 3 ? tail : puff;
        var extraBottom = side == 0 ? tail : puff;
        var tipX = side switch
        {
            2 => originX - tail,
            3 => originX + bodyW + tail,
            _ => originX + along,
        };
        var tipY = side switch
        {
            1 => originY - tail,
            0 => originY + bodyH + tail,
            _ => originY + along,
        };
        return new CloudDraw(
            geometry,
            originX,
            originY,
            tipX,
            tipY,
            originX + (bodyW - textW) / 2,
            originY + (bodyH - textH) / 2,
            Math.Ceiling(right + extraRight + margin),
            Math.Ceiling(bottom + extraBottom + margin),
            alongMin,
            alongMax);
    }

    private static void Edge(
        Media.StreamGeometryContext pen,
        System.Windows.Point start,
        System.Windows.Point end,
        bool tail,
        double tipAlong,
        double tailLen,
        bool forward)
    {
        if (!tail)
        {
            Scallop(pen, start, end);
            return;
        }
        const double spread = 13;
        if (Math.Abs(start.Y - end.Y) < 0.1)
        {
            var y = start.Y;
            var tipY = forward ? y - tailLen : y + tailLen;
            var first = forward ? tipAlong - spread : tipAlong + spread;
            var second = forward ? tipAlong + spread : tipAlong - spread;
            pen.LineTo(new System.Windows.Point(first, y), true, false);
            pen.LineTo(new System.Windows.Point(tipAlong, tipY), true, false);
            pen.LineTo(new System.Windows.Point(second, y), true, false);
            pen.LineTo(end, true, false);
            return;
        }
        var x = start.X;
        var tipX = forward ? x + tailLen : x - tailLen;
        var top = forward ? tipAlong - spread : tipAlong + spread;
        var bottom = forward ? tipAlong + spread : tipAlong - spread;
        pen.LineTo(new System.Windows.Point(x, top), true, false);
        pen.LineTo(new System.Windows.Point(tipX, tipAlong), true, false);
        pen.LineTo(new System.Windows.Point(x, bottom), true, false);
        pen.LineTo(end, true, false);
    }

    private static void Scallop(Media.StreamGeometryContext pen, System.Windows.Point start, System.Windows.Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8)
        {
            pen.LineTo(end, true, false);
            return;
        }
        var bumps = Math.Clamp((int)Math.Round(len / 42), 2, 6);
        var uy = dy / len;
        var nx = uy;
        var ny = -dx / len;
        var bulge = Math.Min(14, len / bumps * 0.62);
        for (var i = 1; i <= bumps; i++)
        {
            var mid = (i - 0.5) / bumps;
            var t = i / (double)bumps;
            var control = new System.Windows.Point(start.X + dx * mid + nx * bulge, start.Y + dy * mid + ny * bulge);
            var point = new System.Windows.Point(start.X + dx * t, start.Y + dy * t);
            pen.QuadraticBezierTo(control, point, true, true);
        }
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, -20);
        style |= 0x20 | 0x80000 | 0x08000000 | 0x80;
        SetWindowLong(hwnd, -20, style);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
