using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MikroLab.App.Controls
{
    internal sealed class NativeRichTextBox : RichTextBox
    {
        private const int WmVScroll = 0x0115;
        private const int WmMouseWheel = 0x020A;
        private const int WmKeyDown = 0x0100;

        public event Action Scrolled;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmVScroll || m.Msg == WmMouseWheel || m.Msg == WmKeyDown)
            {
                Scrolled?.Invoke();
            }
        }
    }

    /// <summary>
    /// Line-number gutter with breakpoint dots and the current execution marker.
    /// </summary>
    internal sealed class LineGutter : Control
    {
        private NativeRichTextBox _editor;
        private readonly HashSet<int> _breakpoints = new HashSet<int>();
        private int _executionLine = -1;
        private int _errorLine = -1;

        public event Action<int> LineClicked;

        public LineGutter()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Width = 56;
            Dock = DockStyle.Left;
            BackColor = AppTheme.GutterBackground;
            Cursor = Cursors.Hand;
        }

        public NativeRichTextBox Editor
        {
            get => _editor;
            set
            {
                _editor = value;
                Invalidate();
            }
        }

        public void SetBreakpoints(IEnumerable<int> lines)
        {
            _breakpoints.Clear();
            if (lines != null)
            {
                foreach (int line in lines) _breakpoints.Add(line);
            }
            Invalidate();
        }

        public void SetExecutionLine(int line)
        {
            if (_executionLine == line) return;
            _executionLine = line;
            Invalidate();
        }

        public void SetErrorLine(int line)
        {
            if (_errorLine == line) return;
            _errorLine = line;
            Invalidate();
        }

        private int FirstVisibleLine()
        {
            if (_editor == null || _editor.TextLength == 0) return 0;
            Point probe = new Point(1, 1);
            int index = _editor.GetCharIndexFromPosition(probe);
            return Math.Max(0, _editor.GetLineFromCharIndex(index));
        }

        private int LineHeight()
        {
            return _editor != null ? Math.Max(16, _editor.Font.Height + 2) : 18;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.Clear(AppTheme.GutterBackground);

            if (_editor == null) return;

            int first = FirstVisibleLine();
            int lineHeight = LineHeight();
            int visible = Math.Max(1, (Height / lineHeight) + 2);
            int top = _editor.GetPositionFromCharIndex(0).Y;
            int numberWidth = Width - 30;

            using (var divider = new Pen(AppTheme.BorderSoft))
            {
                graphics.DrawLine(divider, Width - 1, 0, Width - 1, Height);
            }

            for (int offset = 0; offset < visible; offset++)
            {
                int line = first + offset;
                if (line >= _editor.Lines.Length) break;

                int y = top + offset * lineHeight;
                if (y > Height) break;

                bool isBreakpoint = _breakpoints.Contains(line + 1);
                bool isExecution = _executionLine == line + 1;
                bool isError = _errorLine == line + 1;

                if (isExecution)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(45, 62, 94)))
                    {
                        graphics.FillRectangle(brush, 0, y - 1, Width - 2, lineHeight);
                    }
                }

                if (isBreakpoint)
                {
                    using (var brush = new SolidBrush(AppTheme.Red))
                    {
                        graphics.FillEllipse(brush, 8, y + lineHeight / 2 - 5, 10, 10);
                    }
                }
                else if (isExecution)
                {
                    using (var brush = new SolidBrush(AppTheme.Accent))
                    {
                        graphics.FillPolygon(brush, new[]
                        {
                            new Point(8, y + 4),
                            new Point(17, y + lineHeight / 2),
                            new Point(8, y + lineHeight - 4)
                        });
                    }
                }

                string number = (line + 1).ToString();
                TextRenderer.DrawText(
                    graphics,
                    number,
                    AppTheme.MonoSmall,
                    new Rectangle(22, y, Math.Max(10, numberWidth), lineHeight),
                    isExecution ? AppTheme.Text : (isError ? AppTheme.Red : AppTheme.TextDim),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_editor == null) return;

            int first = FirstVisibleLine();
            int lineHeight = LineHeight();
            int top = _editor.GetPositionFromCharIndex(0).Y;
            int line = first + Math.Max(0, (e.Y - top) / lineHeight) + 1;
            if (line >= 1 && line <= _editor.Lines.Length)
            {
                LineClicked?.Invoke(line);
            }
        }
    }

    /// <summary>
    /// Code editor used by Studio: syntax colouring, line numbers, breakpoint
    /// gutter and the execution marker. Deliberately built on RichTextBox so
    /// it remains predictable on every Windows version.
    /// </summary>
    internal sealed class CodeEditor : UserControl
    {
        private const int WmSetRedraw = 0x000B;

        private static readonly Regex CommentRegex = new Regex(@";.*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex StringRegex = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'", RegexOptions.Compiled);
        private static readonly Regex LabelRegex = new Regex(@"(?m)^[ \t]*[A-Za-z_][A-Za-z0-9_]*:", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex(@"\b(?:[0-9][0-9A-Fa-f]*[hH]|[0-9A-Fa-f]+[hH]|[0-9]+)\b", RegexOptions.Compiled);
        private static readonly Regex MnemonicRegex = new Regex(
            @"\b(?:NOP|HLT|EI|DI|MOV|MVI|LXI|LDA|STA|LHLD|SHLD|LDAX|STAX|XCHG|XTHL|SPHL|PCHL|" +
            @"ADD|ADC|SUB|SBB|ANA|XRA|ORA|CMP|ADI|ACI|SUI|SBI|ANI|XRI|ORI|CPI|" +
            @"INR|DCR|INX|DCX|DAD|DAA|RLC|RRC|RAL|RAR|CMA|CMC|STC|" +
            @"JMP|JNZ|JZ|JNC|JC|JPO|JPE|JP|JM|CALL|CNZ|CZ|CNC|CC|CPO|CPE|CP|CM|" +
            @"RET|RNZ|RZ|RNC|RC|RPO|RPE|RP|RM|RST|PUSH|POP|PCHL|XTHL|SPHL|IN|OUT)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RegisterRegex = new Regex(
            @"\b(?:A|B|C|D|E|H|L|M|SP|PSW|PC)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly TableLayoutPanel _layout;
        private readonly LineGutter _gutter;
        private readonly NativeRichTextBox _text;
        private readonly Timer _highlightTimer;
        private readonly ToolTip _toolTip;
        private int _executionLine = -1;
        private int _errorLine = -1;
        private bool _highlighting;

        public event Action CodeChanged;
        public event Action CaretMoved;
        public event Action<int> BreakpointToggled;

        public CodeEditor()
        {
            BackColor = AppTheme.EditorBackground;

            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.EditorBackground,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _gutter = new LineGutter { Dock = DockStyle.Fill };
            _gutter.LineClicked += line => BreakpointToggled?.Invoke(line);

            _text = new NativeRichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoFont,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                AcceptsTab = true,
                DetectUrls = false,
                HideSelection = false,
                ShortcutsEnabled = true,
                Margin = new Padding(0)
            };
            _text.SelectionColor = AppTheme.Text;
            _gutter.Editor = _text;

            _text.TextChanged += (sender, args) =>
            {
                _gutter.Invalidate();
                ScheduleHighlight();
                CodeChanged?.Invoke();
            };
            _text.SelectionChanged += (sender, args) => CaretMoved?.Invoke();
            _text.Scrolled += () => _gutter.Invalidate();
            _text.KeyDown += OnEditorKeyDown;
            _text.MouseDown += (sender, args) => CaretMoved?.Invoke();

            _highlightTimer = new Timer { Interval = 110 };
            _highlightTimer.Tick += (sender, args) =>
            {
                _highlightTimer.Stop();
                HighlightSyntax();
            };

            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_gutter, "Клик — точка останова. F9 — то же для текущей строки.");

            _layout.Controls.Add(_gutter, 0, 0);
            _layout.Controls.Add(_text, 1, 0);
            Controls.Add(_layout);
        }

        public string Code
        {
            get => _text.Text;
            set
            {
                if (_text.Text == value) return;
                _text.Text = value ?? string.Empty;
                _text.SelectionStart = 0;
                _text.SelectionLength = 0;
                HighlightSyntax();
            }
        }

        public int CaretLine => _text.GetLineFromCharIndex(_text.SelectionStart) + 1;
        public int CaretColumn
        {
            get
            {
                int index = _text.SelectionStart;
                int line = _text.GetLineFromCharIndex(index);
                int start = _text.GetFirstCharIndexFromLine(line);
                return Math.Max(1, index - start + 1);
            }
        }

        public int LineCount => _text.Lines.Length;

        public void FocusEditor()
        {
            _text.Focus();
        }

        public void SetBreakpointLines(IEnumerable<int> lines)
        {
            _gutter.SetBreakpoints(lines);
        }

        public void SetExecutionLine(int? line)
        {
            int value = line ?? -1;
            if (_executionLine == value) return;
            _executionLine = value;
            _gutter.SetExecutionLine(value);
        }

        public void SetErrorLine(int? line)
        {
            int value = line ?? -1;
            if (_errorLine == value) return;
            _errorLine = value;
            _gutter.SetErrorLine(value);
        }

        public void GoToLine(int line)
        {
            if (line < 1) line = 1;
            if (line > _text.Lines.Length) line = _text.Lines.Length;
            int index = _text.GetFirstCharIndexFromLine(line - 1);
            if (index < 0) return;
            _text.SelectionStart = index;
            _text.SelectionLength = 0;
            _text.ScrollToCaret();
            _text.Focus();
            CaretMoved?.Invoke();
        }

        public void SelectLine(int line)
        {
            GoToLine(line);
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab && !e.Shift && !e.Control && !e.Alt)
            {
                e.SuppressKeyPress = true;
                int selectionStart = _text.SelectionStart;
                _text.SelectedText = "    ";
                _text.SelectionStart = selectionStart + 4;
                _text.SelectionLength = 0;
                return;
            }

            if (e.KeyCode == Keys.Tab && e.Shift && !e.Control && !e.Alt)
            {
                e.SuppressKeyPress = true;
                RemoveIndentAtCaret();
            }
        }

        private void RemoveIndentAtCaret()
        {
            int line = _text.GetLineFromCharIndex(_text.SelectionStart);
            int start = _text.GetFirstCharIndexFromLine(line);
            if (start < 0) return;

            int remove = 0;
            while (remove < 4 && start + remove < _text.TextLength && _text.Text[start + remove] == ' ')
            {
                remove++;
            }
            if (remove == 0) return;

            int caret = _text.SelectionStart;
            _text.Select(start, remove);
            _text.SelectedText = string.Empty;
            _text.SelectionStart = Math.Max(start, caret - remove);
            _text.SelectionLength = 0;
        }

        private void ScheduleHighlight()
        {
            if (_highlighting) return;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        private void HighlightSyntax()
        {
            if (_highlighting || IsDisposed) return;

            _highlighting = true;
            int selectionStart = _text.SelectionStart;
            int selectionLength = _text.SelectionLength;
            SendMessage(_text.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);

            try
            {
                _text.SelectAll();
                _text.SelectionColor = AppTheme.Text;

                ApplyRegex(RegisterRegex, AppTheme.Violet);
                ApplyRegex(NumberRegex, AppTheme.Cyan);
                ApplyRegex(MnemonicRegex, AppTheme.Accent);
                ApplyRegex(LabelRegex, AppTheme.Amber);
                ApplyRegex(StringRegex, Color.FromArgb(226, 160, 120));
                ApplyRegex(CommentRegex, Color.FromArgb(116, 151, 121));

                _text.SelectionStart = Math.Min(selectionStart, _text.TextLength);
                _text.SelectionLength = Math.Min(selectionLength, _text.TextLength - _text.SelectionStart);
            }
            catch (ArgumentException)
            {
                // The control can be disposed while the timer is firing.
            }
            finally
            {
                SendMessage(_text.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                _text.Invalidate();
                _highlighting = false;
            }
        }

        private void ApplyRegex(Regex regex, Color color)
        {
            foreach (Match match in regex.Matches(_text.Text))
            {
                if (!match.Success || match.Length == 0) continue;
                _text.Select(match.Index, match.Length);
                _text.SelectionColor = color;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _highlightTimer?.Stop();
                _highlightTimer?.Dispose();
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
