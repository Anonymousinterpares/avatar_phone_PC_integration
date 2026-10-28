using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Bluey.Protocol;

namespace Bluey.Host;

public sealed class DesktopStreamer : IDisposable
{
    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private Func<Packet, Task>? _send;
    private int _fps = 10;
    private int _quality = 60;
    private double _scale = 0.5;
    private bool _grayscale;
    private readonly object _cropGate = new();
    private CropRequest _crop = CropRequest.Normal();
    private int _generation;
    private CancellationTokenSource? _wake;

    public bool Running => _cancel != null && !_cancel.IsCancellationRequested;

    public void Start(Func<Packet, Task> send, int fps, int quality, double scale, bool grayscale)
    {
        Stop();
        _send = send;
        _fps = Math.Clamp(fps, 1, 30);
        _quality = Math.Clamp(quality, 10, 95);
        _scale = Math.Clamp(scale, 0.1, 1.0);
        _grayscale = grayscale;
        lock (_cropGate)
        {
            _crop = CropRequest.Normal();
            _generation++;
        }
        _cancel = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoop(_cancel.Token));
    }

    public void UpdateSettings(int fps, int quality, double scale, bool grayscale)
    {
        _fps = Math.Clamp(fps, 1, 30);
        _quality = Math.Clamp(quality, 10, 95);
        _scale = Math.Clamp(scale, 0.1, 1.0);
        _grayscale = grayscale;
    }

    public void UpdateCrop(double x, double y, double w, double h, int maxW = 0, int maxH = 0)
    {
        var partial = w < 0.98 || h < 0.98;
        var sharp = w >= 0.01 && h >= 0.01 && (partial || (maxW > 0 && maxH > 0));
        CropRequest next;
        if (!sharp)
        {
            next = CropRequest.Normal();
        }
        else
        {
            // Keep max >= min. Math.Clamp throws when the crop is pinned to the far edge.
            var cx = Math.Clamp(x, 0, 0.99);
            var cy = Math.Clamp(y, 0, 0.99);
            var cw = Math.Clamp(w, 0.01, Math.Max(0.01, 1.0 - cx));
            var ch = Math.Clamp(h, 0.01, Math.Max(0.01, 1.0 - cy));
            next = new CropRequest
            {
                X = cx,
                Y = cy,
                W = cw,
                H = ch,
                MaxW = Math.Max(0, maxW),
                MaxH = Math.Max(0, maxH),
                Sharp = true,
            };
        }

        lock (_cropGate)
        {
            // The phone already waits until the view leaves the last halo. A large
            // "did it move" test here dropped that request and left the old tile in place.
            if (_crop.Sharp == next.Sharp &&
                _crop.MaxW == next.MaxW &&
                _crop.MaxH == next.MaxH &&
                !CropNoticeablyDifferent(_crop, next))
            {
                return;
            }
            next.ChangedAt = Environment.TickCount64;
            _crop = next;
            _generation++;
        }
        Wake();
    }

    public void Stop()
    {
        var cancel = _cancel;
        _cancel = null;
        if (cancel != null)
        {
            cancel.Cancel();
            try { _loop?.Wait(1500); } catch { }
            cancel.Dispose();
        }
        _loop = null;
    }

    public void Dispose() => Stop();

    private void Wake()
    {
        CancellationTokenSource? wake;
        lock (_cropGate) wake = _wake;
        if (wake == null) return;
        try
        {
            if (!wake.IsCancellationRequested) wake.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private async Task CaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var start = Environment.TickCount64;
            CropRequest request;
            int generation;
            lock (_cropGate)
            {
                request = _crop;
                generation = _generation;
            }
            var fps = Math.Max(1, _fps);
            var stale = false;
            var sentSharpPreview = false;
            try
            {
                var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var bounds = screen.Bounds;

                Rectangle captureRect;
                double sendCropX = 0, sendCropY = 0, sendCropW = 1.0, sendCropH = 1.0;
                var moving = request.Sharp && Environment.TickCount64 - request.ChangedAt < 140;
                var effectiveQuality = request.Sharp ? (moving ? 72 : 95) : _quality;

                if (request.Sharp)
                {
                    sendCropX = request.X;
                    sendCropY = request.Y;
                    sendCropW = request.W;
                    sendCropH = request.H;

                    var rx = bounds.Left + (int)Math.Floor(sendCropX * bounds.Width);
                    var ry = bounds.Top + (int)Math.Floor(sendCropY * bounds.Height);
                    var rr = bounds.Left + (int)Math.Ceiling((sendCropX + sendCropW) * bounds.Width);
                    var rb = bounds.Top + (int)Math.Ceiling((sendCropY + sendCropH) * bounds.Height);
                    rx = Math.Clamp(rx, bounds.Left, bounds.Right - 1);
                    ry = Math.Clamp(ry, bounds.Top, bounds.Bottom - 1);
                    rr = Math.Clamp(rr, rx + 1, bounds.Right);
                    rb = Math.Clamp(rb, ry + 1, bounds.Bottom);
                    captureRect = new Rectangle(rx, ry, rr - rx, rb - ry);
                }
                else
                {
                    captureRect = bounds;
                }

                var w = Math.Max(1, (int)Math.Round(captureRect.Width * (request.Sharp ? 1.0 : _scale)));
                var h = Math.Max(1, (int)Math.Round(captureRect.Height * (request.Sharp ? 1.0 : _scale)));
                if (request.Sharp && request.MaxW > 0 && request.MaxH > 0 && (w > request.MaxW || h > request.MaxH))
                {
                    // The phone cannot show more pixels than its own view of this rectangle.
                    var fit = Math.Min((double)request.MaxW / w, (double)request.MaxH / h);
                    w = Math.Max(1, (int)Math.Round(w * fit));
                    h = Math.Max(1, (int)Math.Round(h * fit));
                }

                using var bitmap = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(captureRect.Location, Point.Empty, captureRect.Size);
                    try
                    {
                        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                        if (GetCursorInfo(out cursorInfo) && cursorInfo.flags == CURSOR_SHOWING && cursorInfo.hCursor != IntPtr.Zero)
                        {
                            var hdc = g.GetHdc();
                            try
                            {
                                DrawIcon(hdc, cursorInfo.ptScreenPos.x - captureRect.X, cursorInfo.ptScreenPos.y - captureRect.Y, cursorInfo.hCursor);
                            }
                            finally
                            {
                                g.ReleaseHdc(hdc);
                            }
                        }
                    }
                    catch { }
                }

                Bitmap frameToProcess = bitmap;
                Bitmap? scaled = null;
                if (w != captureRect.Width || h != captureRect.Height)
                {
                    scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = effectiveQuality >= 90
                            ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                            : System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                        g.DrawImage(bitmap, 0, 0, w, h);
                    }
                    frameToProcess = scaled;
                }

                Bitmap toEncode;
                Bitmap? gray = null;
                if (_grayscale)
                {
                    gray = MakeGrayscale(frameToProcess);
                    toEncode = gray;
                }
                else
                {
                    toEncode = frameToProcess;
                }

                var jpeg = CompressJpeg(toEncode, effectiveQuality);
                gray?.Dispose();
                scaled?.Dispose();

                lock (_cropGate)
                {
                    stale = _generation != generation && CropMoved(request, _crop);
                }
                if (!stale && _send != null)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var sharp = request.Sharp ? 1 : 0;
                    var packet = new Packet
                    {
                        Command = "deskFrame",
                        Image = Convert.ToBase64String(jpeg),
                        Text = $"{{\"w\":{w},\"h\":{h},\"cropX\":{sendCropX.ToString(inv)},\"cropY\":{sendCropY.ToString(inv)},\"cropW\":{sendCropW.ToString(inv)},\"cropH\":{sendCropH.ToString(inv)},\"sharp\":{sharp},\"q\":{effectiveQuality}}}",
                    };
                    await _send(packet);
                    sentSharpPreview = request.Sharp && effectiveQuality < 90;
                }
            }
            catch (Exception)
            {
                // Screen capture can fail transiently (e.g. UAC secure desktop or sleep)
            }

            if (token.IsCancellationRequested) break;
            var elapsed = Environment.TickCount64 - start;
            int delay;
            if (stale)
            {
                delay = 1;
            }
            else if (sentSharpPreview)
            {
                var remain = 140 - (int)(Environment.TickCount64 - request.ChangedAt);
                delay = Math.Clamp(remain, 30, 140);
            }
            else
            {
                delay = Math.Max(5, (int)(1000 / fps - elapsed));
            }
            using var pace = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (_cropGate) _wake = pace;
            try { await Task.Delay(delay, pace.Token); }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested) break;
            }
            finally
            {
                lock (_cropGate)
                {
                    if (ReferenceEquals(_wake, pace)) _wake = null;
                }
            }
        }
    }

    // A repeat of the rectangle already queued. The phone sends a new one only when the view leaves the halo.
    private static bool CropNoticeablyDifferent(CropRequest older, CropRequest newer)
    {
        if (older.Sharp != newer.Sharp) return true;
        if (!older.Sharp) return false;
        if (Math.Abs(older.X - newer.X) > 0.004) return true;
        if (Math.Abs(older.Y - newer.Y) > 0.004) return true;
        if (Math.Abs(older.W - newer.W) > 0.004) return true;
        if (Math.Abs(older.H - newer.H) > 0.004) return true;
        return false;
    }

    // An in-flight frame is for a different place than the request waiting now, so the phone should not decode it.
    private static bool CropMoved(CropRequest older, CropRequest newer)
    {
        if (older.Sharp != newer.Sharp) return true;
        if (!older.Sharp) return false;
        var dx = Math.Abs((older.X + older.W / 2) - (newer.X + newer.W / 2));
        var dy = Math.Abs((older.Y + older.H / 2) - (newer.Y + newer.H / 2));
        if (older.W > 0 && dx > older.W * 0.22) return true;
        if (older.H > 0 && dy > older.H * 0.22) return true;
        if (older.W > 0 && Math.Abs(older.W - newer.W) / older.W > 0.12) return true;
        if (older.H > 0 && Math.Abs(older.H - newer.H) / older.H > 0.12) return true;
        return false;
    }

    private struct CropRequest
    {
        public double X, Y, W, H;
        public int MaxW, MaxH;
        public long ChangedAt;
        public bool Sharp;

        public static CropRequest Normal() => new() { X = 0, Y = 0, W = 1, H = 1 };
    }

    private static Bitmap MakeGrayscale(Bitmap source)
    {
        var gray = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(gray);
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
        return gray;
    }

    private static byte[] CompressJpeg(Bitmap source, int quality)
    {
        using var memory = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        source.Save(memory, codec, parameters);
        return memory.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

    private const int CURSOR_SHOWING = 0x00000001;
}

