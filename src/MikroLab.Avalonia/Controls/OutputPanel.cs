using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Assembler;
using MikroLab.Presentation;

namespace MikroLab.Studio.Controls
{
    /// <summary>Coloured build/run log plus the assembler error list.</summary>
    internal sealed class OutputPanel : UserControl
    {
        private readonly LogCanvas _log;
        private readonly ScrollViewer _logScroll;
        private readonly ErrorList _errors;
        private TextBlock _errorCount;
        private readonly List<LogEntry> _entries = new List<LogEntry>();

        public OutputPanel()
        {
            var header = new Border
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                Height = 32,
                Child = BuildHeader()
            };

            _log = new LogCanvas(_entries);
            _logScroll = new ScrollViewer
            {
                Content = _log,
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            _errors = new ErrorList();
            _errors.ErrorActivated += line => ErrorActivated?.Invoke(line);

            var errorsScroll = new ScrollViewer
            {
                Content = _errors,
                Background = AppTheme.Brush(AppTheme.Surface),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            var split = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("70*,30*"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };
            split.Children.Add(_logScroll);
            Grid.SetColumn(_logScroll, 0);
            split.Children.Add(errorsScroll);
            Grid.SetColumn(errorsScroll, 1);

            var root = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("32,*"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };
            root.Children.Add(header);
            Grid.SetRow(header, 0);
            root.Children.Add(split);
            Grid.SetRow(split, 1);

            Content = root;
        }

        public event Action<int> ErrorActivated;

        private Control BuildHeader()
        {
            var grid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"),
                Margin = new Thickness(12, 0)
            };

            var title = AppTheme.Label("ЖУРНАЛ · СБОРКА · ОШИБКИ", AppTheme.TextDim, AppTheme.SmallSize, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            _errorCount = AppTheme.Label("Ошибок: 0", AppTheme.TextDim, AppTheme.SmallSize);
            _errorCount.VerticalAlignment = VerticalAlignment.Center;
            _errorCount.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(_errorCount);
            Grid.SetColumn(_errorCount, 1);

            var clear = new UiButton
            {
                Text = "Очистить",
                Kind = UiButtonKind.Ghost,
                Width = 78,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center
            };
            clear.Click += Clear;
            grid.Children.Add(clear);
            Grid.SetColumn(clear, 2);

            return grid;
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (message == null) return;

            Color color;
            switch (level)
            {
                case LogLevel.Success: color = AppTheme.Green; break;
                case LogLevel.Warning: color = AppTheme.Amber; break;
                case LogLevel.Error: color = AppTheme.Red; break;
                default: color = AppTheme.TextMuted; break;
            }

            _entries.Add(new LogEntry("[" + DateTime.Now.ToString("HH:mm:ss") + "]  " + message, color));
            if (_entries.Count > 2000)
            {
                _entries.RemoveRange(0, _entries.Count - 1200);
            }

            _log.InvalidateMeasure();
            _log.InvalidateVisual();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _logScroll.ScrollToEnd());
        }

        public void SetErrors(IReadOnlyList<AssemblerError> errors)
        {
            _errors.SetErrors(errors);
            int count = errors?.Count ?? 0;
            _errorCount.Text = "Ошибок: " + count;
            _errorCount.Foreground = AppTheme.Brush(count == 0 ? AppTheme.TextDim : AppTheme.Red);
        }

        public void Clear()
        {
            _entries.Clear();
            _log.InvalidateMeasure();
            _log.InvalidateVisual();
            SetErrors(Array.Empty<AssemblerError>());
        }

        private readonly record struct LogEntry(string Text, Color Color);

        private sealed class LogCanvas : Control
        {
            private const double LineHeight = 18;
            private const double Left = 10;

            private readonly List<LogEntry> _entries;
            private FormattedText _probe;

            public LogCanvas(List<LogEntry> entries)
            {
                _entries = entries;
                ClipToBounds = true;
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                _probe ??= AppTheme.MakeText("0", AppTheme.MonoFace, AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.TextMuted));
                double width = 0;
                foreach (LogEntry entry in _entries)
                {
                    FormattedText text = AppTheme.MakeText(entry.Text, AppTheme.MonoFace, AppTheme.MonoSmallSize, AppTheme.Brush(entry.Color));
                    width = Math.Max(width, text.Width);
                }
                return new Size(width + Left * 2, Math.Max(LineHeight, _entries.Count * LineHeight + 6));
            }

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.EditorBackground), null,
                    new Rect(0, 0, Bounds.Width, Bounds.Height));

                double y = 4;
                foreach (LogEntry entry in _entries)
                {
                    FormattedText text = AppTheme.MakeText(entry.Text, AppTheme.MonoFace, AppTheme.MonoSmallSize, AppTheme.Brush(entry.Color));
                    context.DrawText(text, new Point(Left, y));
                    y += LineHeight;
                }
            }
        }

        private sealed class ErrorList : Control
        {
            private const double RowHeight = 24;
            private readonly List<AssemblerError> _items = new List<AssemblerError>();
            private int _selected = -1;

            public ErrorList()
            {
                ClipToBounds = true;
            }

            public event Action<int> ErrorActivated;

            public void SetErrors(IReadOnlyList<AssemblerError> errors)
            {
                _items.Clear();
                if (errors != null) _items.AddRange(errors);
                _selected = -1;
                InvalidateMeasure();
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(0, Math.Max(RowHeight, _items.Count * RowHeight));
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                Point point = e.GetPosition(this);
                int index = (int)(point.Y / RowHeight);
                if (index >= 0 && index < _items.Count)
                {
                    _selected = index;
                    InvalidateVisual();

                    if (e.ClickCount >= 2)
                    {
                        ErrorActivated?.Invoke(_items[index].LineNumber);
                    }
                }
                base.OnPointerPressed(e);
            }

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null, new Rect(0, 0, Bounds.Width, Bounds.Height));

                for (int i = 0; i < _items.Count; i++)
                {
                    double y = i * RowHeight;
                    if (i == _selected)
                    {
                        context.DrawRectangle(AppTheme.Brush(AppTheme.AccentSoft), null,
                            new Rect(0, y, Bounds.Width, RowHeight));
                    }

                    string text = "Строка " + _items[i].LineNumber + " · " + _items[i].Message;
                    FormattedText formatted = AppTheme.MakeText(text, AppTheme.MonoFace, AppTheme.MonoSmallSize,
                        AppTheme.Brush(i == _selected ? AppTheme.Text : AppTheme.Red));
                    using (context.PushClip(new Rect(8, y, Math.Max(0, Bounds.Width - 8), RowHeight)))
                    {
                        context.DrawText(formatted, new Point(8, y + (RowHeight - formatted.Height) / 2));
                    }
                }
            }
        }
    }
}
