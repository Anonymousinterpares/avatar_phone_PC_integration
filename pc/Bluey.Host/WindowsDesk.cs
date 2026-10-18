using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Bluey.Desk;
using DrawingPoint = System.Drawing.Point;

namespace Bluey.Host;

public sealed class WindowsDesk : IDesk
{
    private readonly Func<bool> _control;
    private readonly Action<bool> _overlayVisible;

    public WindowsDesk(Func<bool> control, Action<bool> overlayVisible)
    {
        _control = control;
        _overlayVisible = overlayVisible;
    }

    public bool ComputerControl => _control();

    public SizeD ScreenSize
    {
        get
        {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            return new SizeD(screen.Bounds.Width, screen.Bounds.Height);
        }
    }

    public PointD Mouse
    {
        get
        {
            GetCursorPos(out var point);
            return new PointD(point.X, point.Y);
        }
    }

    public void Warp(PointD point) => SetCursorPos((int)point.X, (int)point.Y);

    public async Task<ScreenCapture> CaptureAsync(CancellationToken cancellationToken)
    {
        _overlayVisible(false);
        try
        {
            await Task.Delay(30, cancellationToken);
            var bounds = CaptureBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            var targets = new List<ScreenTarget>();
            string? app = FrontApp();
            try { targets.AddRange(ReadControls(bounds)); }
            catch (Exception) { /* UIA is best-effort. */ }
            try { targets.AddRange(await ReadTextAsync(bitmap, bounds)); }
            catch (Exception) { /* OCR language pack may be missing. */ }
            return new ScreenCapture(Jpeg(bitmap), targets, app, new SizeD(bounds.Width, bounds.Height), new PointD(bounds.X, bounds.Y));
        }
        finally
        {
            _overlayVisible(true);
        }
    }

    public async Task ClickAsync(PointD point, bool right, int count, CancellationToken cancellationToken)
    {
        var saved = Mouse;
        Move(point);
        await Task.Delay(30, cancellationToken);
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            MouseButton(point, right, down: true);
            await Task.Delay(35, cancellationToken);
            MouseButton(point, right, down: false);
            if (i + 1 < count) await Task.Delay(80, cancellationToken);
        }
        Warp(saved);
    }

    public async Task TypeAsync(string text, Func<string, Task> onChunk, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (character is '\n' or '\r')
            {
                PressCombo("enter");
                await onChunk("\n");
            }
            else
            {
                Unicode(character);
                await onChunk(character.ToString());
            }
            await Task.Delay(18, cancellationToken);
        }
    }

    public Task PressAsync(string keys, CancellationToken cancellationToken)
    {
        PressCombo(keys);
        return Task.CompletedTask;
    }

    public async Task ScrollAsync(PointD point, int dx, int dy, CancellationToken cancellationToken)
    {
        var saved = Mouse;
        Move(point);
        var steps = 12;
        for (var i = 0; i < steps; i++)
        {
            if (dy != 0) Wheel(point, -(dy / steps));
            if (dx != 0) Wheel(point, -(dx / steps), horizontal: true);
            await Task.Delay(16, cancellationToken);
        }
        Warp(saved);
    }

    public async Task DragAsync(PointD from, PointD to, CancellationToken cancellationToken)
    {
        var saved = Mouse;
        Move(from);
        MouseButton(from, right: false, down: true);
        await Task.Delay(80, cancellationToken);
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            var point = new PointD(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            Move(point);
            await Task.Delay(16, cancellationToken);
        }
        MouseButton(to, right: false, down: false);
        Warp(saved);
    }

    public async Task<string> OpenAppAsync(string name, CancellationToken cancellationToken)
    {
        var wanted = name.Trim();
        if (wanted.Length == 0) return "Which app?";
        var known = KnownApp(wanted);
        string? match = null;
        if (known == null)
        {
            try { match = FindShortcut(wanted); }
            catch (Exception) { match = null; }
        }
        var target = known ?? match ?? wanted;
        var label = match == null ? wanted : Path.GetFileNameWithoutExtension(match);
        var before = FrontApp();
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return "I couldn't open " + wanted + ": " + ex.Message;
        }
        var shown = await WaitUntilFrontAsync(label, before, cancellationToken);
        return shown
            ? "Opened " + label + ". Its window is in front."
            : "Opened " + label + ", but its window is not in front yet. Look again before clicking.";
    }

    public string OpenUrl(string url)
    {
        var address = url.Trim();
        if (!address.Contains("://", StringComparison.Ordinal)) address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "That doesn't look like a web address.";
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        return "Opened " + uri.Host + ".";
    }

    public bool FocusedFieldIsPassword()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused != null && focused.Current.IsPassword;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? FrontApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception) { return null; }
    }

    private static string FrontTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return "";
        var buffer = new System.Text.StringBuilder(512);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static async Task<bool> WaitUntilFrontAsync(string label, string? previous, CancellationToken cancellationToken)
    {
        var needle = label.Trim().ToLowerInvariant();
        if (needle.EndsWith(".exe", StringComparison.Ordinal)) needle = needle[..^4];
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = FrontApp() ?? "";
            var title = FrontTitle();
            var processMatch = needle.Length > 0 && process.Contains(needle, StringComparison.OrdinalIgnoreCase);
            var changed = previous != null && process.Length > 0 && !process.Equals(previous, StringComparison.OrdinalIgnoreCase) && title.Length > 0 && WindowIsBig();
            if ((processMatch && WindowIsBig()) || changed)
            {
                await Task.Delay(700, cancellationToken);
                return true;
            }
            await Task.Delay(150, cancellationToken);
        }
        return false;
    }

    private static bool WindowIsBig()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return false;
        return rect.Right - rect.Left > 320 && rect.Bottom - rect.Top > 200;
    }

    private static Rectangle CaptureBounds()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != 0 && !IsUs(hwnd) && GetWindowRect(hwnd, out var rect))
        {
            var window = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (window.Width > 200 && window.Height > 200)
            {
                var center = new DrawingPoint(window.Left + window.Width / 2, window.Top + window.Height / 2);
                window.Intersect(Screen.FromPoint(center).Bounds);
                if (window.Width > 200 && window.Height > 200) return window;
            }
        }
        return Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
    }

    private static bool IsUs(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static List<ScreenTarget> ReadControls(Rectangle screen)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return new List<ScreenTarget>();
        var root = AutomationElement.FromHandle(hwnd);
        if (root == null) return new List<ScreenTarget>();
        var found = new List<(string Kind, string Label, RectD Rect)>();
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var deadline = DateTime.UtcNow.AddMilliseconds(900);
        var seen = 0;
        while (queue.Count > 0 && seen < 4000 && DateTime.UtcNow < deadline && found.Count < 400)
        {
            var element = queue.Dequeue();
            seen++;
            try
            {
                var info = element.Current;
                var kind = KindOf(info.ControlType);
                var rect = info.BoundingRectangle;
                if (kind != null && rect.Width > 3 && rect.Height > 3 && screen.IntersectsWith(Rectangle.Truncate(new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height))))
                {
                    var label = info.Name ?? "";
                    if (label.Length > 80) label = label[..80];
                    if (label.Length > 0 || kind is "text field" or "text area")
                        found.Add((kind, label.Replace('\n', ' '), new RectD(rect.X, rect.Y, rect.Width, rect.Height)));
                }
                var walker = TreeWalker.ControlViewWalker;
                var child = walker.GetFirstChild(element);
                var added = 0;
                while (child != null && added < 200)
                {
                    queue.Enqueue(child);
                    child = walker.GetNextSibling(child);
                    added++;
                }
            }
            catch (Exception)
            {
                // The tree changes while we walk it.
            }
        }
        return found.Select((item, index) => new ScreenTarget("C" + (index + 1), item.Kind, item.Label, item.Rect)).ToList();
    }

    private static string? KindOf(ControlType type)
    {
        if (type == ControlType.Button) return "button";
        if (type == ControlType.Edit) return "text field";
        if (type == ControlType.Document) return "text area";
        if (type == ControlType.Hyperlink) return "link";
        if (type == ControlType.CheckBox) return "checkbox";
        if (type == ControlType.RadioButton) return "option";
        if (type == ControlType.ComboBox) return "combo box";
        if (type == ControlType.MenuItem) return "menu";
        if (type == ControlType.TabItem) return "tab";
        if (type == ControlType.Slider) return "slider";
        if (type == ControlType.SplitButton) return "button";
        if (type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.DataItem) return "list item";
        if (type == ControlType.Text) return "text";
        return null;
    }

    private static async Task<List<ScreenTarget>> ReadTextAsync(Bitmap bitmap, Rectangle bounds)
    {
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return new List<ScreenTarget>();
        var limit = (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;
        Bitmap? shrunk = null;
        var source = bitmap;
        var scale = 1.0;
        if (limit >= 40 && (bitmap.Width > limit || bitmap.Height > limit))
        {
            scale = Math.Min((limit - 1) / (double)bitmap.Width, (limit - 1) / (double)bitmap.Height);
            shrunk = new Bitmap(bitmap, Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
            source = shrunk;
        }
        try
        {
        using var memory = new MemoryStream();
        source.Save(memory, ImageFormat.Png);
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var writer = new Windows.Storage.Streams.DataWriter(stream);
        writer.WriteBytes(memory.ToArray());
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var software = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(software);
        var targets = new List<ScreenTarget>();
        var lineIndex = 1;
        var wordIndex = 1;
        foreach (var line in result.Lines)
        {
            if (lineIndex > 500) break;
            var lineRect = line.Words.Count == 0
                ? new RectD(bounds.X, bounds.Y, 1, 1)
                : Union(line.Words.Select(word => word.BoundingRect), bounds, scale);
            targets.Add(new ScreenTarget("L" + lineIndex, "line", line.Text, lineRect));
            lineIndex++;
            if (line.Words.Count < 2) continue;
            foreach (var word in line.Words)
            {
                if (wordIndex > 900) break;
                var rect = word.BoundingRect;
                targets.Add(new ScreenTarget("W" + wordIndex, "word", word.Text, new RectD(
                    bounds.X + rect.X / scale,
                    bounds.Y + rect.Y / scale,
                    Math.Max(1, rect.Width / scale),
                    Math.Max(1, rect.Height / scale))));
                wordIndex++;
            }
        }
        return targets;
        }
        finally
        {
            shrunk?.Dispose();
        }
    }

    private static RectD Union(IEnumerable<Windows.Foundation.Rect> rects, Rectangle bounds, double scale)
    {
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        foreach (var rect in rects)
        {
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }
        var factor = scale <= 0 ? 1 : scale;
        return new RectD(bounds.X + left / factor, bounds.Y + top / factor, Math.Max(1, (right - left) / factor), Math.Max(1, (bottom - top) / factor));
    }

    private static byte[] Jpeg(Bitmap source)
    {
        const double maxPixels = 1536d * 1536d;
        var scale = Math.Min(1, Math.Sqrt(maxPixels / (source.Width * (double)source.Height)));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        using var scaled = new Bitmap(source, width, height);
        using var memory = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
        scaled.Save(memory, codec, parameters);
        return memory.ToArray();
    }

    private static string? KnownApp(string name)
    {
        var key = name.Trim();
        if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) key = key[..^4];
        return key.ToLowerInvariant() switch
        {
            "notepad" or "notatnik" => "notepad.exe",
            "calculator" or "calc" or "kalkulator" => "calc.exe",
            "paint" => "mspaint.exe",
            "explorer" or "file explorer" or "eksplorator" => "explorer.exe",
            "cmd" or "command prompt" or "wiersz polecenia" => "cmd.exe",
            "powershell" => "powershell.exe",
            "terminal" or "windows terminal" => "wt.exe",
            "task manager" or "taskmgr" or "menedzer zadan" or "menedżer zadań" => "taskmgr.exe",
            "settings" or "ustawienia" => "ms-settings:",
            _ => null,
        };
    }

    private static string? FindShortcut(string name)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };
        string? fuzzy = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !seen.Add(root)) continue;
            foreach (var file in EnumerateLinks(root))
            {
                var label = Path.GetFileNameWithoutExtension(file);
                if (label.Equals(name, StringComparison.OrdinalIgnoreCase)) return file;
                if (fuzzy == null && label.Contains(name, StringComparison.OrdinalIgnoreCase)) fuzzy = file;
            }
        }
        return fuzzy;
    }

    // AllDirectories throws on the localized Start Menu junction (Programy on a Polish PC)
    // and that abort used to hide every shortcut, including ones in the real Programs folder.
    private static IEnumerable<string> EnumerateLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir, "*.lnk"); }
            catch (Exception) { continue; }
            foreach (var file in files) yield return file;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { continue; }
            foreach (var sub in subs)
            {
                if (IsReparse(sub)) continue;
                pending.Push(sub);
            }
        }
    }

    private static bool IsReparse(string path)
    {
        try { return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception) { return true; }
    }

    private static void Move(PointD point)
    {
        var (x, y) = Absolute(point);
        Send(new INPUT
        {
            type = 0,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = 0x0001 | 0x8000 | 0x4000 },
            },
        });
    }

    private static void MouseButton(PointD point, bool right, bool down)
    {
        var (x, y) = Absolute(point);
        uint flag = right ? (down ? 0x0008u : 0x0010u) : (down ? 0x0002u : 0x0004u);
        Send(new INPUT
        {
            type = 0,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flag | 0x8000 | 0x4000 },
            },
        });
    }

    private static void Wheel(PointD point, int delta, bool horizontal = false)
    {
        var (x, y) = Absolute(point);
        Send(new INPUT
        {
            type = 0,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    mouseData = unchecked((uint)delta),
                    dwFlags = (horizontal ? 0x1000u : 0x0800u) | 0x8000 | 0x4000,
                },
            },
        });
    }

    private static (int X, int Y) Absolute(PointD point)
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = Math.Max(1, GetSystemMetrics(78) - 1);
        var height = Math.Max(1, GetSystemMetrics(79) - 1);
        var x = (int)Math.Clamp((point.X - left) * 65535 / width, 0, 65535);
        var y = (int)Math.Clamp((point.Y - top) * 65535 / height, 0, 65535);
        return (x, y);
    }

    private static void Unicode(char character)
    {
        SendKey(new KEYBDINPUT { wScan = character, dwFlags = 0x0004 });
        SendKey(new KEYBDINPUT { wScan = character, dwFlags = 0x0004 | 0x0002 });
    }

    private static void PressCombo(string combo)
    {
        var parts = combo.ToLowerInvariant().Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<ushort>();
        ushort? key = null;
        foreach (var part in parts)
        {
            switch (part)
            {
                case "ctrl":
                case "control": modifiers.Add(0x11); break;
                case "alt":
                case "option": modifiers.Add(0x12); break;
                case "shift": modifiers.Add(0x10); break;
                case "win":
                case "meta":
                case "super":
                case "cmd": modifiers.Add(0x5B); break;
                default:
                    key = KeyCode(part);
                    break;
            }
        }
        if (key == null) throw new InvalidOperationException("I don't know the key \"" + combo + "\".");
        foreach (var modifier in modifiers) SendKey(new KEYBDINPUT { wVk = modifier });
        SendKey(new KEYBDINPUT { wVk = key.Value });
        SendKey(new KEYBDINPUT { wVk = key.Value, dwFlags = 0x0002 });
        for (var i = modifiers.Count - 1; i >= 0; i--) SendKey(new KEYBDINPUT { wVk = modifiers[i], dwFlags = 0x0002 });
    }

    private static ushort? KeyCode(string part)
    {
        if (part.Length == 1 && part[0] is >= 'a' and <= 'z') return (ushort)char.ToUpperInvariant(part[0]);
        if (part.Length == 1 && part[0] is >= '0' and <= '9') return part[0];
        if (part.StartsWith('f') && int.TryParse(part[1..], out var function) && function is >= 1 and <= 12) return (ushort)(0x70 + function - 1);
        return part switch
        {
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "space" or "spacebar" => 0x20,
            "backspace" or "delete" => 0x08,
            "del" => 0x2E,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            _ => null,
        };
    }

    private static void SendKey(KEYBDINPUT key) => Send(new INPUT { type = 1, union = new INPUTUNION { ki = key } });

    private static void Send(INPUT input)
    {
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0) throw new InvalidOperationException("Windows rejected the input.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out DrawingPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
