using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ThermoForge;

internal readonly record struct GraphSample(long Time, float? Cpu, float? Gpu, float? Package, float? CpuLoad, float? GpuLoad);

internal sealed class TelemetryPanel : TableLayoutPanel
{
    private readonly LiveGraph graph;
    private readonly ComboBox chooser;
    private readonly RichTextBox summary;
    private readonly TableLayoutPanel resultPanel;
    private readonly DataGridView comparison;
    private readonly Label comparisonNote;
    private readonly Button export;
    private readonly Font emphasis = new("Segoe UI", 10, FontStyle.Bold);
    private TestResult[] results = [];
    private bool exporting;
    internal event Func<TestResult[], Task>? ExportRequested;

    internal TelemetryPanel(Color ground, Color surface, Color raised, Color ink, Color muted)
    {
        Dock = DockStyle.Fill;
        Padding = new(0, 10, 0, 8);
        ColumnCount = 2;
        RowCount = 1;
        ColumnStyles.Add(new(SizeType.Percent, 61));
        ColumnStyles.Add(new(SizeType.Percent, 39));
        graph = new LiveGraph(surface, ink, muted) { Margin = new(0, 0, 14, 0) };
        Controls.Add(graph, 0, 0);

        resultPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = surface, Padding = new(14, 11, 14, 12),
            ColumnCount = 1, RowCount = 5, Margin = Padding.Empty
        };
        resultPanel.RowStyles.Add(new(SizeType.Absolute, 33));
        resultPanel.RowStyles.Add(new(SizeType.Absolute, 36));
        resultPanel.RowStyles.Add(new(SizeType.Percent, 100));
        resultPanel.RowStyles.Add(new(SizeType.Absolute, 0));
        resultPanel.RowStyles.Add(new(SizeType.Absolute, 38));
        resultPanel.ColumnStyles.Add(new(SizeType.Percent, 100));
        var heading = new Label
        {
            Text = "Sažetak testa", Dock = DockStyle.Fill, ForeColor = ink,
            Font = new("Segoe UI Semibold", 13), Margin = Padding.Empty
        };
        chooser = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat, BackColor = raised, ForeColor = ink,
            AccessibleName = "Odaberi rezultat testa", Margin = Padding.Empty
        };
        summary = new RichTextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = surface, ForeColor = ink,
            ReadOnly = true, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
            AccessibleName = "Rezultat testa", Margin = new(0, 6, 0, 0)
        };
        comparison = new DataGridView
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackgroundColor = surface,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AllowUserToResizeColumns = false, RowHeadersVisible = false,
            EnableHeadersVisualStyles = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = ground,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, Visible = false,
            AccessibleName = "Usporedba zadnja dva testa", Margin = new(0, 6, 0, 0)
        };
        comparison.DefaultCellStyle = new()
        {
            BackColor = surface, ForeColor = ink, SelectionBackColor = raised, SelectionForeColor = ink,
            WrapMode = DataGridViewTriState.True, Padding = new(8, 7, 8, 7)
        };
        comparison.ColumnHeadersDefaultCellStyle = new()
        {
            BackColor = surface, ForeColor = muted, Padding = new(8, 6, 8, 6)
        };
        foreach (var column in new[] { ("Svojstvo", 30f), ("Prethodni test", 35f), ("Zadnji test", 35f) })
            comparison.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = column.Item1, FillWeight = column.Item2,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable
            });
        comparisonNote = new Label
        {
            Text = TestReport.ComparisonNote, AutoSize = false, Dock = DockStyle.Fill,
            ForeColor = muted, Margin = new(0, 8, 0, 10), Visible = false
        };
        export = new Button
        {
            Text = "Izvezi izvještaj…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = raised, ForeColor = ink, UseVisualStyleBackColor = false,
            Margin = new(0, 4, 0, 0)
        };
        export.FlatAppearance.BorderColor = raised;
        export.Click += async (_, _) =>
        {
            var selection = chooser.SelectedIndex == 2 ? results.ToArray() :
                chooser.SelectedIndex >= 0 ? new[] { results[chooser.SelectedIndex] } : [];
            if (selection.Length == 0 || ExportRequested == null) return;
            exporting = true;
            export.Enabled = false;
            export.Text = "Priprema izvještaja…";
            try { await ExportRequested(selection); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Diagnostics.Log("Izvještaj: " + e.Message);
                if (!IsDisposed) MessageBox.Show(FindForm(), "Izvještaj nije pripremljen. Pokušaj ponovno.", "ThermoForge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { exporting = false; if (!IsDisposed) { export.Enabled = results.Length > 0; export.Text = "Izvezi izvještaj…"; } }
        };
        chooser.SelectedIndexChanged += (_, _) => ShowResult();
        resultPanel.Controls.Add(heading, 0, 0);
        resultPanel.Controls.Add(chooser, 0, 1);
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Controls.Add(summary);
        content.Controls.Add(comparison);
        resultPanel.Controls.Add(content, 0, 2);
        resultPanel.Controls.Add(comparisonNote, 0, 3);
        resultPanel.Controls.Add(export, 0, 4);
        Controls.Add(resultPanel, 1, 0);
        SetResults([]);
    }

    internal void Observe(GraphSample sample, long now, string? gpuKey, int cpuLimit, int gpuLimit, TestTimeline timeline) =>
        graph.Observe(sample, now, gpuKey, cpuLimit, gpuLimit, timeline);

    internal void SetResults(TestResult[] history)
    {
        results = history;
        chooser.Items.Clear();
        if (history.Length > 0) chooser.Items.Add("Zadnji test");
        if (history.Length > 1) chooser.Items.Add("Prethodni test");
        if (history.Length > 1) chooser.Items.Add("Usporedi zadnja dva");
        chooser.Enabled = history.Length > 1;
        if (history.Length == 0) ShowResult();
        else chooser.SelectedIndex = 0;
    }

    private void ShowResult()
    {
        bool compare = chooser.SelectedIndex == 2;
        SuspendLayout();
        graph.Visible = !compare;
        summary.Visible = !compare;
        comparison.Visible = comparisonNote.Visible = compare;
        resultPanel.RowStyles[3].Height = compare ? 58 * DeviceDpi / 96f : 0;
        SetColumn(resultPanel, compare ? 0 : 1);
        SetColumnSpan(resultPanel, compare ? 2 : 1);
        export.Enabled = !exporting && results.Length > 0;
        ResumeLayout(true);
        if (compare)
        {
            var previous = TestReport.Fields(results[1]);
            var latest = TestReport.Fields(results[0]);
            comparison.Rows.Clear();
            for (int i = 0; i < latest.Length; i++)
            {
                var row = comparison.Rows[comparison.Rows.Add(latest[i].Label, previous[i].Value, latest[i].Value)];
                if (i == 1 || i >= 4)
                    for (int column = 1; column <= 2; column++) row.Cells[column].Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            comparison.ClearSelection();
            return;
        }
        summary.Clear();
        if (chooser.SelectedIndex < 0)
        {
            summary.Text = "Sažetak će se prikazati kad test završi. Ovdje će ostati zadnja dva rezultata.";
            return;
        }
        foreach (var field in TestReport.Fields(results[chooser.SelectedIndex]))
            Field(field.Label, field.Value);
        summary.SelectionStart = 0;
        summary.ScrollToCaret();

        void Field(string label, string value, bool separated = true)
        {
            summary.SelectionFont = emphasis;
            summary.AppendText(label + "\n");
            summary.SelectionFont = summary.Font;
            summary.AppendText(value + (separated ? "\n\n" : "\n"));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) emphasis.Dispose();
    }

    private sealed class LiveGraph : Control
    {
        private const int WindowSeconds = 600;
        private static readonly Color CpuColor = Color.FromArgb(255, 165, 104);
        private static readonly Color GpuColor = Color.FromArgb(133, 199, 233);
        private readonly Queue<GraphSample> samples = new();
        private readonly Color muted;
        private string? gpuKey;
        private long gpuChanged, now, lastSample;
        private int cpuLimit, gpuLimit;
        private TestTimeline timeline;

        internal LiveGraph(Color surface, Color ink, Color muted)
        {
            this.muted = muted;
            Dock = DockStyle.Fill;
            BackColor = surface;
            ForeColor = ink;
            DoubleBuffered = true;
            ResizeRedraw = true;
            AccessibleName = "Graf temperature i opterećenja";
            AccessibleDescription = "Zadnjih 10 minuta. CPU, GPU i CPU paket u načinu nadzora integrirane grafike.";
        }

        internal void Observe(GraphSample sample, long time, string? selectedGpu, int cpu, int gpu, TestTimeline test)
        {
            if (selectedGpu != gpuKey)
            {
                gpuKey = selectedGpu;
                gpuChanged = Math.Max(sample.Time, lastSample + 1);
            }
            now = time;
            cpuLimit = cpu;
            gpuLimit = gpu;
            timeline = test;
            if (sample.Time > lastSample)
            {
                lastSample = sample.Time;
                samples.Enqueue(sample);
            }
            while (samples.TryPeek(out var first) && (samples.Count > 1200 || Age(first.Time) > WindowSeconds))
                samples.Dequeue();
            if (Visible) Invalidate();
        }

        private double Age(long time) => (now - time) / (double)Stopwatch.Frequency;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = DeviceDpi / 96f;
            int S(int n) => (int)Math.Round(n * scale);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int left = S(45), right = Width - S(16), middle = (Height + S(57)) / 2;
            var temperature = Rectangle.FromLTRB(left, S(100), right, middle - S(14));
            var load = Rectangle.FromLTRB(left, middle + S(26), right, Height - S(29));
            if (temperature.Width < S(120) || temperature.Height < S(42) || load.Height < S(42)) return;

            int seconds = samples.TryPeek(out var first) ? Math.Clamp((int)Math.Ceiling(Age(first.Time) / 60) * 60, 60, WindowSeconds) : 60;
            int topTemperature = 120;
            foreach (var sample in samples)
                if (Math.Max(sample.Cpu ?? 0, sample.Gpu ?? 0) > topTemperature) topTemperature = 160;

            using var headingFont = new Font("Segoe UI Semibold", 13);
            TextRenderer.DrawText(g, "Graf uživo", headingFont, new Point(S(15), S(11)), ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Write($"Zadnjih {seconds / 60} min  ·  P početak  /  K kraj", S(15), S(37), muted);
            Legend("CPU", S(15), CpuColor, DashStyle.Solid);
            Legend("GPU", S(89), GpuColor, DashStyle.Solid);
            Legend("CPU paket · iGPU", S(163), GpuColor, DashStyle.Dash);
            Write("Temperatura · °C", S(15), S(77), ForeColor);
            Write(gpuKey == null ? $"Prag CPU {cpuLimit}°" : $"Pragovi  CPU {cpuLimit}°  /  GPU {gpuLimit}°", S(156), S(77), muted);
            Write("Opterećenje · %", S(15), middle, ForeColor);
            Grid(temperature, topTemperature, topTemperature / 4);
            Grid(load, 100, 25);
            Limit(temperature, cpuLimit, topTemperature, CpuColor);
            if (gpuKey != null) Limit(temperature, gpuLimit, topTemperature, GpuColor);
            foreach (var plot in new[] { temperature, load })
            {
                Mark(plot, timeline.LastStart, "P");
                Mark(plot, timeline.LastStop, "K");
                Mark(plot, timeline.ActiveStart, "P");
            }
            Plot(temperature, topTemperature, CpuColor, DashStyle.Solid, sample => sample.Cpu);
            Plot(temperature, topTemperature, GpuColor, DashStyle.Solid, sample => sample.Time >= gpuChanged ? sample.Gpu : null);
            Plot(temperature, topTemperature, GpuColor, DashStyle.Dash, sample => sample.Time >= gpuChanged ? sample.Package : null);
            Plot(load, 100, CpuColor, DashStyle.Solid, sample => sample.CpuLoad);
            Plot(load, 100, GpuColor, DashStyle.Solid, sample => sample.Time >= gpuChanged ? sample.GpuLoad : null);

            for (int i = 0; i <= 5; i++)
            {
                int age = seconds * (5 - i) / 5;
                string text = age == 0 ? "Sada" : age >= 60 && age % 60 == 0 ? $"−{age / 60} min" : $"−{age} s";
                int width = TextRenderer.MeasureText(g, text, Font).Width;
                Write(text, Math.Clamp(left + temperature.Width * i / 5 - width / 2, 0, Width - width), load.Bottom + S(6), muted);
            }
            if (samples.Count == 0 || !samples.Any(s => s.Cpu.HasValue || s.Time >= gpuChanged && (s.Gpu.HasValue || s.Package.HasValue)))
                Write("Čekam očitanja temperature…", left + S(8), temperature.Top + S(22), muted);

            void Write(string text, int x, int y, Color color) =>
                TextRenderer.DrawText(g, text, Font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            void Legend(string label, int x, Color color, DashStyle style)
            {
                using var pen = new Pen(color, 2 * scale) { DashStyle = style };
                g.DrawLine(pen, x, S(65), x + S(16), S(65));
                Write(label, x + S(22), S(56), muted);
            }
            float X(Rectangle plot, long time) => plot.Right - plot.Width * (float)(Age(time) / seconds);
            static float Y(Rectangle plot, float value, int maximum) => plot.Bottom - plot.Height * Math.Clamp(value / maximum, 0, 1);
            void Grid(Rectangle plot, int maximum, int step)
            {
                using var pen = new Pen(Color.FromArgb(57, muted), scale);
                for (int value = 0; value <= maximum; value += step)
                {
                    float y = Y(plot, value, maximum);
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    Write(value.ToString(), S(15), (int)y - S(7), muted);
                }
            }
            void Limit(Rectangle plot, int limit, int maximum, Color color)
            {
                using var pen = new Pen(Color.FromArgb(145, color), scale) { DashStyle = DashStyle.Dot };
                float y = Y(plot, limit, maximum);
                g.DrawLine(pen, plot.Left, y, plot.Right, y);
            }
            void Mark(Rectangle plot, long timestamp, string label)
            {
                if (timestamp == 0 || Age(timestamp) is < 0 || Age(timestamp) > seconds) return;
                float x = X(plot, timestamp);
                using var pen = new Pen(muted, scale) { DashStyle = DashStyle.Dash };
                g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                if (plot == temperature) Write(label, Math.Min((int)x + S(3), plot.Right - S(10)), plot.Top + S(2), muted);
            }
            void Plot(Rectangle plot, int maximum, Color color, DashStyle style, Func<GraphSample, float?> read)
            {
                using var pen = new Pen(color, 1.8f * scale) { DashStyle = style };
                var state = g.Save();
                g.SetClip(Rectangle.Inflate(plot, 0, S(1)));
                var segment = new List<PointF>();
                long lastTime = 0;
                foreach (var sample in samples)
                {
                    if (Age(sample.Time) > seconds || read(sample) is not float value || !float.IsFinite(value))
                    { DrawSegment(); continue; }
                    if (segment.Count > 0 && Stopwatch.GetElapsedTime(lastTime, sample.Time).TotalSeconds > 3) DrawSegment();
                    segment.Add(new(X(plot, sample.Time), Y(plot, value, maximum)));
                    lastTime = sample.Time;
                }
                DrawSegment();
                g.Restore(state);

                void DrawSegment()
                {
                    if (segment.Count > 1) g.DrawLines(pen, segment.ToArray());
                    else if (segment.Count == 1)
                    {
                        var point = segment[0];
                        g.DrawLine(pen, point.X - scale, point.Y, point.X + scale, point.Y);
                    }
                    segment.Clear();
                }
            }
        }
    }
}
