using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ThaiModelStudio.Controls;

public sealed class CodeEditor : UserControl
{
    private readonly RichTextBox editor;
    private readonly Panel gutter;
    private readonly System.Windows.Forms.Timer highlightTimer = new() { Interval = 350 };
    private bool formatting;
    private bool restoring;
    private string previousText = "";
    private readonly List<(string Text, int Caret)> undo = [];
    private readonly List<(string Text, int Caret)> redo = [];
    public event EventHandler? CodeChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Code
    {
        get => editor.Text;
        set { restoring = true; try { editor.Text = value; previousText = editor.Text; undo.Clear(); redo.Clear(); } finally { restoring = false; } Highlight(); }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsReadOnly { get => editor.ReadOnly; set => editor.ReadOnly = value; }
    public int CurrentLine => editor.GetLineFromCharIndex(editor.SelectionStart) + 1;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, ref Point l);

    public CodeEditor()
    {
        BackColor = Theme.Background;
        editor = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = Theme.Background, ForeColor = Theme.Text,
            Font = new Font("Consolas", 11F), AcceptsTab = true, WordWrap = false,
            DetectUrls = false, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Both,
            SelectionTabs = [36, 72, 108, 144, 180, 216, 252, 288] };
        gutter = new Panel { Dock = DockStyle.Left, Width = 48, BackColor = Theme.Background };
        Controls.Add(editor); Controls.Add(gutter);
        gutter.Paint += (_, e) => PaintGutter(e.Graphics);
        editor.VScroll += (_, _) => gutter.Invalidate();
        editor.Resize += (_, _) => gutter.Invalidate();
        editor.SelectionChanged += (_, _) => gutter.Invalidate();
        editor.TextChanged += (_, _) =>
        {
            if (formatting) return;
            if (!restoring && editor.Text != previousText)
            {
                undo.Add((previousText, Math.Clamp(editor.SelectionStart, 0, previousText.Length)));
                if (undo.Count > 100) undo.RemoveAt(0);
                redo.Clear();
            }
            previousText = editor.Text;
            gutter.Invalidate(); highlightTimer.Stop(); highlightTimer.Start(); CodeChanged?.Invoke(this, EventArgs.Empty);
        };
        editor.KeyDown += HandleKey;
        highlightTimer.Tick += (_, _) => { highlightTimer.Stop(); Highlight(); };
    }

    public void FocusCode() => editor.Focus();
    public void InsertSnippet(string text) { editor.SelectedText = text; editor.Focus(); }
    public void AppendSnippet(string text) { if (!string.IsNullOrWhiteSpace(editor.Text) && !editor.Text.EndsWith("\n")) editor.AppendText(Environment.NewLine); editor.AppendText(text); editor.Focus(); }
    private void PaintGutter(Graphics g)
    {
        var first = editor.GetLineFromCharIndex(editor.GetCharIndexFromPosition(new Point(0, 0)));
        var last = editor.GetLineFromCharIndex(editor.GetCharIndexFromPosition(new Point(0, editor.ClientSize.Height)));
        using var active = new SolidBrush(Theme.Accent); using var normal = new SolidBrush(Theme.Muted);
        for (var i = first; i <= Math.Min(last + 1, editor.Lines.Length - 1); i++)
        {
            var index = editor.GetFirstCharIndexFromLine(i); if (index < 0) continue;
            var p = editor.GetPositionFromCharIndex(index);
            var text = (i + 1).ToString(); var width = g.MeasureString(text, editor.Font).Width;
            g.DrawString(text, editor.Font, i + 1 == CurrentLine ? active : normal, 40 - width, p.Y);
        }
        using var pen = new Pen(Theme.Border); g.DrawLine(pen, 47, 0, 47, Height);
    }

    private void HandleKey(object? sender, KeyEventArgs e)
    {
        if (IsReadOnly) return;
        if (e.Control && e.KeyCode is Keys.Z or Keys.Y)
        {
            e.SuppressKeyPress = true;
            UndoRedo(e.KeyCode == Keys.Y || e.Shift); return;
        }
        if (e.KeyCode == Keys.Tab && !e.Control)
        { e.SuppressKeyPress = true; if (!e.Shift) editor.SelectedText = "    "; return; }
        if (e.KeyCode == Keys.Enter && !e.Control)
        {
            e.SuppressKeyPress = true;
            var lineStart = editor.GetFirstCharIndexFromLine(CurrentLine - 1);
            var before = editor.Text[lineStart..editor.SelectionStart];
            var indent = Regex.Match(before, @"^\s*").Value;
            editor.SelectedText = "\n" + indent + (before.TrimEnd().EndsWith(':') ? "    " : "");
        }
    }

    private void Highlight()
    {
        if (formatting || editor.TextLength > 150000 || editor.IsDisposed) return;
        formatting = true;
        var start = editor.SelectionStart; var length = editor.SelectionLength; var scroll = new Point();
        SendMessage(editor.Handle, 0x04DD, IntPtr.Zero, ref scroll);
        SendMessage(editor.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try
        {
            editor.SelectAll(); editor.SelectionColor = Theme.Text;
            var pattern = "(?<comment>#[^\\r\\n]*)|(?<str>\"\"\"[\\s\\S]*?\"\"\"|'''[\\s\\S]*?'''|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*')|(?<kw>\\b(?:import|from|as|for|in|if|else|elif|def|return|class|while|with|try|except|raise|True|False|None|and|or|not|pass|break|continue|range|print)\\b)|(?<number>\\b\\d+(?:\\.\\d+)?\\b)";
            foreach (Match match in Regex.Matches(editor.Text, pattern))
            {
                editor.Select(match.Index, match.Length);
                editor.SelectionColor = match.Groups["comment"].Success ? Color.FromArgb(111, 139, 123)
                    : match.Groups["str"].Success ? Color.FromArgb(221, 192, 133)
                    : match.Groups["kw"].Success ? Color.FromArgb(178, 151, 238) : Theme.Accent;
            }
            editor.Select(start, length); if (length == 0) editor.SelectionColor = Theme.Text;
            SendMessage(editor.Handle, 0x04DE, IntPtr.Zero, ref scroll);
        }
        finally
        {
            SendMessage(editor.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            formatting = false; editor.Invalidate(); gutter.Invalidate();
        }
    }
    private void UndoRedo(bool forward)
    {
        var source = forward ? redo : undo; var destination = forward ? undo : redo;
        if (source.Count == 0) return;
        destination.Add((editor.Text, editor.SelectionStart));
        var snapshot = source[^1]; source.RemoveAt(source.Count - 1);
        restoring = true;
        try { editor.Text = snapshot.Text; editor.SelectionStart = Math.Min(snapshot.Caret, editor.TextLength); }
        finally { restoring = false; }
        Highlight();
    }
    protected override void Dispose(bool disposing) { if (disposing) highlightTimer.Dispose(); base.Dispose(disposing); }
}
