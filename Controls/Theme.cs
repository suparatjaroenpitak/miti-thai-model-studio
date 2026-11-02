using System.Drawing.Drawing2D;

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace ThaiModelStudio.Controls;

internal static class Theme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr handle, string? applicationName, string? idList);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    public static void UseDarkControls(params Control[] controls)
    {
        foreach (var control in controls)
        {
            void Apply() { if (OperatingSystem.IsWindows()) _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            control.HandleCreated += (_, _) => Apply();
            if (control.IsHandleCreated) Apply();
        }
    }
    public static void UseDarkWindow(Form form)
    {
        void Apply()
        {
            if (!OperatingSystem.IsWindows()) return;
            int enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
        }
        form.HandleCreated += (_, _) => Apply();
        if (form.IsHandleCreated) Apply();
    }
    public static readonly Color Background = Color.FromArgb(13, 19, 30);
    public static readonly Color Panel = Color.FromArgb(20, 29, 44);
    public static readonly Color Raised = Color.FromArgb(34, 47, 66);
    public static readonly Color Border = Color.FromArgb(51, 67, 88);
    public static readonly Color Text = Color.FromArgb(237, 243, 249);
    public static readonly Color Muted = Color.FromArgb(153, 173, 194);
    public static readonly Color Accent = Color.FromArgb(91, 226, 186);
    public static Label Label(string text, float size = 10, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Text,
        Font = new Font("Leelawadee UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 5)
    };
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var b = new StudioButton(primary) { Text = text, AutoSize = true, Height = 36, MinimumSize = new Size(76, 36),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Raised,
            ForeColor = primary ? Background : Text, Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0), Margin = new Padding(4, 0, 4, 0),
            Font = new Font("Leelawadee UI", 10, primary ? FontStyle.Bold : FontStyle.Regular) };
        b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Border;
        b.Click += action; return b;
    }
}

internal sealed class StudioButton(bool primary) : Button
{
    private bool hovered;
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = new GraphicsPath();
        int radius = 9, d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        var fill = !Enabled ? Theme.Border : primary
            ? hovered ? Color.FromArgb(119, 242, 204) : Theme.Accent
            : hovered ? Color.FromArgb(46, 63, 84) : Theme.Raised;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (!primary)
        {
            using var border = new Pen(Theme.Border, 1);
            e.Graphics.DrawPath(border, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, !Enabled ? Theme.Muted : primary ? Theme.Background : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

internal sealed class StudioComboBox : ComboBox
{
    public StudioComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 25;
        BackColor = Theme.Raised; ForeColor = Theme.Text;
        Font = new Font("Leelawadee UI", 9.2f);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var highlighted = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(highlighted ? Color.FromArgb(49, 75, 91) : Theme.Raised);
        e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font,
            new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 10, e.Bounds.Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class StudioTabPage : Panel
{
    public StudioTabPage(string text) { Text = text; BackColor = Theme.Panel; ForeColor = Theme.Text; }
}

internal sealed class StudioTabs : UserControl
{
    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Panel, Cursor = Cursors.Hand };
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel };
    private int selectedIndex = -1;
    public Pages TabPages { get; }
    public event EventHandler? SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= TabPages.Count || selectedIndex == value) return;
            selectedIndex = value;
            for (int i = 0; i < TabPages.Count; i++) TabPages[i].Visible = i == value;
            TabPages[value].BringToFront(); header.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public StudioTabs()
    {
        TabPages = new Pages(this);
        Font = new Font("Leelawadee UI", 9.2f, FontStyle.Bold);
        BackColor = Theme.Panel;
        TabStop = true;
        Controls.Add(content); Controls.Add(header);
        header.Paint += (_, e) => PaintHeader(e.Graphics);
        header.MouseClick += (_, e) =>
        {
            if (TabPages.Count == 0) return;
            SelectedIndex = Math.Clamp(e.X * TabPages.Count / Math.Max(1, header.Width), 0, TabPages.Count - 1);
        };
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (TabPages.Count > 1 && keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SelectedIndex = (SelectedIndex + (keyData.HasFlag(Keys.Shift) ? -1 : 1) + TabPages.Count) % TabPages.Count;
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }
    private void PaintHeader(Graphics graphics)
    {
        graphics.Clear(Theme.Panel);
        if (TabPages.Count == 0) return;
        int width = Math.Max(1, header.Width / TabPages.Count);
        for (int i = 0; i < TabPages.Count; i++)
        {
            var bounds = new Rectangle(i * width + 2, 2, i == TabPages.Count - 1 ? header.Width - i * width - 4 : width - 4, header.Height - 4);
            bool selected = i == SelectedIndex;
            using var brush = new SolidBrush(selected ? Theme.Raised : Theme.Panel);
            graphics.FillRectangle(brush, bounds);
            TextRenderer.DrawText(graphics, TabPages[i].Text, Font, bounds, selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected)
            {
                using var pen = new Pen(Theme.Accent, 3);
                graphics.DrawLine(pen, bounds.Left + 10, bounds.Bottom - 1, bounds.Right - 10, bounds.Bottom - 1);
            }
        }
    }
    public sealed class Pages(StudioTabs owner) : Collection<StudioTabPage>
    {
        public void AddRange(IEnumerable<StudioTabPage> pages) { foreach (var page in pages) Add(page); }
        protected override void InsertItem(int index, StudioTabPage item)
        {
            base.InsertItem(index, item);
            item.Dock = DockStyle.Fill; item.BackColor = Theme.Panel; item.ForeColor = Theme.Text;
            owner.content.Controls.Add(item);
            if (owner.selectedIndex < 0) owner.SelectedIndex = 0;
            else item.Visible = false;
            owner.header.Invalidate();
        }
        protected override void ClearItems()
        {
            owner.content.Controls.Clear(); base.ClearItems();
            owner.selectedIndex = -1; owner.header.Invalidate();
        }
    }
}

internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkTable()) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
    private sealed class DarkTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Raised;
        public override Color MenuItemSelectedGradientBegin => Theme.Raised;
        public override Color MenuItemSelectedGradientEnd => Theme.Raised;
        public override Color MenuItemPressedGradientBegin => Theme.Raised;
        public override Color MenuItemPressedGradientEnd => Theme.Raised;
        public override Color MenuItemBorder => Theme.Border;
        public override Color MenuBorder => Theme.Border;
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color SeparatorDark => Theme.Border;
    }
}
