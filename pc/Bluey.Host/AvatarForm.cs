using System.Drawing;
using System.Windows.Forms;
using Bluey.Auth;

namespace Bluey.Host;

/// <summary>Size, speed, teleport, and where he sleeps. Dark, like the help bar.</summary>
public sealed class AvatarForm : Form
{
    private readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _move = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _sleep = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    public AvatarForm(AppSettings settings)
    {
        Text = "Bluey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(28, 32, 52);
        ForeColor = Color.FromArgb(244, 246, 255);
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(420, 280);
        _size.Items.AddRange(new object[] { "Small", "Medium", "Large" });
        _size.SelectedIndex = settings.BerrySize switch { "small" => 0, "medium" => 1, _ => 2 };
        _speed.Items.AddRange(new object[] { "100%", "150%", "200%" });
        _speed.SelectedIndex = settings.BerrySpeed switch { 150 => 1, 200 => 2, _ => 0 };
        _move.Items.AddRange(new object[] { "Walk", "Teleport" });
        _move.SelectedIndex = settings.BerryTeleport ? 1 : 0;
        _sleep.Items.AddRange(new object[] { "Bottom right", "Bottom left", "Top right", "Top left" });
        _sleep.SelectedIndex = settings.BerrySleepCorner switch
        {
            "bottom-left" => 1,
            "top-right" => 2,
            "top-left" => 3,
            _ => 0,
        };
        foreach (var box in new[] { _size, _speed, _move, _sleep })
        {
            box.BackColor = Color.FromArgb(18, 20, 34);
            box.ForeColor = Color.White;
            box.FlatStyle = FlatStyle.Flat;
        }
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(layout, "Size", _size);
        Add(layout, "Move speed", _speed);
        Add(layout, "Movement", _move);
        Add(layout, "Sleeps at", _sleep);
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(28, 32, 52),
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88 };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 88 };
        Style(save, Color.FromArgb(108, 134, 245));
        Style(cancel, Color.FromArgb(54, 60, 88));
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(layout);
        Controls.Add(buttons);
        save.Click += (_, _) =>
        {
            settings.BerrySize = _size.SelectedIndex switch { 0 => "small", 1 => "medium", _ => "large" };
            settings.BerrySpeed = _speed.SelectedIndex switch { 1 => 150, 2 => 200, _ => 100 };
            settings.BerryTeleport = _move.SelectedIndex == 1;
            settings.BerrySleepCorner = _sleep.SelectedIndex switch
            {
                1 => "bottom-left",
                2 => "top-right",
                3 => "top-left",
                _ => "bottom-right",
            };
        };
    }

    private static void Add(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = Color.FromArgb(244, 246, 255),
            Anchor = AnchorStyles.Left,
        }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static void Style(Button button, Color back)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = back;
        button.ForeColor = Color.White;
        button.Height = 32;
    }
}
