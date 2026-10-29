using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Debugging;
using MikroLab.Core.Machine;
using MikroLab.Studio.Dialogs;

namespace MikroLab.Studio.Controls
{
    /// <summary>
    /// Register, flag and cycle inspector with the rotation animation at the
    /// bottom. Values can be edited by double-clicking a register; flags toggle
    /// on a single click.
    /// </summary>
    internal sealed class RegisterPanel : UserControl
    {
        private readonly RegisterCanvas _canvas;
        private readonly RotationView _rotation;
        private MachineSnapshot _snapshot;

        public RegisterPanel()
        {
            var grid = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("*,88"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };

            _canvas = new RegisterCanvas();
            _canvas.RegisterEdited += (name, value) => RegisterEdited?.Invoke(name, value);
            _canvas.FlagToggled += name => FlagToggled?.Invoke(name);
            grid.Children.Add(_canvas);

            _rotation = new RotationView();
            grid.Children.Add(_rotation);
            Grid.SetRow(_rotation, 1);

            Content = grid;
        }

        public event Action<string, ushort> RegisterEdited;
        public event Action<string> FlagToggled;

        public void UpdateSnapshot(MachineSnapshot snapshot, ExecutionLogEntry lastEntry = null)
        {
            if (_snapshot != null && lastEntry != null && snapshot != null)
            {
                _rotation.Observe(_snapshot, snapshot, lastEntry.Instruction);
            }

            _snapshot = snapshot;
            _canvas.Snapshot = snapshot;
            _canvas.InvalidateVisual();
        }

        private sealed class RegisterCanvas : Control
        {
            private readonly List<RegisterHit> _hits = new List<RegisterHit>();
            private readonly List<FlagHit> _flagHits = new List<FlagHit>();

            public RegisterCanvas()
            {
                ClipToBounds = true;
            }

            public MachineSnapshot Snapshot { get; set; }

            public event Action<string, ushort> RegisterEdited;
            public event Action<string> FlagToggled;

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                    new Rect(0, 0, Bounds.Width, Bounds.Height));

                _hits.Clear();
                _flagHits.Clear();

                if (Snapshot == null)
                {
                    AppTheme.DrawText(context, "Нет данных", new Point(16, 16),
                        AppTheme.UiSize, AppTheme.Brush(AppTheme.TextDim));
                    return;
                }

                string[] names = { "A", "B", "C", "D", "E", "H", "L", "M", "F" };
                ushort[] values =
                {
                    Snapshot.A, Snapshot.B, Snapshot.C,
                    Snapshot.D, Snapshot.E, Snapshot.H,
                    Snapshot.L, Snapshot.M, Snapshot.Flags.ToByte()
                };

                double top = 12;
                const double rowHeight = 25;
                double colWidth = Math.Max(150, Bounds.Width / 2 - 8);
                const double left = 14;

                AppTheme.DrawText(context, "РЕГИСТРЫ", new Point(left, top),
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim), true);
                FormattedText hint = AppTheme.MakeText("двойной клик — изменить",
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
                context.DrawText(hint, new Point(Bounds.Width - left - hint.Width, top));
                top += 26;

                context.DrawLine(AppTheme.Pen(AppTheme.BorderSoft),
                    new Point(left, top - 6), new Point(Bounds.Width - 14, top - 6));

                for (int i = 0; i < names.Length; i++)
                {
                    int column = i / 5;
                    int row = i % 5;
                    double x = left + column * colWidth;
                    double y = top + row * rowHeight;
                    bool highlighted = names[i] == "M" || names[i] == "F";
                    string text = values[i].ToString("X2") + "h";

                    AppTheme.DrawText(context, names[i], new Point(x, y),
                        AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextMuted));
                    AppTheme.DrawText(context, text, new Point(x + 26, y - 1),
                        AppTheme.MonoBoldSize,
                        AppTheme.Brush(highlighted ? AppTheme.Amber : AppTheme.Text), true, true);

                    _hits.Add(new RegisterHit(names[i], values[i], 2,
                        new Rect(x, y - 2, Math.Max(100, colWidth - 12), rowHeight)));
                }

                top += 5 * rowHeight + 12;
                context.DrawLine(AppTheme.Pen(AppTheme.BorderSoft),
                    new Point(left, top - 6), new Point(Bounds.Width - 14, top - 6));

                AppTheme.DrawText(context, "PC", new Point(left, top),
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextMuted));
                AppTheme.DrawText(context, Snapshot.PC.ToString("X4") + "h", new Point(left + 26, top - 1),
                    AppTheme.MonoBoldSize, AppTheme.Brush(AppTheme.Cyan), true, true);
                AppTheme.DrawText(context, "SP", new Point(left + colWidth, top),
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextMuted));
                AppTheme.DrawText(context, Snapshot.SP.ToString("X4") + "h",
                    new Point(left + colWidth + 26, top - 1),
                    AppTheme.MonoBoldSize, AppTheme.Brush(AppTheme.Cyan), true, true);

                _hits.Add(new RegisterHit("PC", Snapshot.PC, 4,
                    new Rect(left, top - 2, Math.Max(100, colWidth - 12), rowHeight)));
                _hits.Add(new RegisterHit("SP", Snapshot.SP, 4,
                    new Rect(left + colWidth, top - 2, Math.Max(100, colWidth - 12), rowHeight)));

                top += rowHeight + 10;
                AppTheme.DrawText(context, "ФЛАГИ", new Point(left, top),
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim), true);
                top += 24;

                DrawFlag(context, left, top, "C", Snapshot.Flags.C);
                DrawFlag(context, left + 52, top, "Z", Snapshot.Flags.Z);
                DrawFlag(context, left + 104, top, "S", Snapshot.Flags.S);
                DrawFlag(context, left + 156, top, "P", Snapshot.Flags.P);
                DrawFlag(context, left + 208, top, "AC", Snapshot.Flags.AC);

                top += 40;
                AppTheme.DrawText(context, "ТАКТЫ", new Point(left, top),
                    AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim), true);
                AppTheme.DrawText(context, Snapshot.Cycles.ToString("N0"),
                    new Point(left + 66, top - 1), AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.Text));
            }

            private void DrawFlag(DrawingContext context, double x, double y, string name, bool on)
            {
                var bounds = new Rect(x, y, 38, 26);
                AppTheme.FillRounded(context, bounds, 6,
                    AppTheme.Brush(on ? AppTheme.AccentSoft : AppTheme.SurfaceRaised));
                AppTheme.DrawRoundedBorder(context, bounds, 6,
                    AppTheme.Pen(on ? AppTheme.Accent : AppTheme.BorderSoft));

                FormattedText text = AppTheme.MakeText(name, AppTheme.SmallSize,
                    AppTheme.Brush(on ? AppTheme.Text : AppTheme.TextDim), true);
                context.DrawText(text, new Point(
                    bounds.X + (bounds.Width - text.Width) / 2,
                    bounds.Y + (bounds.Height - text.Height) / 2));

                _flagHits.Add(new FlagHit(name, bounds));
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                PointerPoint point = e.GetCurrentPoint(this);
                if (!point.Properties.IsLeftButtonPressed) return;

                Point position = e.GetPosition(this);
                if (e.ClickCount < 2)
                {
                    foreach (FlagHit hit in _flagHits)
                    {
                        if (hit.Bounds.Contains(position))
                        {
                            FlagToggled?.Invoke(hit.Name);
                            e.Handled = true;
                            return;
                        }
                    }
                    return;
                }

                foreach (RegisterHit hit in _hits)
                {
                    if (!hit.Bounds.Contains(position)) continue;
                    _ = EditAsync(hit);
                    e.Handled = true;
                    return;
                }
            }

            private async System.Threading.Tasks.Task EditAsync(RegisterHit hit)
            {
                Window owner = TopLevel.GetTopLevel(this) as Window;
                ushort? value = await RegisterEditDialog.ShowAsync(owner, hit.Name, hit.Value, hit.Digits);
                if (value.HasValue) RegisterEdited?.Invoke(hit.Name, value.Value);
            }
        }

        private readonly struct FlagHit
        {
            public string Name { get; }
            public Rect Bounds { get; }

            public FlagHit(string name, Rect bounds)
            {
                Name = name;
                Bounds = bounds;
            }
        }

        private readonly struct RegisterHit
        {
            public string Name { get; }
            public ushort Value { get; }
            public int Digits { get; }
            public Rect Bounds { get; }

            public RegisterHit(string name, ushort value, int digits, Rect bounds)
            {
                Name = name;
                Value = value;
                Digits = digits;
                Bounds = bounds;
            }
        }
    }

    /// <summary>Call stack with return addresses.</summary>
    internal sealed class CallStackPanel : UserControl
    {
        private readonly TextBlock _depth;
        private readonly ListBox _list;

        public CallStackPanel()
        {
            var grid = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("36,*"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };

            var header = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Margin = new Thickness(12, 0)
            };
            var title = AppTheme.Label("СТЕК ВЫЗОВОВ", AppTheme.TextDim, AppTheme.SmallSize, true);
            header.Children.Add(title);

            _depth = AppTheme.Label("Глубина: 0", AppTheme.TextMuted, AppTheme.SmallSize);
            _depth.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(_depth);
            Grid.SetColumn(_depth, 1);

            grid.Children.Add(header);

            _list = new ListBox
            {
                Background = AppTheme.Brush(AppTheme.Surface),
                Foreground = AppTheme.Brush(AppTheme.TextMuted),
                BorderThickness = new Thickness(0),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSmallSize
            };
            grid.Children.Add(_list);
            Grid.SetRow(_list, 1);

            Content = grid;
        }

        public void UpdateStack(MachineSnapshot snapshot, ushort[] stack)
        {
            int depth = snapshot != null ? snapshot.CallStackDepth : 0;
            _depth.Text = "Глубина: " + depth;

            var items = new List<string>();
            if (stack == null || stack.Length == 0)
            {
                items.Add("— стек пуст —");
            }
            else
            {
                for (int i = 0; i < stack.Length; i++)
                {
                    items.Add((i + 1) + ".  RET → " + stack[i].ToString("X4") + "h");
                }
            }

            _list.ItemsSource = null;
            _list.ItemsSource = items;
        }
    }
}
