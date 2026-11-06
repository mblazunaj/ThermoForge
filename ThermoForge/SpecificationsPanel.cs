namespace ThermoForge;

internal sealed class SpecificationsPanel : TableLayoutPanel
{
    private readonly Color surface, raised, ink, muted;
    private readonly ListBox categories = new();
    private readonly Label heading = new();
    private readonly Label status = new();
    private readonly Button reload = new();
    private readonly DataGridView fields = new();
    private readonly Font groupFont = new("Segoe UI Semibold", 10);
    private SpecSection[]? sections;
    private Task? loadingTask;
    internal DateTimeOffset ReadAt { get; private set; }

    internal SpecificationsPanel(Color ground, Color surface, Color raised, Color ink, Color muted, CancellationToken token)
    {
        this.surface = surface;
        this.raised = raised;
        this.ink = ink;
        this.muted = muted;
        Dock = DockStyle.Fill;
        BackColor = ground;
        Margin = new(0, 10, 0, 8);
        ColumnCount = 2;
        RowCount = 3;
        ColumnStyles.Add(new(SizeType.Absolute, 195));
        ColumnStyles.Add(new(SizeType.Percent, 100));
        RowStyles.Add(new(SizeType.Absolute, 44));
        RowStyles.Add(new(SizeType.Absolute, 31));
        RowStyles.Add(new(SizeType.Percent, 100));

        var navigation = new Label
        {
            Text = "Specifikacije", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ink, Font = new("Segoe UI Semibold", 12), Margin = new(0, 0, 12, 0)
        };
        Controls.Add(navigation, 0, 0);
        categories.Dock = DockStyle.Fill;
        categories.Margin = new(0, 0, 16, 0);
        categories.BackColor = surface;
        categories.ForeColor = ink;
        categories.BorderStyle = BorderStyle.None;
        categories.DrawMode = DrawMode.OwnerDrawFixed;
        categories.ItemHeight = 37;
        categories.IntegralHeight = false;
        categories.AccessibleName = "Kategorije specifikacija";
        categories.DrawItem += DrawCategory;
        categories.SelectedIndexChanged += (_, _) => ShowSection();
        Controls.Add(categories, 0, 1);
        SetRowSpan(categories, 2);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 144));
        heading.Text = "Pregled";
        heading.Dock = DockStyle.Fill;
        heading.TextAlign = ContentAlignment.MiddleLeft;
        heading.Font = new("Segoe UI Semibold", 17);
        heading.ForeColor = ink;
        header.Controls.Add(heading, 0, 0);
        reload.Text = "Osvježi podatke";
        reload.Dock = DockStyle.Fill;
        reload.Margin = new(0, 4, 0, 4);
        reload.FlatStyle = FlatStyle.Flat;
        reload.FlatAppearance.BorderColor = raised;
        reload.BackColor = raised;
        reload.ForeColor = ink;
        reload.UseVisualStyleBackColor = false;
        reload.Click += async (_, _) => await LoadAsync(token, refresh: true);
        header.Controls.Add(reload, 1, 0);
        Controls.Add(header, 1, 0);

        status.Text = "Učitavanje specifikacija…";
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        status.ForeColor = muted;
        status.Margin = new(3, 0, 0, 4);
        Controls.Add(status, 1, 1);

        fields.Dock = DockStyle.Fill;
        fields.Margin = Padding.Empty;
        fields.BackgroundColor = surface;
        fields.BorderStyle = BorderStyle.None;
        fields.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        fields.GridColor = ground;
        fields.RowHeadersVisible = false;
        fields.EnableHeadersVisualStyles = false;
        fields.ReadOnly = true;
        fields.AllowUserToAddRows = false;
        fields.AllowUserToDeleteRows = false;
        fields.AllowUserToResizeRows = false;
        fields.AllowUserToResizeColumns = false;
        fields.MultiSelect = true;
        fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        fields.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        fields.AccessibleName = "Podaci o računalu";
        fields.DefaultCellStyle = new()
        {
            BackColor = surface, ForeColor = ink, SelectionBackColor = raised, SelectionForeColor = ink,
            WrapMode = DataGridViewTriState.True, Padding = new(10, 6, 10, 6)
        };
        fields.ColumnHeadersDefaultCellStyle = new()
        {
            BackColor = surface, ForeColor = muted, SelectionBackColor = surface, SelectionForeColor = muted,
            Padding = new(10, 5, 10, 5)
        };
        fields.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        fields.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        fields.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Svojstvo", FillWeight = 37, SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = new() { ForeColor = muted }
        });
        fields.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Vrijednost", FillWeight = 63, SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        Controls.Add(fields, 1, 2);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        categories.ItemHeight = 37 * DeviceDpi / 96;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        categories.ItemHeight = 37 * DeviceDpi / 96;
    }

    private void DrawCategory(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? raised : surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var textArea = Rectangle.Inflate(e.Bounds, -11 * DeviceDpi / 96, 0);
        TextRenderer.DrawText(e.Graphics, categories.Items[e.Index].ToString(), Font, textArea,
            selected ? ink : muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    internal Task LoadAsync(CancellationToken token, bool refresh = false)
    {
        if (loadingTask != null) return loadingTask;
        if (sections != null && !refresh) return Task.CompletedTask;
        return loadingTask = LoadCoreAsync(token);
    }

    internal async Task<SpecSection[]> GetReportSectionsAsync(CancellationToken token)
    {
        await LoadAsync(token);
        token.ThrowIfCancellationRequested();
        return sections ?? [];
    }

    private async Task LoadCoreAsync(CancellationToken token)
    {
        reload.Enabled = false;
        status.Text = "Učitavanje specifikacija…";
        try
        {
            var result = await Task.Run(() => ComputerSpecs.Read(token), token);
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                ReadAt = DateTimeOffset.Now;
                SetSections(result);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            Diagnostics.Log("Specifikacije: " + e.GetType().Name);
            if (!IsDisposed) status.Text = "Očitavanje nije uspjelo. Pokušaj osvježiti podatke.";
        }
        finally
        {
            loadingTask = null;
            if (!IsDisposed) reload.Enabled = true;
        }
    }

    private void SetSections(SpecSection[] result)
    {
        int selected = Math.Max(0, categories.SelectedIndex);
        categories.BeginUpdate();
        try
        {
            sections = result;
            categories.Items.Clear();
            categories.Items.AddRange(result.Select(section => section.Name).ToArray());
            categories.SelectedIndex = Math.Min(selected, result.Length - 1);
        }
        finally { categories.EndUpdate(); }
    }

    private void ShowSection()
    {
        if (sections == null || categories.SelectedIndex < 0) return;
        var section = sections[categories.SelectedIndex];
        heading.Text = section.Name;
        status.Text = section.Incomplete ? "Dio podataka nije dostupan." :
            section.Groups.Count == 0 ? "Windows nije prijavio uređaje u ovoj kategoriji." : "Podaci s ovog računala";
        fields.Rows.Clear();
        foreach (var group in section.Groups)
        {
            var row = fields.Rows[fields.Rows.Add("", group.Title)];
            row.DefaultCellStyle.BackColor = raised;
            row.DefaultCellStyle.Font = groupFont;
            foreach (var field in group.Fields)
            {
                var entry = fields.Rows[fields.Rows.Add(field.Label, field.Value ?? "—")];
                if (field.Value == null) entry.Cells[1].Style.ForeColor = muted;
            }
        }
        fields.ClearSelection();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) groupFont.Dispose();
    }
}
