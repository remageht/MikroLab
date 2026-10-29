using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace MikroLab.Studio.Controls
{
    /// <summary>
    /// Code editor with line numbers, breakpoints, the execution marker and
    /// assembly syntax colouring. Avalonia has no RichTextBox, so the editor
    /// paints its own text and implements caret, selection, scrolling,
    /// clipboard and undo. The command set matches the WinForms build.
    /// </summary>
    internal sealed class CodeEditor : Control
    {
        private const double GutterWidth = 56;
        private const double TextLeft = GutterWidth + 12;
        private const double LineHeight = 21;
        private const double TopPadding = 6;
        private const double RightPadding = 14;

        private static readonly Regex CommentRegex = new Regex(@";.*$", RegexOptions.Compiled);
        private static readonly Regex StringRegex = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'", RegexOptions.Compiled);
        private static readonly Regex LabelRegex = new Regex(@"^[ \t]*[A-Za-z_][A-Za-z0-9_]*:", RegexOptions.Compiled);
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

        private readonly List<string> _lines = new List<string>();
        private readonly List<int> _lineStarts = new List<int>();
        private readonly HashSet<int> _breakpoints = new HashSet<int>();
        private readonly List<EditSnapshot> _undo = new List<EditSnapshot>();
        private readonly List<EditSnapshot> _redo = new List<EditSnapshot>();
        private readonly DispatcherTimer _blinkTimer;

        private string _text = string.Empty;
        private int _caret;
        private int _selectionAnchor = -1;
        private int _scrollLine;
        private double _scrollX;
        private int _executionLine = -1;
        private int _errorLine = -1;
        private bool _caretVisible = true;
        private bool _dragging;
        private double _charWidth = 8;

        public CodeEditor()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Ibeam);

            AddHandler(TextInputEvent, OnTextInputHandler, Avalonia.Interactivity.RoutingStrategies.Bubble);
            AddHandler(GotFocusEvent, (sender, args) =>
            {
                _caretVisible = true;
                InvalidateVisual();
            });
            AddHandler(LostFocusEvent, (sender, args) => InvalidateVisual());

            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _blinkTimer.Tick += (sender, args) =>
            {
                if (!IsFocused) return;
                _caretVisible = !_caretVisible;
                InvalidateVisual();
            };
            _blinkTimer.Start();

            RebuildLines();
            MeasureCharacter();
        }

        public event Action CodeChanged;
        public event Action CaretMoved;
        public event Action<int> BreakpointToggled;

        private readonly record struct EditSnapshot(string Text, int Caret);

        public string Code
        {
            get => _text;
            set
            {
                string text = Normalize(value);
                if (_text == text) return;
                _text = text;
                _caret = 0;
                _selectionAnchor = -1;
                _scrollLine = 0;
                _scrollX = 0;
                _undo.Clear();
                _redo.Clear();
                RebuildLines();
                InvalidateVisual();
            }
        }

        public int CaretLine => LineOfIndex(_caret) + 1;

        public int CaretColumn
        {
            get
            {
                int line = LineOfIndex(_caret);
                return _caret - _lineStarts[line] + 1;
            }
        }

        public int LineCount => _lines.Count;

        public void FocusEditor()
        {
            Focus();
            _caretVisible = true;
            InvalidateVisual();
        }

        public void SetBreakpointLines(IEnumerable<int> lines)
        {
            _breakpoints.Clear();
            if (lines != null)
            {
                foreach (int line in lines) _breakpoints.Add(line);
            }
            InvalidateVisual();
        }

        public void SetExecutionLine(int? line)
        {
            int value = line ?? -1;
            if (_executionLine == value) return;
            _executionLine = value;
            InvalidateVisual();
        }

        public void SetErrorLine(int? line)
        {
            int value = line ?? -1;
            if (_errorLine == value) return;
            _errorLine = value;
            InvalidateVisual();
        }

        public void GoToLine(int line)
        {
            if (_lines.Count == 0) return;
            line = Math.Max(1, Math.Min(_lines.Count, line));
            SetCaret(_lineStarts[line - 1], false);
            EnsureCaretVisible();
            Focus();
            InvalidateVisual();
        }

        public void SelectLine(int line) => GoToLine(line);

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private void RebuildLines()
        {
            _lines.Clear();
            _lineStarts.Clear();

            int start = 0;
            for (int i = 0; i <= _text.Length; i++)
            {
                if (i != _text.Length && _text[i] != '\n') continue;
                _lineStarts.Add(start);
                _lines.Add(_text.Substring(start, i - start));
                start = i + 1;
            }

            if (_lines.Count == 0)
            {
                _lines.Add(string.Empty);
                _lineStarts.Add(0);
            }

            if (_caret > _text.Length) _caret = _text.Length;
            if (_selectionAnchor > _text.Length) _selectionAnchor = -1;
            ClampScroll();
        }

        private void MeasureCharacter()
        {
            FormattedText probe = AppTheme.MakeText("0000000000", AppTheme.MonoFace, AppTheme.MonoSize, AppTheme.Brush(AppTheme.Text));
            _charWidth = Math.Max(6, probe.Width / 10.0);
        }

        private int LineOfIndex(int index)
        {
            if (_lineStarts.Count == 0) return 0;
            int low = 0;
            int high = _lineStarts.Count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                int start = _lineStarts[middle];
                int end = middle + 1 < _lineStarts.Count ? _lineStarts[middle + 1] - 1 : _text.Length;
                if (index < start) high = middle - 1;
                else if (index > end) low = middle + 1;
                else return middle;
            }
            return Math.Max(0, Math.Min(_lineStarts.Count - 1, low));
        }

        private int VisibleLineCount => Math.Max(1, (int)((Bounds.Height - TopPadding) / LineHeight));

        private void ClampScroll()
        {
            int maxLine = Math.Max(0, _lines.Count - 1);
            _scrollLine = Math.Max(0, Math.Min(maxLine, _scrollLine));
            _scrollX = Math.Max(0, _scrollX);
        }

        // ------------------------------------------------------------------ editing

        private void PushUndo()
        {
            _undo.Add(new EditSnapshot(_text, _caret));
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void ReplaceSelection(string replacement, bool pushUndo = true)
        {
            if (pushUndo) PushUndo();

            int start = SelectionStart;
            int length = SelectionLength;
            _text = _text.Remove(start, length).Insert(start, replacement);
            _caret = start + replacement.Length;
            _selectionAnchor = -1;
            RebuildLines();
            EnsureCaretVisible();
            InvalidateVisual();
            CodeChanged?.Invoke();
            CaretMoved?.Invoke();
        }

        private int SelectionStart => _selectionAnchor < 0 ? _caret : Math.Min(_caret, _selectionAnchor);
        private int SelectionLength => _selectionAnchor < 0 ? 0 : Math.Abs(_caret - _selectionAnchor);

        private void SetCaret(int index, bool extend)
        {
            index = Math.Max(0, Math.Min(_text.Length, index));
            if (extend)
            {
                if (_selectionAnchor < 0) _selectionAnchor = _caret;
            }
            else
            {
                _selectionAnchor = -1;
            }

            if (_caret == index)
            {
                CaretMoved?.Invoke();
                return;
            }

            _caret = index;
            _caretVisible = true;
            EnsureCaretVisible();
            InvalidateVisual();
            CaretMoved?.Invoke();
        }

        private void MoveCaret(int delta, bool extend)
        {
            SetCaret(_caret + delta, extend);
        }

        private void EnsureCaretVisible()
        {
            int line = LineOfIndex(_caret);
            int visible = VisibleLineCount;
            if (line < _scrollLine) _scrollLine = line;
            else if (line >= _scrollLine + visible) _scrollLine = line - visible + 1;

            int column = _caret - _lineStarts[line];
            double x = column * _charWidth;
            double viewWidth = Math.Max(40, Bounds.Width - TextLeft - RightPadding);
            if (x - _scrollX < 0) _scrollX = Math.Max(0, x - 12);
            else if (x - _scrollX > viewWidth) _scrollX = x - viewWidth + 12;

            ClampScroll();
        }

        private void Backspace()
        {
            if (SelectionLength > 0)
            {
                ReplaceSelection(string.Empty);
                return;
            }
            if (_caret == 0) return;

            PushUndo();
            _text = _text.Remove(_caret - 1, 1);
            _caret--;
            _selectionAnchor = -1;
            RebuildLines();
            EnsureCaretVisible();
            InvalidateVisual();
            CodeChanged?.Invoke();
            CaretMoved?.Invoke();
        }

        private void DeleteForward()
        {
            if (SelectionLength > 0)
            {
                ReplaceSelection(string.Empty);
                return;
            }
            if (_caret >= _text.Length) return;

            PushUndo();
            _text = _text.Remove(_caret, 1);
            _selectionAnchor = -1;
            RebuildLines();
            InvalidateVisual();
            CodeChanged?.Invoke();
            CaretMoved?.Invoke();
        }

        private void RemoveIndent()
        {
            int line = LineOfIndex(_caret);
            int start = _lineStarts[line];
            int remove = 0;
            while (remove < 4 && start + remove < _text.Length && _text[start + remove] == ' ') remove++;
            if (remove == 0) return;

            PushUndo();
            _text = _text.Remove(start, remove);
            _caret = Math.Max(start, _caret - remove);
            _selectionAnchor = -1;
            RebuildLines();
            InvalidateVisual();
            CodeChanged?.Invoke();
            CaretMoved?.Invoke();
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(new EditSnapshot(_text, _caret));
            EditSnapshot snapshot = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            ApplySnapshot(snapshot);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(new EditSnapshot(_text, _caret));
            EditSnapshot snapshot = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            ApplySnapshot(snapshot);
        }

        private void ApplySnapshot(EditSnapshot snapshot)
        {
            _text = snapshot.Text;
            _caret = Math.Min(snapshot.Caret, _text.Length);
            _selectionAnchor = -1;
            RebuildLines();
            EnsureCaretVisible();
            InvalidateVisual();
            CodeChanged?.Invoke();
            CaretMoved?.Invoke();
        }

        private async void Copy()
        {
            if (SelectionLength == 0) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(_text.Substring(SelectionStart, SelectionLength)));
            await clipboard.SetDataAsync(data);
        }

        private async void Cut()
        {
            if (SelectionLength == 0) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(_text.Substring(SelectionStart, SelectionLength)));
            await clipboard.SetDataAsync(data);
            ReplaceSelection(string.Empty);
        }

        private async void Paste()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            using IAsyncDataTransfer transfer = await clipboard.TryGetDataAsync();
            if (transfer == null) return;

            string text = await transfer.TryGetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            ReplaceSelection(Normalize(text));
        }

        // ------------------------------------------------------------------ input

        private void OnTextInputHandler(object sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            string text = e.Text.Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (text.Length == 0) return;

            ReplaceSelection(text);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool control = (e.KeyModifiers & KeyModifiers.Control) != 0;
            if (OperatingSystem.IsMacOS())
            {
                // macOS users expect Command where Windows uses Control.
                control |= (e.KeyModifiers & KeyModifiers.Meta) != 0;
            }
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            if (control)
            {
                switch (e.Key)
                {
                    case Key.C: Copy(); e.Handled = true; return;
                    case Key.X: Cut(); e.Handled = true; return;
                    case Key.V: Paste(); e.Handled = true; return;
                    case Key.A:
                        _selectionAnchor = 0;
                        _caret = _text.Length;
                        _caretVisible = true;
                        EnsureCaretVisible();
                        InvalidateVisual();
                        CaretMoved?.Invoke();
                        e.Handled = true;
                        return;
                    case Key.Z:
                        if (shift) Redo(); else Undo();
                        e.Handled = true;
                        return;
                    case Key.Y: Redo(); e.Handled = true; return;
                    case Key.Home: SetCaret(0, shift); e.Handled = true; return;
                    case Key.End: SetCaret(_text.Length, shift); e.Handled = true; return;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.Left: MoveCaret(-1, shift); e.Handled = true; return;
                case Key.Right: MoveCaret(1, shift); e.Handled = true; return;
                case Key.Up: MoveVertical(-1, shift); e.Handled = true; return;
                case Key.Down: MoveVertical(1, shift); e.Handled = true; return;
                case Key.Home:
                    SetCaret(_lineStarts[LineOfIndex(_caret)], shift);
                    e.Handled = true;
                    return;
                case Key.End:
                    SetCaret(LineEnd(LineOfIndex(_caret)), shift);
                    e.Handled = true;
                    return;
                case Key.PageUp: MoveVertical(-VisibleLineCount, shift); e.Handled = true; return;
                case Key.PageDown: MoveVertical(VisibleLineCount, shift); e.Handled = true; return;
                case Key.Back: Backspace(); e.Handled = true; return;
                case Key.Delete: DeleteForward(); e.Handled = true; return;
                case Key.Enter: ReplaceSelection("\n"); e.Handled = true; return;
                case Key.Tab:
                    if (shift) RemoveIndent(); else ReplaceSelection("    ");
                    e.Handled = true;
                    return;
            }

            base.OnKeyDown(e);
        }

        private int LineEnd(int line)
        {
            if (line + 1 < _lineStarts.Count) return _lineStarts[line + 1] - 1;
            return _text.Length;
        }

        private void MoveVertical(int delta, bool extend)
        {
            int line = LineOfIndex(_caret);
            int column = _caret - _lineStarts[line];
            int target = Math.Max(0, Math.Min(_lines.Count - 1, line + delta));
            int index = Math.Min(_lineStarts[target] + column, LineEnd(target));
            SetCaret(index, extend);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            Focus();
            _caretVisible = true;
            Point position = e.GetPosition(this);

            if (position.X < GutterWidth)
            {
                int line = _scrollLine + (int)((position.Y - TopPadding) / LineHeight) + 1;
                if (line >= 1 && line <= _lines.Count)
                {
                    BreakpointToggled?.Invoke(line);
                }
                e.Handled = true;
                return;
            }

            int index = HitTestIndex(position);
            bool extend = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            SetCaret(index, extend);
            if (!extend) _selectionAnchor = _caret;
            _dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            base.OnPointerPressed(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging) return;
            SetCaret(HitTestIndex(e.GetPosition(this)), true);
            e.Handled = true;
            base.OnPointerMoved(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            if (SelectionLength == 0) _selectionAnchor = -1;
            e.Handled = true;
            base.OnPointerReleased(e);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _dragging = false;
            base.OnPointerCaptureLost(e);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                _scrollX = Math.Max(0, _scrollX - e.Delta.Y * _charWidth * 4);
            }
            else
            {
                _scrollLine -= (int)(e.Delta.Y * 3);
                ClampScroll();
            }

            InvalidateVisual();
            e.Handled = true;
            base.OnPointerWheelChanged(e);
        }

        private int HitTestIndex(Point position)
        {
            int line = _scrollLine + (int)((position.Y - TopPadding) / LineHeight);
            line = Math.Max(0, Math.Min(_lines.Count - 1, line));
            double localX = position.X - TextLeft + _scrollX;
            int column = (int)Math.Round(localX / _charWidth);
            column = Math.Max(0, Math.Min(_lines[line].Length, column));
            return _lineStarts[line] + column;
        }

        // ------------------------------------------------------------------ rendering

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;

            context.DrawRectangle(AppTheme.Brush(AppTheme.EditorBackground), null, new Rect(0, 0, width, height));
            context.DrawRectangle(AppTheme.Brush(AppTheme.GutterBackground), null, new Rect(0, 0, GutterWidth, height));
            context.DrawLine(AppTheme.Pen(AppTheme.BorderSoft), new Point(GutterWidth - 0.5, 0), new Point(GutterWidth - 0.5, height));

            int first = _scrollLine;
            int last = Math.Min(_lines.Count - 1, first + VisibleLineCount + 1);
            int selectionStart = SelectionStart;
            int selectionEnd = selectionStart + SelectionLength;

            using (context.PushClip(new Rect(0, 0, width, height)))
            {
                for (int line = first; line <= last; line++)
                {
                    double y = TopPadding + (line - first) * LineHeight;
                    bool execution = _executionLine == line + 1;
                    bool error = _errorLine == line + 1;
                    bool breakpoint = _breakpoints.Contains(line + 1);

                    if (execution)
                    {
                        context.DrawRectangle(AppTheme.Brush(Color.FromRgb(45, 62, 94)), null, new Rect(0, y - 1, width, LineHeight));
                    }
                    else if ((line & 1) == 1)
                    {
                        context.DrawRectangle(AppTheme.Brush(Color.FromRgb(22, 24, 30)), null, new Rect(GutterWidth, y - 1, width - GutterWidth, LineHeight));
                    }

                    if (breakpoint)
                    {
                        context.DrawEllipse(AppTheme.Brush(AppTheme.Red), null,
                            new Point(13, y + LineHeight / 2), 5, 5);
                    }
                    else if (execution)
                    {
                        DrawTriangle(context, AppTheme.Brush(AppTheme.Accent), y);
                    }

                    string number = (line + 1).ToString();
                    FormattedText numberText = AppTheme.MakeText(number, AppTheme.MonoFace, AppTheme.MonoSmallSize,
                        AppTheme.Brush(execution ? AppTheme.Text : error ? AppTheme.Red : AppTheme.TextDim));
                    context.DrawText(numberText, new Point(GutterWidth - 10 - numberText.Width, y + 1));

                    string text = _lines[line];
                    if (text.Length == 0) continue;

                    if (selectionEnd > selectionStart)
                    {
                        int lineStart = _lineStarts[line];
                        int lineEnd = lineStart + text.Length;
                        int from = Math.Max(selectionStart, lineStart);
                        int to = Math.Min(selectionEnd, lineEnd);
                        if (to > from)
                        {
                            double x = TextLeft + (from - lineStart) * _charWidth - _scrollX;
                            context.DrawRectangle(AppTheme.Brush(AppTheme.Selection), null,
                                new Rect(x, y - 1, (to - from) * _charWidth, LineHeight));
                        }
                    }

                    using (context.PushClip(new Rect(GutterWidth, 0, width - GutterWidth, height)))
                    {
                        DrawLineText(context, text, y);
                    }
                }

                if (IsFocused && _caretVisible)
                {
                    int line = LineOfIndex(_caret);
                    if (line >= first && line <= last)
                    {
                        int column = _caret - _lineStarts[line];
                        double x = TextLeft + column * _charWidth - _scrollX;
                        double y = TopPadding + (line - first) * LineHeight;
                        context.DrawRectangle(AppTheme.Brush(AppTheme.Text), null, new Rect(x, y + 2, 1.4, LineHeight - 5));
                    }
                }
            }

            DrawScrollbar(context, width, height);
        }

        private static void DrawTriangle(DrawingContext context, IBrush brush, double y)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(new Point(8, y + 4), true);
                geometryContext.LineTo(new Point(17, y + LineHeight / 2));
                geometryContext.LineTo(new Point(8, y + LineHeight - 4));
                geometryContext.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geometry);
        }

        private void DrawLineText(DrawingContext context, string text, double y)
        {
            Color[] colors = BuildColors(text);
            double x = TextLeft - _scrollX;
            int runStart = 0;

            for (int i = 1; i <= text.Length; i++)
            {
                if (i < text.Length && colors[i] == colors[runStart]) continue;
                string run = text.Substring(runStart, i - runStart);
                FormattedText formatted = AppTheme.MakeText(run, AppTheme.MonoFace, AppTheme.MonoSize, AppTheme.Brush(colors[runStart]));
                context.DrawText(formatted, new Point(x + runStart * _charWidth, y));
                runStart = i;
            }
        }

        private static Color[] BuildColors(string text)
        {
            var colors = new Color[text.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = AppTheme.Text;

            Apply(RegisterRegex, AppTheme.Violet);
            Apply(NumberRegex, AppTheme.Cyan);
            Apply(MnemonicRegex, AppTheme.Accent);
            Apply(LabelRegex, AppTheme.Amber);
            Apply(StringRegex, Color.FromRgb(226, 160, 120));
            Apply(CommentRegex, Color.FromRgb(116, 151, 121));

            return colors;

            void Apply(Regex regex, Color color)
            {
                foreach (Match match in regex.Matches(text))
                {
                    if (!match.Success || match.Length == 0) continue;
                    int end = Math.Min(colors.Length, match.Index + match.Length);
                    for (int i = match.Index; i < end; i++) colors[i] = color;
                }
            }
        }

        private void DrawScrollbar(DrawingContext context, double width, double height)
        {
            if (_lines.Count <= VisibleLineCount) return;

            double trackTop = 4;
            double trackHeight = height - 8;
            double thumbHeight = Math.Max(24, trackHeight * VisibleLineCount / _lines.Count);
            double maxLine = Math.Max(1, _lines.Count - VisibleLineCount);
            double thumbTop = trackTop + (trackHeight - thumbHeight) * (_scrollLine / maxLine);

            AppTheme.FillRounded(context, new Rect(width - 7, trackTop, 5, trackHeight), 2.5,
                AppTheme.Brush(Color.FromRgb(28, 31, 39)));
            AppTheme.FillRounded(context, new Rect(width - 7, thumbTop, 5, thumbHeight), 2.5,
                AppTheme.Brush(Color.FromRgb(60, 66, 80)));
        }
    }
}
