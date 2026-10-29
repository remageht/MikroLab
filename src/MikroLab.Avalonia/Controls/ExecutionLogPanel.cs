using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Debugging;

namespace MikroLab.Studio.Controls
{
    /// <summary>Execution log with selection details, CSV/Markdown export and follow mode.</summary>
    internal sealed class ExecutionLogPanel : UserControl
    {
        private readonly LogList _list;
        private readonly TextBlock _details;
        private readonly ToggleSwitch _follow;
        private readonly TextBlock _countLabel;
        private ExecutionLog _log;
        private long _revision;
        private DateTime _lastRefresh = DateTime.MinValue;

        public ExecutionLogPanel()
        {
            var grid = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("62,*,58"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };

            var header = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*"),
                Margin = new Thickness(12, 6, 12, 4)
            };

            var title = AppTheme.Label("ЖУРНАЛ ВЫПОЛНЕНИЯ", AppTheme.TextDim, AppTheme.SmallSize, true);
            header.Children.Add(title);

            _countLabel = AppTheme.Label("0 записей", AppTheme.TextMuted, AppTheme.SmallSize);
            _countLabel.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(_countLabel);
            Grid.SetRow(_countLabel, 0);

            var buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var clear = new UiButton
            {
                Text = "Очистить",
                Kind = UiButtonKind.Ghost,
                Width = 84,
                Height = 26,
                FontSize = AppTheme.SmallSize
            };
            clear.Click += () => ClearRequested?.Invoke();
            buttonBar.Children.Add(clear);

            var csv = new UiButton
            {
                Text = "CSV",
                Kind = UiButtonKind.Subtle,
                Width = 52,
                Height = 26,
                FontSize = AppTheme.SmallSize
            };
            csv.Click += () => ExportRequested?.Invoke(ExecutionLogFormat.Csv);
            buttonBar.Children.Add(csv);

            var md = new UiButton
            {
                Text = "MD",
                Kind = UiButtonKind.Subtle,
                Width = 52,
                Height = 26,
                FontSize = AppTheme.SmallSize
            };
            md.Click += () => ExportRequested?.Invoke(ExecutionLogFormat.Markdown);
            buttonBar.Children.Add(md);

            _follow = new ToggleSwitch
            {
                Text = "Следить",
                Checked = true,
                Width = 90,
                Height = 26
            };
            buttonBar.Children.Add(_follow);

            header.Children.Add(buttonBar);
            Grid.SetRow(buttonBar, 1);
            grid.Children.Add(header);

            _list = new LogList();
            _list.SelectionChanged += UpdateDetails;
            grid.Children.Add(_list);
            Grid.SetRow(_list, 1);

            _details = new TextBlock
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                Foreground = AppTheme.Brush(AppTheme.TextMuted),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSmallSize,
                Padding = new Thickness(10, 6),
                Text = "Выберите запись, чтобы увидеть регистры.",
                TextWrapping = TextWrapping.NoWrap
            };
            grid.Children.Add(_details);
            Grid.SetRow(_details, 2);

            Content = grid;
        }

        public event Action ClearRequested;
        public event Action<ExecutionLogFormat> ExportRequested;

        public void RefreshLog(ExecutionLog log, bool force = false)
        {
            if (log == null) return;
            _log = log;

            long currentRevision = log.Revision;
            if (!force && currentRevision == _revision) return;
            if (!force && (DateTime.UtcNow - _lastRefresh).TotalMilliseconds < 350) return;

            _revision = currentRevision;
            _lastRefresh = DateTime.UtcNow;

            ExecutionLogEntry[] entries = log.GetLast(300);
            _list.SetEntries(entries);
            _countLabel.Text = log.Count + " / " + log.Capacity + " записей";

            if (_follow.Checked && entries.Length > 0)
            {
                _list.SelectLast();
            }
            UpdateDetails();
        }

        private void UpdateDetails()
        {
            ExecutionLogEntry entry = _list.SelectedEntry;
            if (entry == null) return;

            _details.Text =
                entry.RegistersText + Environment.NewLine +
                "Флаги: " + entry.FlagsText + "   Такты: +" + entry.Cycles +
                " / всего " + entry.TotalCycles;
        }

        private sealed class LogList : Control
        {
            private const double RowHeight = 20;
            private readonly List<ExecutionLogEntry> _entries = new List<ExecutionLogEntry>();
            private int _selected = -1;
            private int _scrollRow;

            public LogList()
            {
                ClipToBounds = true;
                Focusable = true;
                Cursor = new Cursor(StandardCursorType.Arrow);
            }

            public event Action SelectionChanged;

            public ExecutionLogEntry SelectedEntry =>
                _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;

            public void SetEntries(ExecutionLogEntry[] entries)
            {
                ExecutionLogEntry selected = SelectedEntry;
                _entries.Clear();
                if (entries != null) _entries.AddRange(entries);
                _selected = selected != null ? _entries.IndexOf(selected) : -1;
                if (_selected < 0 && _entries.Count > 0) _selected = _entries.Count - 1;
                _scrollRow = Math.Max(0, Math.Min(_scrollRow, Math.Max(0, _entries.Count - VisibleRows)));
                InvalidateVisual();
            }

            public void SelectLast()
            {
                if (_entries.Count == 0) return;
                _selected = _entries.Count - 1;
                EnsureSelectedVisible();
                InvalidateVisual();
                SelectionChanged?.Invoke();
            }

            private int VisibleRows => Math.Max(1, (int)(Bounds.Height / RowHeight));

            private void EnsureSelectedVisible()
            {
                if (_selected < _scrollRow) _scrollRow = _selected;
                else if (_selected >= _scrollRow + VisibleRows) _scrollRow = _selected - VisibleRows + 1;
                _scrollRow = Math.Max(0, Math.Min(_scrollRow, Math.Max(0, _entries.Count - VisibleRows)));
            }

            protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            {
                _scrollRow -= (int)(e.Delta.Y * 3);
                _scrollRow = Math.Max(0, Math.Min(_scrollRow, Math.Max(0, _entries.Count - VisibleRows)));
                InvalidateVisual();
                e.Handled = true;
                base.OnPointerWheelChanged(e);
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

                Focus();
                int index = _scrollRow + (int)(e.GetPosition(this).Y / RowHeight);
                if (index >= 0 && index < _entries.Count)
                {
                    _selected = index;
                    InvalidateVisual();
                    SelectionChanged?.Invoke();
                }
                e.Handled = true;
            }

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                    new Rect(0, 0, Bounds.Width, Bounds.Height));

                int last = Math.Min(_entries.Count, _scrollRow + VisibleRows + 1);
                for (int i = _scrollRow; i < last; i++)
                {
                    double y = (i - _scrollRow) * RowHeight;
                    if (i == _selected)
                    {
                        context.DrawRectangle(AppTheme.Brush(AppTheme.Selection), null,
                            new Rect(0, y, Bounds.Width, RowHeight));
                    }

                    FormattedText text = AppTheme.MakeText(_entries[i].ShortText,
                        AppTheme.MonoSmallSize,
                        AppTheme.Brush(i == _selected ? AppTheme.Text : AppTheme.TextMuted));
                    using (context.PushClip(new Rect(6, y, Math.Max(0, Bounds.Width - 12), RowHeight)))
                    {
                        context.DrawText(text, new Point(6, y + (RowHeight - text.Height) / 2));
                    }
                }
            }
        }
    }
}
