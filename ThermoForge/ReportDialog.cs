using System.Runtime.InteropServices;
using System.Text;

namespace ThermoForge;

internal sealed class ReportDialog : Form
{
    private readonly RichTextBox preview;
    private readonly CheckBox computerName;

    internal ReportDialog(TestResult[] results, SpecSection[] sections, DateTimeOffset readAt,
        Color ground, Color surface, Color raised, Color ink, Color muted)
    {
        Text = "ThermoForge · izvoz izvještaja";
        Font = new("Segoe UI", 10);
        BackColor = ground;
        ForeColor = ink;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(700, 600);
        MinimumSize = new(540, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4
        };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Pregledaj izvještaj pa ga kopiraj ili spremi kao tekst.",
            AutoSize = true, Margin = new(0, 0, 0, 10), ForeColor = muted
        }, 0, 0);
        computerName = new CheckBox
        {
            Text = "Uključi naziv računala", AutoSize = true, Checked = false,
            Margin = new(0, 0, 0, 12), UseVisualStyleBackColor = false
        };
        layout.Controls.Add(computerName, 0, 1);
        preview = new RichTextBox
        {
            Dock = DockStyle.Fill, BackColor = surface, ForeColor = ink, BorderStyle = BorderStyle.None,
            ReadOnly = true, DetectUrls = false, Font = new("Consolas", 10),
            AccessibleName = "Pregled izvještaja", Margin = new(0, 0, 0, 12)
        };
        layout.Controls.Add(preview, 0, 2);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty
        };
        var close = Action("Zatvori");
        close.DialogResult = DialogResult.Cancel;
        var save = Action("Spremi .txt…");
        var copy = Action("Kopiraj");
        var feedback = new Label { AutoSize = true, ForeColor = muted, Margin = new(0, 8, 8, 0) };
        actions.Controls.AddRange([close, save, copy, feedback]);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AutoScaleDimensions = new(96, 96);
        PerformAutoScale();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new(Math.Min(MinimumSize.Width, area.Width - 32), Math.Min(MinimumSize.Height, area.Height - 32));
            Size = new(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
        };
        CancelButton = close;
        computerName.CheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(preview.Text); feedback.Text = "Kopirano"; }
            catch (ExternalException) { MessageBox.Show(this, "Međuspremnik je zauzet. Pokušaj ponovno.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Spremi ThermoForge izvještaj", Filter = "Tekstualni izvještaj (*.txt)|*.txt",
                DefaultExt = "txt", AddExtension = true, OverwritePrompt = true,
                FileName = $"ThermoForge-{results[0].FinishedAt.ToLocalTime():yyyy-MM-dd-HHmmss}.txt"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, preview.Text, new UTF8Encoding(true)); feedback.Text = "Spremljeno"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, "Izvještaj nije spremljen. " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };

        void UpdatePreview()
        {
            preview.Text = TestReport.Build(results, sections, readAt, computerName.Checked);
            preview.SelectionStart = 0;
            preview.ScrollToCaret();
        }
        Button Action(string label) => new()
        {
            Text = label, AutoSize = true, MinimumSize = new(105, 35), Padding = new(8, 3, 8, 3),
            BackColor = raised, ForeColor = ink, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, Margin = new(8, 0, 0, 0)
        };
    }
}
