namespace ThaiModelStudio.Controls;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 20, 29);
    public static readonly Color Panel = Color.FromArgb(22, 29, 41);
    public static readonly Color Raised = Color.FromArgb(30, 40, 55);
    public static readonly Color Border = Color.FromArgb(43, 55, 74);
    public static readonly Color Text = Color.FromArgb(225, 232, 243);
    public static readonly Color Muted = Color.FromArgb(143, 159, 183);
    public static readonly Color Accent = Color.FromArgb(104, 222, 177);
    public static Label Label(string text, float size = 10, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Text,
        Font = new Font("Leelawadee UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 5)
    };
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 36, MinimumSize = new Size(76, 36),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Raised,
            ForeColor = primary ? Background : Text, Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0), Margin = new Padding(4, 0, 4, 0),
            Font = new Font("Leelawadee UI", 10, primary ? FontStyle.Bold : FontStyle.Regular) };
        b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Border;
        b.Click += action; return b;
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
