using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Bluey.Commands;
using Bluey.Protocol;
using Media = System.Windows.Media;

namespace Bluey.Host;

public sealed class CommandPrompt
{
    private readonly PhoneLink _phones;
    private readonly object _gate = new();
    private Pending? _pending;

    public CommandPrompt(PhoneLink phones) => _phones = phones;

    public async Task<CommandChoice> AskAsync(CommandProposal proposal, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        string? previous;
        lock (_gate) previous = _pending?.Id;
        if (previous != null) Finish(previous, CommandChoice.No);
        var pending = new Pending(id);
        lock (_gate) _pending = pending;
        var body = JsonSerializer.Serialize(new
        {
            id,
            program = proposal.ProgramName,
            args = proposal.Args,
            folder = proposal.Folder,
            display = proposal.Display,
        });
        await _phones.BroadcastAsync(Packet.Of("commandAsk", body));
        Show(id, proposal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var registration = linked.Token.Register(() => Finish(id, CommandChoice.No));
        try
        {
            return await pending.Done.Task;
        }
        finally
        {
            Finish(id, CommandChoice.No);
        }
    }

    public void Offer(string? text)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
            var choice = root.TryGetProperty("choice", out var choiceValue) ? choiceValue.GetString() ?? "" : "";
            Finish(id, choice switch
            {
                "once" => CommandChoice.Once,
                "always" => CommandChoice.Always,
                _ => CommandChoice.No,
            });
        }
        catch (JsonException)
        {
            // A bad answer counts as no only when it names the open ask.
        }
    }

    private void Show(string id, CommandProposal proposal)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_gate)
            {
                if (_pending == null || _pending.Id != id || _pending.Done.Task.IsCompleted) return;
            }
            var window = Build(proposal, id);
            lock (_gate)
            {
                if (_pending == null || _pending.Id != id || _pending.Done.Task.IsCompleted) return;
                _pending.Window = window;
            }
            window.Show();
        }));
    }

    private Window Build(CommandProposal proposal, string id)
    {
        var window = new Window
        {
            Title = "Bluey wants to run a program",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Background = Media.Brushes.White,
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Line(proposal.Display, 16, bold: true));
        root.Children.Add(Line("Folder", 13, bold: true));
        root.Children.Add(Line(proposal.Folder, 13, bold: false));
        if (proposal.Args.Length > 0)
        {
            root.Children.Add(Line("Arguments", 13, bold: true));
            foreach (var arg in proposal.Args) root.Children.Add(Line(arg, 13, bold: false));
        }
        root.Children.Add(Line("Run once does it this time. Always allow saves this exact program, these arguments, and this folder.", 12, bold: false));
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(MakeButton("Run once", () => Finish(id, CommandChoice.Once)));
        row.Children.Add(MakeButton("Always allow", () => Finish(id, CommandChoice.Always)));
        row.Children.Add(MakeButton("No", () => Finish(id, CommandChoice.No)));
        root.Children.Add(row);
        window.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        window.Closed += (_, _) => Finish(id, CommandChoice.No);
        return window;
    }

    private static TextBlock Line(string text, double size, bool bold) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
        Foreground = Media.Brushes.Black,
    };

    private static System.Windows.Controls.Button MakeButton(string text, Action click)
    {
        var button = new System.Windows.Controls.Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), IsCancel = text == "No" };
        button.Click += (_, _) => click();
        return button;
    }

    private void Finish(string id, CommandChoice choice)
    {
        Window? window;
        lock (_gate)
        {
            if (_pending == null || _pending.Id != id) return;
            if (!_pending.Done.TrySetResult(choice)) return;
            window = _pending.Window;
            _pending = null;
        }
        _ = _phones.BroadcastAsync(Packet.Of("commandAsk", ""));
        if (window == null) return;
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { window.Close(); } catch (Exception) { }
        }));
    }

    private sealed class Pending
    {
        public Pending(string id) => Id = id;
        public string Id { get; }
        public TaskCompletionSource<CommandChoice> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Window? Window { get; set; }
    }
}
