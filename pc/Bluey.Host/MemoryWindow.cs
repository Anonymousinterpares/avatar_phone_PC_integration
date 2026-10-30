using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Bluey.Memory;

namespace Bluey.Host;

internal sealed class MemoryWindow : Form
{
    private readonly MemoryStore _memory;
    private readonly ListView _list = new();
    private readonly ComboBox _copies = new();
    private readonly Label _status = new();

    public event Action<string?>? Changed;

    public MemoryWindow(MemoryStore memory)
    {
        _memory = memory;
        Text = "What he remembers";
        Width = 720;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.Columns.Add("What", 420);
        _list.Columns.Add("Use", 120);
        _list.Columns.Add("From", 140);
        layout.Controls.Add(_list, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(MakeButton("Just do it", () => SetSelected("just_do_it")));
        actions.Controls.Add(MakeButton("Ask first", () => SetSelected("ask_first")));
        actions.Controls.Add(MakeButton("Off", () => SetSelected("off")));
        actions.Controls.Add(MakeButton("Forget", ForgetSelected));
        actions.Controls.Add(MakeButton("Forget everything", ForgetAll));
        actions.Controls.Add(MakeButton("Save a copy…", SaveCopy));
        actions.Controls.Add(MakeButton("Load a copy…", LoadCopy));
        layout.Controls.Add(actions, 0, 1);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _copies.Width = 280;
        _copies.DropDownStyle = ComboBoxStyle.DropDownList;
        bottom.Controls.Add(_copies);
        bottom.Controls.Add(MakeButton("Replace with this backup", () => RestoreBackup(add: false)));
        bottom.Controls.Add(MakeButton("Add this backup", () => RestoreBackup(add: true)));
        _status.AutoSize = true;
        _status.Padding = new Padding(0, 8, 0, 0);
        bottom.Controls.Add(_status);
        layout.Controls.Add(bottom, 0, 2);
        Controls.Add(layout);
        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        var book = _memory.Load();
        if (_memory.TooNew)
        {
            _status.Text = "This file is from a newer Bluey. It was left as it is.";
            return;
        }
        if (_memory.Unreadable) _status.Text = "The memory file could not be read. Load a copy.";
        else if (book.Habits.Count + book.Likes.Count + book.Never.Count == 0) _status.Text = "Nothing yet.";
        else _status.Text = "";
        foreach (var habit in book.Habits)
            AddRow(habit.Id, "When you ask to " + habit.When + ", " + habit.Do + ".", UseLabel(habit.Strength), habit.Source == "repeated" ? "It happened 3 times" : "You said it");
        foreach (var like in book.Likes)
            AddRow(like.Id, like.Text, UseLabel(like.Use), "You said it");
        foreach (var topic in book.Never)
            AddRow(UserMemory.IdFor("never", topic), "Don't remember " + topic + ".", "Don't remember", "You said it");
        var selected = _copies.SelectedItem as string;
        _copies.Items.Clear();
        foreach (var copy in _memory.Backups())
            _copies.Items.Add(copy.Name);
        if (selected != null) _copies.SelectedItem = selected;
    }

    private void AddRow(string id, string text, string use, string source)
    {
        var row = new ListViewItem(text);
        row.SubItems.Add(use);
        row.SubItems.Add(source);
        row.Tag = id;
        _list.Items.Add(row);
    }

    private void SetSelected(string use)
    {
        if (_list.SelectedItems.Count == 0) return;
        var book = _memory.Load();
        if (!UserMemory.SetUse(book, _list.SelectedItems[0].Tag as string ?? "", use)) return;
        if (_memory.Save(book)) Changed?.Invoke(null);
        Reload();
    }

    private void ForgetSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var book = _memory.Load();
        if (!UserMemory.ForgetId(book, _list.SelectedItems[0].Tag as string ?? "")) return;
        if (_memory.Save(book)) Changed?.Invoke(null);
        Reload();
    }

    private void ForgetAll()
    {
        if (MessageBox.Show("Forget the habits and likes? The do-not-remember list stays. A backup is kept.", "Bluey", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        _memory.ForgetEverything();
        Changed?.Invoke("Forgotten. A copy is in the backups.");
        Reload();
    }

    private void SaveCopy()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "bluey-memory.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var source = Path.Combine(_memory.Folder, "memory.json");
        if (!File.Exists(source))
        {
            _status.Text = "There is nothing to save yet.";
            return;
        }
        File.Copy(source, dialog.FileName, true);
        _status.Text = "Saved.";
    }

    private void LoadCopy()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var choice = MessageBox.Show("Replace what he remembers with this copy?\n\nYes replaces the list. No adds to it.", "Bluey", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;
        try
        {
            var message = _memory.RestorePath(dialog.FileName, add: choice == DialogResult.No);
            Changed?.Invoke(message);
            Reload();
            _status.Text = message;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            MessageBox.Show(ex.Message, "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void RestoreBackup(bool add)
    {
        if (_copies.SelectedItem is not string name) return;
        try
        {
            var message = _memory.Restore(name, add);
            Changed?.Invoke(message);
            Reload();
            _status.Text = message;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            MessageBox.Show(ex.Message, "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static string UseLabel(string use) => use switch
    {
        "just_do_it" => "Just do it",
        "ask_first" => "Ask first",
        "off" => "Off",
        "use" => "Use it",
        _ => use,
    };

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => click();
        return button;
    }
}
