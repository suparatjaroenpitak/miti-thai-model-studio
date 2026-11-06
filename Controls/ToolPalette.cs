namespace ThaiModelStudio.Controls;

/// <summary>A bilingual vertical tool switcher with one content page per tool group.</summary>
public sealed class ToolPage : Panel
{
    public ToolPage(string navText)
    {
        NavText = navText;
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Visible = false;
    }
    public string NavText { get; }
}

public sealed class ToolPalette : UserControl
{
    private readonly FlowLayoutPanel nav = new() { Dock = DockStyle.Left, Width = 104, BackColor = Theme.Panel,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(3, 6, 3, 6) };
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel };
    private readonly List<ToolPage> pages = [];
    private readonly List<Button> buttons = [];
    private int selectedIndex = -1;

    public ToolPalette()
    {
        BackColor = Theme.Panel;
        Controls.Add(content);
        Controls.Add(nav);
        nav.Controls.Add(new Label { Text = "TOOLS\nเครื่องมือ", AutoSize = false, Width = 92, Height = 44,
            ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Leelawadee UI", 8, FontStyle.Bold) });
    }

    public event EventHandler? SelectedIndexChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= pages.Count || selectedIndex == value) return;
            selectedIndex = value;
            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == value;
                PaintButton(buttons[i], i == value);
            }
            pages[value].BringToFront();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<ToolPage> Pages => pages;

    public ToolPage Add(string navText)
    {
        var page = new ToolPage(navText);
        pages.Add(page);
        content.Controls.Add(page);
        var button = new Button
        {
            Text = navText, Width = 92, Height = 62, Margin = new Padding(0, 2, 0, 2),
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Raised, ForeColor = Theme.Text,
            Font = new Font("Leelawadee UI", 8.5f), Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.AccessibleDescription = navText.Replace("\n", " · ");
        button.Click += (_, _) => SelectedIndex = pages.IndexOf(page);
        buttons.Add(button);
        nav.Controls.Add(button);
        if (selectedIndex < 0) SelectedIndex = 0;
        else page.Visible = false;
        return page;
    }

    private static void PaintButton(Button button, bool selected)
    {
        button.BackColor = selected ? Theme.Accent : Theme.Raised;
        button.ForeColor = selected ? Theme.Background : Theme.Text;
        button.FlatAppearance.BorderColor = selected ? Theme.Accent : Theme.Border;
    }
}
