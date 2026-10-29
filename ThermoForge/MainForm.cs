using System.Diagnostics;
using System.Security.Principal;

namespace ThermoForge;

public sealed class MainForm : Form
{
    private static readonly Color Ground = Color.FromArgb(23, 23, 25), Surface = Color.FromArgb(19, 18, 23), Raised = Color.FromArgb(33, 32, 35), Ink = Color.FromArgb(242, 242, 243), Muted = Color.FromArgb(160, 161, 167), Line = Color.FromArgb(58, 58, 62), Danger = Color.FromArgb(229, 86, 111);
    private readonly Settings settings = Settings.Load();
    private readonly SensorService sensors;
    private readonly StressController stress;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private readonly CancellationTokenSource closing = new();
    private readonly Label cpuTemp = Metric(), cpuLoad = Small(), cpuName = Small(), gpuTemp = Metric(), gpuLoad = Small(), status = Small(), timerValue = Metric(), sensorNote = Small();
    private readonly Button cpuButton = Button("Pokreni CPU"), gpuButton = Button("Pokreni GPU"), stopButton = Button("Zaustavi sve"), updateButton = Button("Provjeri nadogradnje"), adminButton = Button("Aktiviraj CPU senzore");
    private readonly ComboBox duration = Combo(), adapterSelect = Combo();
    private readonly NumericUpDown cpuLimit = Limit(40, 110), gpuLimit = Limit(40, 105);
    private readonly DataGridView table = new();
    private readonly ToolTip tips = new();
    private GpuAdapter[] adapters = [];
    private DeviceReading? selectedGpu;
    private ReleaseInfo? availableRelease;
    private bool updateBusy;
    private string? lastTableShape;
    private long tableTimestamp;
    private bool? tableFresh;
    private bool disposed;
    public MainForm()
    {
        Text = "ThermoForge · CPU i GPU stress test"; Name = "ThermoForge";
        ClientSize = new(960, 780); MinimumSize = new(830, 650); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ground; ForeColor = Ink; Font = new("Segoe UI", 10); AutoScaleMode = AutoScaleMode.None;
        _ = Handle;
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { stress?.StopAll(); e.Handled = true; } };
        BuildUi();
        ScaleLayout(DeviceDpi / 96f);
        DpiChanged += (_, e) => ScaleLayout(e.DeviceDpiNew / (float)e.DeviceDpiOld);
        sensors = new(); stress = new(sensors, settings);
        try { adapters = GpuWorkload.Adapters(); } catch (Exception e) { Diagnostics.Log("DXGI: " + e); }
        foreach (var adapter in adapters) adapterSelect.Items.Add(adapter);
        if (adapters.Length > 0) adapterSelect.SelectedIndex = Math.Max(0, Array.FindIndex(adapters, a => a.Name == settings.GpuName));
        duration.Items.AddRange(["Ništa · do ručnog prekida", "1 minuta", "5 minuta", "10 minuta", "15 minuta", "30 minuta"]);
        duration.SelectedIndex = Array.IndexOf(new[] { 0, 1, 5, 10, 15, 30 }, settings.Minutes);
        cpuLimit.Value = settings.CpuLimit; gpuLimit.Value = settings.GpuLimit;
        duration.SelectedIndexChanged += (_, _) => { settings.Minutes = new[] { 0, 1, 5, 10, 15, 30 }[duration.SelectedIndex]; settings.Save(); };
        cpuLimit.ValueChanged += (_, _) => { settings.CpuLimit = (int)cpuLimit.Value; settings.Save(); };
        gpuLimit.ValueChanged += (_, _) => { settings.GpuLimit = (int)gpuLimit.Value; settings.Save(); };
        adapterSelect.SelectedIndexChanged += (_, _) => { settings.GpuName = (adapterSelect.SelectedItem as GpuAdapter)?.Name; settings.Save(); };
        cpuButton.Click += (_, _) => { if (stress.CpuRunning) stress.StopCpu(); else ShowError(stress.StartCpu()); UpdateView(); };
        gpuButton.Click += (_, _) => { if (stress.GpuRunning) stress.StopGpu(); else if (adapterSelect.SelectedItem is GpuAdapter a && selectedGpu != null) ShowError(stress.StartGpu(a, selectedGpu.Id)); UpdateView(); };
        stopButton.Click += (_, _) => { stress.StopAll(); UpdateView(); };
        adminButton.Click += (_, _) => Elevate();
        updateButton.Click += async (_, _) => { if (availableRelease == null) await CheckUpdates(false); if (availableRelease != null) await InstallUpdate(); };
        refresh.Tick += (_, _) => UpdateView(); refresh.Start();
        Shown += async (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
            if (StartPosition != FormStartPosition.Manual) Location = new(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            UpdateView(); if (settings.CheckUpdates) await CheckUpdates(true);
        };
        FormClosing += (_, _) => { stress.StopAll("Zatvaranje programa: svi testovi zaustavljeni."); settings.Save(); closing.Cancel(); };
    }
    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 20, 28, 18), ColumnCount = 1, RowCount = 6, BackColor = Ground };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 66, 184, 142 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 54)); root.RowStyles.Add(new(SizeType.Absolute, 42));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 230));
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(new Label { Text = "ThermoForge", Font = new("Segoe UI Semibold", 22), AutoSize = true, Margin = Padding.Empty });
        brand.Controls.Add(new Label { Text = "Temperature i stress test · v" + UpdateService.Version, ForeColor = Muted, AutoSize = true, Margin = new(2, 0, 0, 0) });
        header.Controls.Add(brand, 0, 0); header.Controls.Add(adminButton, 1, 0); adminButton.Dock = DockStyle.Top; root.Controls.Add(header, 0, 0);
        tips.SetToolTip(adminButton, "Ponovno pokretanje uz administratorska prava za pristup PawnIO CPU senzorima. Testovi se prije toga zaustavljaju.");
        var devices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new(0, 10, 0, 14) };
        devices.ColumnStyles.Add(new(SizeType.Percent, 50)); devices.ColumnStyles.Add(new(SizeType.Percent, 50));
        devices.Controls.Add(DevicePanel("CPU", cpuName, cpuTemp, cpuLoad, cpuButton, null), 0, 0);
        devices.Controls.Add(DevicePanel("GPU", null, gpuTemp, gpuLoad, gpuButton, adapterSelect), 1, 0); root.Controls.Add(devices, 0, 1);
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Surface, Padding = new(18, 10, 18, 10), Margin = new(0, 0, 0, 14) };
        controls.ColumnStyles.Add(new(SizeType.Percent, 40)); controls.ColumnStyles.Add(new(SizeType.Percent, 31)); controls.ColumnStyles.Add(new(SizeType.Percent, 29));
        var timerPanel = Stack(); timerPanel.Controls.Add(Title("Zajednički timer")); duration.Width = 245; timerPanel.Controls.Add(duration); timerPanel.Controls.Add(timerValue); controls.Controls.Add(timerPanel, 0, 0);
        var limits = Stack(); limits.Controls.Add(Title("Zaustavi na temperaturi"));
        var cpuRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 5, 0, 0) }; cpuRow.Controls.Add(new Label { Text = "CPU", Width = 45, Padding = new(0, 5, 0, 0) }); cpuRow.Controls.Add(cpuLimit); cpuRow.Controls.Add(new Label { Text = "°C", AutoSize = true, Padding = new(0, 5, 0, 0) }); limits.Controls.Add(cpuRow);
        var gpuRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 5, 0, 0) }; gpuRow.Controls.Add(new Label { Text = "GPU", Width = 45, Padding = new(0, 5, 0, 0) }); gpuRow.Controls.Add(gpuLimit); gpuRow.Controls.Add(new Label { Text = "°C", AutoSize = true, Padding = new(0, 5, 0, 0) }); limits.Controls.Add(gpuRow); controls.Controls.Add(limits, 1, 0);
        var emergency = Stack(); stopButton.Width = 185; stopButton.Height = 40; emergency.Controls.Add(stopButton); var keyHint = Small(); keyHint.Text = "Esc · zaustavi oba testa"; keyHint.Margin = new(2, 8, 0, 0); emergency.Controls.Add(keyHint); controls.Controls.Add(emergency, 2, 0); root.Controls.Add(controls, 0, 2);
        tips.SetToolTip(timerValue, "Odbrojavanje počinje s prvim testom. Uključivanje drugog testa ne vraća timer na početak. Istek zaustavlja oba testa.");
        tips.SetToolTip(cpuLimit, "Zaštita koristi najvišu dostupnu CPU temperaturu, uključujući jezgre i paket."); tips.SetToolTip(gpuLimit, "Zaštita koristi najvišu GPU temperaturu, uključujući hotspot i memoriju ako postoje.");
        var data = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        data.ColumnStyles.Add(new(SizeType.Percent, 100));
        data.RowStyles.Add(new(SizeType.Absolute, 31)); data.RowStyles.Add(new(SizeType.Percent, 100)); data.Controls.Add(Title("Senzori · CPU, jezgre i grafičke kartice"), 0, 0);
        table.Dock = DockStyle.Fill; table.BackgroundColor = Surface; table.BorderStyle = BorderStyle.None; table.ReadOnly = true; table.AllowUserToAddRows = false; table.AllowUserToDeleteRows = false; table.AllowUserToResizeRows = false; table.RowHeadersVisible = false; table.AutoGenerateColumns = false; table.EnableHeadersVisualStyles = false; table.SelectionMode = DataGridViewSelectionMode.FullRowSelect; table.MultiSelect = false;
        table.DefaultCellStyle = new() { BackColor = Surface, ForeColor = Ink, SelectionBackColor = Raised, SelectionForeColor = Ink, Padding = new(8, 0, 8, 0), Font = new("Segoe UI", 10) };
        table.ColumnHeadersDefaultCellStyle = new() { BackColor = Surface, ForeColor = Muted, Font = new("Segoe UI", 9), Padding = new(8, 0, 8, 0) }; table.ColumnHeadersHeight = 32; table.RowTemplate.Height = 28; table.GridColor = Raised; table.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; table.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "device", HeaderText = "Uređaj", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 43 });
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "sensor", HeaderText = "Senzor", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 33 });
        foreach (var pair in new[] { ("current", "Sada"), ("max", "Maksimum") })
        { var col = new DataGridViewTextBoxColumn { Name = pair.Item1, HeaderText = pair.Item2, Width = 112, DefaultCellStyle = new() { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new("Consolas", 10) } }; col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; table.Columns.Add(col); }
        data.Controls.Add(table, 0, 1); root.Controls.Add(data, 0, 3);
        sensorNote.Dock = DockStyle.Fill; sensorNote.AutoSize = false; sensorNote.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(sensorNote, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty }; footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.Absolute, 220)); status.Dock = DockStyle.Fill; status.AutoSize = false; status.TextAlign = ContentAlignment.MiddleLeft; footer.Controls.Add(status, 0, 0); footer.Controls.Add(updateButton, 1, 0); root.Controls.Add(footer, 0, 5);
        tips.SetToolTip(updateButton, "Provjera novih službenih objava na GitHubu. Nadogradnja se preuzima i instalira tek nakon tvoje potvrde.");
        var menu = new ContextMenuStrip(); menu.Items.Add("Mapa dnevnika i postavki", null, (_, _) => { Directory.CreateDirectory(Settings.Folder); Process.Start(new ProcessStartInfo(Settings.Folder) { UseShellExecute = true }); });
        var auto = new ToolStripMenuItem("Automatska provjera nadogradnji") { Checked = settings.CheckUpdates, CheckOnClick = true }; auto.CheckedChanged += (_, _) => { settings.CheckUpdates = auto.Checked; settings.Save(); }; menu.Items.Add(auto); menu.Items.Add("O programu i licencije", null, (_, _) => MessageBox.Show("ThermoForge 1.0.0\nCPU SIMD + Direct3D 11 GPU compute\n\nZaštita: najviša dostupna temperatura. Bez senzora nema testa. Prag GPU-a vrijedi i za hotspot/memoriju.\n\nNeki procesori ne daju temperature pojedinih jezgri. Visoko opterećenje ovisi o hardveru, hlađenju i ograničenjima sustava.\n\nLicencije i izvori: THIRD-PARTY-NOTICES.txt uz program.\nGitHub: github.com/mblazunaj/ThermoForge", "O programu", MessageBoxButtons.OK, MessageBoxIcon.Information)); ContextMenuStrip = menu;
    }
    private static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private static Label Title(string text) => new() { Text = text, AutoSize = true, Font = new("Segoe UI Semibold", 11), Margin = new(0, 0, 0, 5) };
    private static Label Small() => new() { AutoSize = true, ForeColor = Muted, Font = new("Segoe UI", 9), Margin = new(0, 2, 0, 0) };
    private static Label Metric() => new() { Text = "—", AutoSize = true, ForeColor = Ink, Font = new("Consolas", 24), Margin = new(0, 4, 0, 0) };
    private static Button Button(string text) => new ActionButton { Text = text, Height = 35, Width = 210, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Ink, Cursor = Cursors.Hand, Margin = new(0, 0, 0, 0), UseVisualStyleBackColor = false, FlatAppearance = { BorderColor = Line, BorderSize = 1 } };
    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Ink, Width = 300, IntegralHeight = true, Margin = new(0, 2, 0, 0) };
    private static NumericUpDown Limit(int min, int max) => new() { Minimum = min, Maximum = max, Width = 65, BackColor = Raised, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
    private static Control DevicePanel(string title, Label? name, Label temp, Label load, Button button, ComboBox? combo)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, ColumnCount = 2, RowCount = 4, Padding = new(18, 10, 18, 10), Margin = title == "CPU" ? new(0, 0, 8, 0) : new(8, 0, 0, 0) };
        p.ColumnStyles.Add(new(SizeType.Percent, 100)); p.ColumnStyles.Add(new(SizeType.Absolute, 166));
        p.RowStyles.Add(new(SizeType.Absolute, 23)); p.RowStyles.Add(new(SizeType.Absolute, 31)); p.RowStyles.Add(new(SizeType.Percent, 100)); p.RowStyles.Add(new(SizeType.Absolute, 35));
        p.Controls.Add(Title(title), 0, 0); p.SetColumnSpan(p.GetControlFromPosition(0, 0)!, 2);
        if (combo != null) { combo.Dock = DockStyle.Fill; p.Controls.Add(combo, 0, 1); p.SetColumnSpan(combo, 2); }
        else if (name != null) { name.AutoSize = false; name.Dock = DockStyle.Fill; name.AutoEllipsis = true; p.Controls.Add(name, 0, 1); p.SetColumnSpan(name, 2); }
        p.Controls.Add(temp, 0, 2); load.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; p.Controls.Add(load, 1, 2);
        button.Dock = DockStyle.Fill; p.Controls.Add(button, 0, 3); p.SetColumnSpan(button, 2); return p;
    }
    private static string Temperature(float? v) => v is float n ? $"{n:F1} °C" : "—";
    private void UpdateView()
    {
        if (disposed) return;
        // DataGridView also scales fixed columns when its native handle is created.
        // Reapply their intended DPI width after handle creation and parent layout.
        int numberWidth = 112 * DeviceDpi / 96;
        foreach (string name in new[] { "current", "max" }) if (table.Columns[name] is { } column && column.Width != numberWidth) column.Width = numberWidth;
        stress.UiPulse(); var snapshot = sensors.Current; bool fresh = snapshot.Fresh(Stopwatch.GetTimestamp()) && snapshot.Error == null;
        var cpus = snapshot.Devices.Where(d => d.IsCpu).ToArray();
        var cpu = cpus.FirstOrDefault();
        selectedGpu = MatchGpu(snapshot, adapterSelect.SelectedItem as GpuAdapter);
        cpuName.Text = cpu?.Name ?? "Otkrivanje CPU-a…";
        cpuTemp.Text = Temperature(fresh ? cpus.Select(c => c.Temperature).DefaultIfEmpty().Max() : null); gpuTemp.Text = Temperature(fresh ? selectedGpu?.Temperature : null);
        cpuLoad.Text = "Opterećenje " + (fresh && cpu?.Load is float cl ? $"{cl:F0}%" : "—"); gpuLoad.Text = "Opterećenje " + (fresh && selectedGpu?.Load is float gl ? $"{gl:F0}%" : "—");
        cpuButton.Text = stress.CpuRunning ? "Zaustavi CPU" : "Pokreni CPU"; gpuButton.Text = stress.GpuRunning ? "Zaustavi GPU" : "Pokreni GPU";
        cpuButton.Enabled = stress.CpuRunning || (fresh && cpus.Length > 0 && cpus.All(c => c.Hottest.HasValue)); gpuButton.Enabled = stress.GpuRunning || (fresh && selectedGpu?.Hottest != null && adapterSelect.SelectedItem != null);
        cpuButton.FlatAppearance.BorderColor = stress.CpuRunning ? Danger : Line; gpuButton.FlatAppearance.BorderColor = stress.GpuRunning ? Danger : Line;
        stopButton.Enabled = stress.Running; duration.Enabled = cpuLimit.Enabled = gpuLimit.Enabled = adapterSelect.Enabled = !stress.Running;
        timerValue.Text = stress.Running ? (settings.Minutes == 0 ? "∞ · " + FormatTime(stress.Elapsed) : FormatTime(TimeSpan.FromMinutes(settings.Minutes) - stress.Elapsed)) : settings.Minutes == 0 ? "Neograničeno" : $"{settings.Minutes:00}:00";
        status.Text = stress.Message; status.ForeColor = stress.Message.StartsWith("Zaustavljeno:") || stress.Message.StartsWith("Test je prekinut") ? Danger : Muted;
        bool admin = IsAdmin(); adminButton.Visible = !admin; adminButton.Enabled = !stress.Running;
        if (snapshot.Timestamp == 0) sensorNote.Text = "Učitavanje senzora…";
        else if (!fresh) sensorNote.Text = snapshot.Error == null ? "Očitanja nisu svježa. Testovi su onemogućeni." : "Senzori: " + snapshot.Error;
        else if (cpu?.Hottest == null) sensorNote.Text = admin ? "CPU temperatura nije dostupna. Provjeri PawnIO driver i podršku procesora. CPU test je onemogućen." : "Za CPU temperature odaberi „Aktiviraj CPU senzore”. Potreban je PawnIO driver iz instalacije.";
        else if (!cpus.Any(c => c.Temperatures.Any(t => t.Name.Contains("Core #") || t.Name.Contains("Core ", StringComparison.OrdinalIgnoreCase)))) sensorNote.Text = "Ovaj CPU ne izlaže temperature pojedinih jezgri. Prikazani su svi dostupni CPU senzori.";
        else sensorNote.Text = "Osvježavanje svake sekunde · Zaštita koristi najvišu temperaturu, uključujući GPU hotspot i memoriju.";
        if (snapshot.Timestamp != tableTimestamp || tableFresh != fresh)
        {
            tableTimestamp = snapshot.Timestamp; tableFresh = fresh;
            var rows = snapshot.Devices.SelectMany(d => d.Temperatures.Select(t => (Device: d, Sensor: t))).ToArray();
            string shape = string.Join("|", rows.Select(r => r.Sensor.Id));
            if (shape != lastTableShape) { table.Rows.Clear(); foreach (var row in rows) table.Rows.Add(row.Device.Name, row.Sensor.Name, "—", "—"); lastTableShape = shape; }
            for (int i = 0; i < rows.Length; i++) { table.Rows[i].Cells[2].Value = Temperature(fresh ? rows[i].Sensor.Value : null); table.Rows[i].Cells[3].Value = Temperature(rows[i].Sensor.Max); }
        }
    }
    public static DeviceReading? MatchGpu(Snapshot snapshot, GpuAdapter? adapter)
    {
        if (adapter == null) return null;
        static string Normalize(string s) => new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var name = Normalize(adapter.Name);
        var matches = snapshot.Devices.Where(d => !d.IsCpu && Normalize(d.Name) == name).ToArray();
        // No vendor-only fallback: binding the wrong temperature would invalidate protection.
        return matches.Length == 1 ? matches[0] : null;
    }
    private static string FormatTime(TimeSpan time) { if (time < TimeSpan.Zero) time = TimeSpan.Zero; return time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}"; }
    internal void RenderForVerification(string path, int width, int height)
    {
        ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new(-20000, -20000);
        Show(); ClientSize = new(width * DeviceDpi / 96, height * DeviceDpi / 96); Application.DoEvents();
        var wait = Stopwatch.StartNew();
        while (sensors.Current.Timestamp == 0 && wait.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(100);
        UpdateView(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path);
        File.WriteAllText(path + ".layout.json", System.Text.Json.JsonSerializer.Serialize(new { DeviceDpi, Width, Height, GridWidth = table.ClientSize.Width, Columns = table.Columns.Cast<DataGridViewColumn>().Select(c => new { c.Name, c.Width }).ToArray() }));
    }
    private void ScaleLayout(float factor)
    {
        if (Math.Abs(factor - 1) < .01) return;
        var originalClientSize = ClientSize;
        int S(int n) => (int)Math.Round(n * factor);
        Padding P(Padding p) => new(S(p.Left), S(p.Top), S(p.Right), S(p.Bottom));
        var all = new List<Control>();
        void Collect(Control parent) { all.Add(parent); parent.SuspendLayout(); foreach (Control c in parent.Controls) Collect(c); }
        Collect(this);
        foreach (var c in all)
        {
            c.Padding = P(c.Padding); c.Margin = P(c.Margin);
            c.MinimumSize = new(S(c.MinimumSize.Width), S(c.MinimumSize.Height));
            if (c is not Form) { c.Size = new(S(c.Width), S(c.Height)); c.Location = new(S(c.Left), S(c.Top)); }
            if (c is TableLayoutPanel panel)
            {
                foreach (RowStyle row in panel.RowStyles) if (row.SizeType == SizeType.Absolute) row.Height *= factor;
                foreach (ColumnStyle col in panel.ColumnStyles) if (col.SizeType == SizeType.Absolute) col.Width *= factor;
            }
            if (c is DataGridView grid)
            {
                grid.RowTemplate.Height = S(grid.RowTemplate.Height); grid.ColumnHeadersHeight = S(grid.ColumnHeadersHeight);
                foreach (DataGridViewColumn column in grid.Columns) if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) column.Width = S(column.Width);
            }
        }
        ClientSize = new(S(originalClientSize.Width), S(originalClientSize.Height));
        for (int i = all.Count - 1; i >= 0; i--) all[i].ResumeLayout(true);
    }
    private sealed class ActionButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackColor); e.Graphics.FillRectangle(bg, ClientRectangle);
            using var border = new Pen(FlatAppearance.BorderColor); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Ink, BackColor);
        }
    }
    private static bool IsAdmin() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    private void ShowError(string? error) { if (error != null) MessageBox.Show(this, error, "Test nije pokrenut", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    private void Elevate()
    {
        try { settings.Save(); stress.StopAll(); Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--wait-for " + Environment.ProcessId }); Close(); }
        catch (Exception e) { ShowError("Administratorsko pokretanje nije dovršeno: " + e.Message); }
    }
    private async Task CheckUpdates(bool quiet)
    {
        if (updateBusy) return; updateBusy = true; updateButton.Enabled = false; updateButton.Text = "Provjera…";
        try
        {
            availableRelease = await UpdateService.CheckAsync(closing.Token);
            updateButton.Text = availableRelease == null ? "Provjeri nadogradnje" : $"Instaliraj v{availableRelease.Version}";
            if (!quiet && availableRelease == null) MessageBox.Show(this, "Nema novije stabilne objave na GitHubu.", "Nadogradnje");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { updateButton.Text = "Ponovi provjeru"; Diagnostics.Log("Nadogradnje: " + e.Message); if (!quiet) MessageBox.Show(this, "Provjera nadogradnji nije uspjela:\n" + e.Message, "Nadogradnje"); }
        finally { updateBusy = false; if (!IsDisposed) updateButton.Enabled = true; }
    }
    private async Task InstallUpdate()
    {
        if (availableRelease == null || updateBusy) return;
        if (MessageBox.Show(this, $"Preuzeti i instalirati ThermoForge {availableRelease.Version}?\n\nAktivni testovi bit će zaustavljeni. Postavke se čuvaju.", "Nadogradnja", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        stress.StopAll("Zaustavljeno zbog nadogradnje."); updateBusy = true; updateButton.Enabled = false; updateButton.Text = "Preuzimanje…";
        try
        {
            string file = await UpdateService.DownloadAsync(availableRelease, closing.Token);
            var info = new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas", Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS" };
            Process.Start(info); Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { MessageBox.Show(this, "Nadogradnja nije pokrenuta:\n" + e.Message, "Nadogradnje"); }
        finally { updateBusy = false; if (!IsDisposed) { updateButton.Enabled = true; updateButton.Text = "Provjeri nadogradnje"; } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; refresh.Stop(); refresh.Dispose(); closing.Cancel(); stress?.Dispose(); sensors?.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
