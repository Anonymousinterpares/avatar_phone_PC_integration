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
    private double _cropX = 0;
    private double _cropY = 0;
    private double _cropW = 1.0;
    private double _cropH = 1.0;
    private bool _zoomActive;

    public bool Running => _cancel != null && !_cancel.IsCancellationRequested;

    public void Start(Func<Packet, Task> send, int fps, int quality, double scale, bool grayscale)
    {
        Stop();
        _send = send;
        _fps = Math.Clamp(fps, 1, 30);
        _quality = Math.Clamp(quality, 10, 95);
        _scale = Math.Clamp(scale, 0.1, 1.0);
        _grayscale = grayscale;
        _zoomActive = false;
        _cropX = 0;
        _cropY = 0;
        _cropW = 1.0;
        _cropH = 1.0;
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

    public void UpdateCrop(double x, double y, double w, double h)
    {
        if (w >= 0.98 && h >= 0.98)
        {
            _zoomActive = false;
            _cropX = 0;
            _cropY = 0;
            _cropW = 1.0;
            _cropH = 1.0;
        }
        else
        {
            _cropX = Math.Clamp(x, 0, 1);
            _cropY = Math.Clamp(y, 0, 1);
            _cropW = Math.Clamp(w, 0, 1.0 - _cropX);
            _cropH = Math.Clamp(h, 0, 1.0 - _cropY);
            if (_cropW < 0.01 || _cropH < 0.01)
            {
                _zoomActive = false;
                _cropX = 0;
                _cropY = 0;
                _cropW = 1.0;
                _cropH = 1.0;
            }
            else
            {
                _zoomActive = true;
            }
        }
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

    private async Task CaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var start = Environment.TickCount64;
            try
            {
                var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var bounds = screen.Bounds;

                Rectangle captureRect;
                double effectiveScale = _scale;
                int effectiveQuality = _quality;
                double sendCropX = 0, sendCropY = 0, sendCropW = 1.0, sendCropH = 1.0;

                if (_zoomActive && (_cropW < 0.98 || _cropH < 0.98))
                {
                    sendCropX = _cropX;
                    sendCropY = _cropY;
                    sendCropW = _cropW;
                    sendCropH = _cropH;

                    // Native pixels of the whole phone view, not a smaller focus box.
                    var rx = bounds.Left + (int)Math.Floor(sendCropX * bounds.Width);
                    var ry = bounds.Top + (int)Math.Floor(sendCropY * bounds.Height);
                    var rr = bounds.Left + (int)Math.Ceiling((sendCropX + sendCropW) * bounds.Width);
                    var rb = bounds.Top + (int)Math.Ceiling((sendCropY + sendCropH) * bounds.Height);
                    rx = Math.Clamp(rx, bounds.Left, bounds.Right - 1);
                    ry = Math.Clamp(ry, bounds.Top, bounds.Bottom - 1);
                    rr = Math.Clamp(rr, rx + 1, bounds.Right);
                    rb = Math.Clamp(rb, ry + 1, bounds.Bottom);
                    captureRect = new Rectangle(rx, ry, rr - rx, rb - ry);
                    effectiveQuality = 95;
                    effectiveScale = 1.0;
                }
                else
                {
                    captureRect = bounds;
                }

                var w = Math.Max(1, (int)(captureRect.Width * effectiveScale));
                var h = Math.Max(1, (int)(captureRect.Height * effectiveScale));

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

                Bitmap frameToProcess;
                if (w != captureRect.Width || h != captureRect.Height)
                {
                    var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bitmap, 0, 0, w, h);
                    }
                    bitmap.Dispose();
                    frameToProcess = scaled;
                }
                else
                {
                    frameToProcess = bitmap;
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
                frameToProcess.Dispose();

                var base64 = Convert.ToBase64String(jpeg);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var packet = new Packet
                {
                    Command = "deskFrame",
                    Image = base64,
                    Text = $"{{\"w\":{w},\"h\":{h},\"cropX\":{sendCropX.ToString(inv)},\"cropY\":{sendCropY.ToString(inv)},\"cropW\":{sendCropW.ToString(inv)},\"cropH\":{sendCropH.ToString(inv)},\"q\":{effectiveQuality}}}",
                };

                if (_send != null)
                    await _send(packet);
            }
            catch (Exception)
            {
                // Screen capture can fail transiently (e.g. UAC secure desktop or sleep)
            }

            var elapsed = Environment.TickCount64 - start;
            var targetDelay = 1000 / Math.Max(1, _fps);
            var delay = Math.Max(5, (int)(targetDelay - elapsed));
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { break; }
        }
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

