using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Bluey.Speech;

namespace Bluey.Host;

public sealed class SupertonicSpeaker : IReplySpeaker, IDisposable
{
    private readonly SpeechConfigStore _speech;
    private readonly object _gate = new();
    private readonly string _script;
    private Process? _process;
    private StreamWriter? _stdin;
    private int _generation;
    private int _requestId;
    private string? _pending;
    private string _sex = "male";
    private string _lang = "na";
    private bool _warm;
    private bool _disposed;
    private bool _ready;
    private string? _python;
    private string? _lastError;
    private Thread? _worker;
    private readonly PhysicalSpeaker _speakers = new();
    private System.Windows.Media.MediaPlayer? _player;
    private System.Media.SoundPlayer? _soundPlayer;
    private readonly System.Threading.Timer _watchdog;

    public event Action<string>? Failed;
    public event Action<string>? StatusChanged;

    public bool Enabled => _speech.Current.TextToSpeech;
    public bool IsRunning => _process is { HasExited: false };
    public bool IsReady => _ready && IsRunning;
    public string? LastError => _lastError;

    public string StatusText =>
        !_speech.Current.TextToSpeech ? "Disabled" :
        IsReady ? "Ready (Supertonic 3)" :
        IsRunning ? "Starting model..." :
        string.IsNullOrEmpty(_lastError) ? "Stopped" : $"Error: {_lastError}";

    public SupertonicSpeaker(SpeechConfigStore speech)
    {
        _speech = speech;
        _script = LocateScript();
        _watchdog = new System.Threading.Timer(CheckHealth, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    private void CheckHealth(object? _)
    {
        if (_disposed) return;
        if (!_speech.Current.TextToSpeech)
        {
            if (_process != null) KillProcess();
            return;
        }

        bool dead;
        lock (_gate)
        {
            dead = _process == null || _process.HasExited || !_ready || _stdin == null;
        }

        if (dead)
        {
            Warmup();
        }
    }

    public void SpeakTest(string? customSentence = null)
    {
        var spoken = SpeechText.Prepare(string.IsNullOrWhiteSpace(customSentence)
            ? "Hello! Supertonic 3 text to speech is working."
            : customSentence);
        if (spoken.Length == 0) return;
        lock (_gate)
        {
            if (_disposed) return;
            _pending = spoken;
            _sex = _speech.Current.Sex;
            _lang = _speech.Current.Lang;
            _generation++;
            Monitor.Pulse(_gate);
        }
        EnsureWorker();
    }

    public void Speak(string text)
    {
        if (!_speech.Current.TextToSpeech) return;
        var spoken = SpeechText.Prepare(text);
        if (spoken.Length == 0) return;
        lock (_gate)
        {
            if (_disposed) return;
            _pending = spoken;
            _sex = _speech.Current.Sex;
            _lang = _speech.Current.Lang;
            _generation++;
            Monitor.Pulse(_gate);
        }
        EnsureWorker();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _pending = null;
            _generation++;
        }
        _speakers.Clear();
        StopPlayer();
    }

    public void Warmup()
    {
        if (!_speech.Current.TextToSpeech) return;
        lock (_gate)
        {
            if (_disposed) return;
            _warm = true;
            Monitor.Pulse(_gate);
        }
        EnsureWorker();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = null;
            _generation++;
            Monitor.PulseAll(_gate);
        }
        _watchdog.Dispose();
        _speakers.Dispose();
        StopPlayer();
        KillProcess();
    }

    private void EnsureWorker()
    {
        lock (_gate)
        {
            if (_worker != null) return;
            _worker = new Thread(Work) { IsBackground = true, Name = "Bluey speech" };
            _worker.Start();
        }
    }

    private void Work()
    {
        while (true)
        {
            string? text = null;
            var sex = "male";
            var lang = "na";
            var generation = 0;
            var warm = false;
            lock (_gate)
            {
                while (_pending == null && !_warm && !_disposed)
                    Monitor.Wait(_gate);
                if (_disposed) return;
                warm = _warm;
                _warm = false;
                if (_pending != null)
                {
                    text = _pending;
                    _pending = null;
                    sex = _sex;
                    lang = _lang;
                    generation = _generation;
                }
            }
            if (!EnsureProcess())
            {
                if (warm && text == null) continue;
                if (text != null) Report(_lastError ?? "Supertonic did not start.");
                continue;
            }
            if (text == null) continue;
            var wav = Request(text, sex, lang);
            var play = false;
            lock (_gate) play = wav != null && generation == _generation && !_disposed;
            if (play) Play(wav!);
        }
    }

    private bool EnsureProcess()
    {
        if (_process is { HasExited: false } && _ready && _stdin != null) return true;
        KillProcess();
        if (!File.Exists(_script))
        {
            Report("Supertonic script is missing: " + _script);
            return false;
        }
        _python ??= ResolvePython();
        if (_python == null)
        {
            Report("Install Python 3, then run: py -3 -m pip install supertonic");
            return false;
        }
        var start = new ProcessStartInfo
        {
            FileName = _python,
            Arguments = "-u \"" + _script + "\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["HF_HUB_DISABLE_PROGRESS_BARS"] = "1";
        start.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        start.Environment["TQDM_DISABLE"] = "1";
        try
        {
            _process = Process.Start(start);
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return false;
        }
        if (_process == null)
        {
            Report("Supertonic did not start.");
            return false;
        }
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        _stdin = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        var ready = ReadMessage(TimeSpan.FromMinutes(10));
        if (ready == null || !ready.Value.TryGetProperty("ready", out var flag) || !flag.GetBoolean())
        {
            var error = ready != null && ready.Value.TryGetProperty("error", out var message) ? message.GetString() : null;
            Report(string.IsNullOrWhiteSpace(error) ? "Supertonic did not become ready." : error!);
            KillProcess();
            return false;
        }
        _ready = true;
        _lastError = null;
        NotifyStatus();
        return true;
    }

    private string? Request(string text, string sex, string lang)
    {
        if (_stdin == null) return null;
        var id = Interlocked.Increment(ref _requestId);
        try
        {
            _stdin.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = id,
                ["text"] = text,
                ["sex"] = sex,
                ["lang"] = lang,
            }));
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            KillProcess();
            return null;
        }
        var reply = ReadMessage(TimeSpan.FromMinutes(2));
        if (reply == null)
        {
            Report("Supertonic returned no audio.");
            KillProcess();
            return null;
        }
        if (!reply.Value.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var error = reply.Value.TryGetProperty("error", out var message) ? message.GetString() : null;
            Report(string.IsNullOrWhiteSpace(error) ? "Supertonic could not speak that line." : error!);
            return null;
        }
        if (!reply.Value.TryGetProperty("wav", out var wav)) return null;
        var path = wav.GetString();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    private JsonElement? ReadMessage(TimeSpan timeout)
    {
        var stdout = _process?.StandardOutput;
        if (stdout == null) return null;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true } && stdout.EndOfStream) return null;
            string? line = null;
            try
            {
                line = stdout.ReadLine();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return null;
            }
            if (line == null) return null;
            line = line.Trim();
            if (line.Length == 0 || line[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
        }
        return null;
    }

    private void Play(string wav)
    {
        if (PhysicalSpeaker.TryReadWav(wav, out var samples, out var rate))
        {
            _speakers.Clear();
            _speakers.Enqueue(samples, rate);
            return;
        }
        try
        {
            lock (_gate)
            {
                _soundPlayer?.Stop();
                _soundPlayer?.Dispose();
                _soundPlayer = new System.Media.SoundPlayer(wav);
                _soundPlayer.Play();
            }
        }
        catch (Exception)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                _player ??= CreatePlayer();
                _player.Stop();
                _player.Close();
                _player.Open(new Uri(wav, UriKind.Absolute));
                _player.Play();
            }));
        }
    }

    private System.Windows.Media.MediaPlayer CreatePlayer()
    {
        var player = new System.Windows.Media.MediaPlayer();
        player.MediaFailed += (_, args) => Report(args.ErrorException?.Message ?? "Playback failed.");
        return player;
    }

    private void StopPlayer()
    {
        lock (_gate)
        {
            try { _soundPlayer?.Stop(); } catch { }
        }
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(new Action(() => _player?.Stop()));
    }

    private void KillProcess()
    {
        _ready = false;
        try { _stdin?.Dispose(); } catch (Exception) { }
        _stdin = null;
        var process = _process;
        _process = null;
        if (process == null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // The worker already exited.
        }
        process.Dispose();
        NotifyStatus();
    }

    private void Report(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message == _lastError) return;
        _lastError = message;
        Failed?.Invoke(message);
        NotifyStatus();
    }

    private void NotifyStatus()
    {
        try { StatusChanged?.Invoke(StatusText); } catch { }
    }

    private static string LocateScript()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "supertonic_speak.py");
        if (File.Exists(beside)) return beside;
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "pc", "Bluey.Host", "supertonic_speak.py");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return beside;
    }

    private static string? ResolvePython()
    {
        foreach (var (file, args) in new[] { ("py", "-3 -c \"import sys; print(sys.executable)\""), ("python", "-c \"import sys; print(sys.executable)\""), ("python3", "-c \"import sys; print(sys.executable)\"") })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                });
                if (probe == null) continue;
                var output = probe.StandardOutput.ReadToEnd().Trim();
                if (!probe.WaitForExit(8000))
                {
                    try { probe.Kill(entireProcessTree: true); } catch (Exception) { }
                    continue;
                }
                var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                if (probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(line) && File.Exists(line)) return line;
            }
            catch (Exception)
            {
                // Try the next launcher.
            }
        }

        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localApp, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python310", "python.exe"),
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
