namespace MicLock;

// Small "type a number" box for the Level > Custom menu item.
static class LevelDialog
{
    public static int? Ask(int current)
    {
        using var form = new Form
        {
            Text = "MicLock level",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = true,
            TopMost = true,
            AutoScaleDimensions = new SizeF(96F, 96F), // layout below is in 96 DPI pixels
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(260, 96),
        };
        var label = new Label { Text = "Lock the mic at (%):", AutoSize = true, Location = new Point(12, 18) };
        var number = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = Math.Clamp(current, 1, 100),
            Location = new Point(160, 14),
            Width = 80,
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(84, 56), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(165, 56), Width = 75 };
        form.Controls.AddRange(new Control[] { label, number, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { number.Select(0, number.Text.Length); number.Focus(); };

        return form.ShowDialog() == DialogResult.OK ? (int)number.Value : null;
    }
}
