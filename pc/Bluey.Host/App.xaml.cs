using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Bluey.Auth;
using Bluey.Brains;
using Bluey.Desk;
using Bluey.Protocol;
using Bluey.Session;

namespace Bluey.Host;

public partial class App : System.Windows.Application
{
    private PhoneLink? _phones;
    private TrayApp? _tray;
    private CursorOverlay? _overlay;
    private CharacterSession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "Bluey");
            args.Handled = true;
        };
        var store = new SettingsStore();
        var settings = store.Load();
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var brains = new BrainClient(http, store);
        _overlay = new CursorOverlay();
        _overlay.Show();
        var desk = new WindowsDesk(() => store.Load().ComputerControl, visible => _overlay.SetVisible(visible));
        _session = new CharacterSession(brains, desk);
        _session.CaptionChanged += text => _overlay.SetCaption(text);
        _session.PointChanged += point => _overlay.PointAt(point);
        _phones = new PhoneLink((packet, push, token) => _session.HandleAsync(packet, push, token));
        _phones.Start();
        var hints = new UsageHintsWindow();
        hints.Show();
        hints.SetEnabled(settings.UsageHints);
        _tray = new TrayApp(settings, store, brains, _session, _phones, _overlay, hints);
        var gaze = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        gaze.Tick += (_, _) => PumpFace(store);
        gaze.Start();
        _overlay.SourceInitialized += (_, _) => InstallHotkeys();
        if (new WindowInteropHelper(_overlay).Handle != IntPtr.Zero) InstallHotkeys();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _phones?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private void PumpFace(SettingsStore store)
    {
        if (_phones == null || _session == null) return;
        var settings = store.Load();
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.Screen.AllScreens[0].Bounds;
        var seat = settings.PhoneSeat switch
        {
            "left" => 0.18,
            "right" => 0.82,
            _ => 0.5,
        };
        var homeX = screen.Left + screen.Width * seat;
        var homeY = screen.Bottom - 40;
        GetCursorPos(out var cursor);
        var gazeX = Math.Clamp((cursor.X - homeX) / (screen.Width * 0.45), -1, 1);
        var gazeY = Math.Clamp((cursor.Y - homeY) / (screen.Height * 0.45), -1, 1);
        var mood = _session.Awake ? Mood.Listening : Mood.Resting;
        _ = _phones.BroadcastAsync(new Packet { Face = FaceState.Make(gazeX, gazeY, mood) });
    }

    private void InstallHotkeys()
    {
        var hwnd = new WindowInteropHelper(_overlay!).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(HotkeyHook);
        RegisterHotKey(hwnd, 1, 0x0002 | 0x0001, 0x53); // Ctrl+Alt+S
        RegisterHotKey(hwnd, 2, 0x0002 | 0x0001, 0x20); // Ctrl+Alt+Space
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0312 || _session == null || _phones == null) return IntPtr.Zero;
        if (wParam == 1)
        {
            _session.Stop();
            _ = _phones.BroadcastAsync(Packet.Of("stop"));
            handled = true;
        }
        else if (wParam == 2)
        {
            _ = _phones.BroadcastAsync(Packet.Of(_session.Awake ? "sleep" : "wake"));
            handled = true;
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point point);
}
