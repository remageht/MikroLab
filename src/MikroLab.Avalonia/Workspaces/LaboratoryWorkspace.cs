using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Machine;
using MikroLab.Presentation;
using MikroLab.Studio.Controls;
using ToggleSwitch = MikroLab.Studio.Controls.ToggleSwitch;

namespace MikroLab.Studio.Workspaces
{
    /// <summary>
    /// Guided lab bench: catalogue and theory on the left, a live board with
    /// indicators, lamps, tumblers, sensors and the matrix keyboard on the
    /// right. The same <see cref="MachineHost"/> drives both workspaces.
    /// </summary>
    internal sealed class LaboratoryWorkspace : UserControl
    {
        private readonly MachineHost _host;
        private readonly LabList _labs;
        private readonly TextBlock _description;
        private readonly TextBlock _labTitle;
        private readonly TextBlock _stateLabel;
        private readonly UiButton _run;
        private readonly UiButton _pause;
        private readonly UiButton _reset;
        private readonly UiButton _check;
        private readonly TextBlock _checkLabel;

        private readonly SevenSegmentDisplay _display;
        private readonly LedBar _lamps;
        private readonly ToggleSwitch[] _tumblers = new ToggleSwitch[3];
        private readonly VoltageSlider[] _sliders = new VoltageSlider[4];
        private readonly MatrixKeyboardControl _keyboard;
        private readonly TextBlock _portA;
        private readonly TextBlock _portB;
        private readonly TextBlock _adc;
        private readonly TextBlock _regs;
        private readonly TextBlock _flags;
        private bool _syncing;
        private bool _wasHalted;

        public LaboratoryWorkspace(MachineHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Background = AppTheme.Brush(AppTheme.Window);

            var root = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("370,*"),
                Margin = new Thickness(14, 12, 14, 12)
            };

            // ------------------------------------------------------------ left
            var leftLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("32,170,34,*,86,42"),
                Margin = new Thickness(14, 12, 14, 12)
            };

            var labsTitle = AppTheme.Label("ЛАБОРАТОРИИ", AppTheme.TextDim, AppTheme.SmallSize, true);
            labsTitle.VerticalAlignment = VerticalAlignment.Center;
            leftLayout.Children.Add(labsTitle);

            _labs = new LabList();
            foreach (LabDefinition lab in LabCatalog.All)
            {
                _labs.Add(lab);
            }
            _labs.SelectionChanged += ShowLab;
            var labsScroll = new ScrollViewer
            {
                Content = _labs,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            leftLayout.Children.Add(labsScroll);
            Grid.SetRow(labsScroll, 1);

            _labTitle = AppTheme.Label(string.Empty, AppTheme.Text, AppTheme.SmallSize, true);
            _labTitle.VerticalAlignment = VerticalAlignment.Center;
            _labTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            leftLayout.Children.Add(_labTitle);
            Grid.SetRow(_labTitle, 2);

            _description = new TextBlock
            {
                Foreground = AppTheme.Brush(AppTheme.TextMuted),
                FontFamily = AppTheme.UiFamily,
                FontSize = AppTheme.UiSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var descriptionScroll = new ScrollViewer
            {
                Content = _description,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            leftLayout.Children.Add(descriptionScroll);
            Grid.SetRow(descriptionScroll, 3);

            var checkPanel = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,104"),
                Margin = new Thickness(0, 6)
            };
            _checkLabel = AppTheme.Label(string.Empty, AppTheme.TextMuted, AppTheme.SmallSize);
            _checkLabel.TextWrapping = TextWrapping.Wrap;
            _checkLabel.VerticalAlignment = VerticalAlignment.Center;
            _checkLabel.Margin = new Thickness(0, 0, 8, 0);
            checkPanel.Children.Add(_checkLabel);

            _check = new UiButton
            {
                Text = "Проверить",
                Kind = UiButtonKind.Secondary,
                Height = 30,
                FontSize = AppTheme.SmallSize,
                VerticalAlignment = VerticalAlignment.Center
            };
            _check.Click += () => RunCheck(false);
            checkPanel.Children.Add(_check);
            Grid.SetColumn(_check, 1);
            leftLayout.Children.Add(checkPanel);
            Grid.SetRow(checkPanel, 4);

            var open = new UiButton
            {
                Text = "Открыть пример в студии →",
                Kind = UiButtonKind.Primary,
                Height = 36,
                FontSize = AppTheme.SmallSize
            };
            open.Click += () =>
            {
                LabDefinition lab = _labs.Selected;
                if (lab != null) OpenInStudioRequested?.Invoke(lab.SampleName);
            };
            leftLayout.Children.Add(open);
            Grid.SetRow(open, 5);

            var leftCard = AppTheme.Card(leftLayout, new Thickness(0), AppTheme.Surface);
            leftCard.Margin = new Thickness(0, 0, 14, 0);
            root.Children.Add(leftCard);

            // ----------------------------------------------------------- right
            var rightLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("52,*")
            };

            var boardHeader = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
                Background = AppTheme.Brush(AppTheme.SurfaceRaised)
            };

            var boardTitle = AppTheme.Label("УЧЕБНЫЙ СТЕНД", AppTheme.Text, AppTheme.SmallSize, true);
            boardTitle.VerticalAlignment = VerticalAlignment.Center;
            boardTitle.Margin = new Thickness(14, 0, 0, 0);
            boardHeader.Children.Add(boardTitle);

            var runFlow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            _run = new UiButton
            {
                Text = "Пуск",
                Kind = UiButtonKind.Primary,
                Width = 68,
                Height = 32,
                FontSize = AppTheme.SmallSize
            };
            _pause = new UiButton
            {
                Text = "Пауза",
                Kind = UiButtonKind.Secondary,
                Width = 68,
                Height = 32,
                FontSize = AppTheme.SmallSize
            };
            _reset = new UiButton
            {
                Text = "Сброс",
                Kind = UiButtonKind.Secondary,
                Width = 82,
                Height = 32,
                FontSize = AppTheme.SmallSize
            };
            _run.Click += () => _host.Run();
            _pause.Click += () => _host.Pause();
            _reset.Click += () =>
            {
                _host.Reset();
                _host.Session.WithMachine(machine => machine.Cpu.State.PC = _host.EntryPoint);
            };
            runFlow.Children.Add(_run);
            runFlow.Children.Add(_pause);
            runFlow.Children.Add(_reset);
            boardHeader.Children.Add(runFlow);
            Grid.SetColumn(runFlow, 2);

            _stateLabel = AppTheme.Label("ОСТАНОВЛЕНО", AppTheme.TextMuted, AppTheme.SmallSize, true);
            _stateLabel.VerticalAlignment = VerticalAlignment.Center;
            _stateLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _stateLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            _stateLabel.Margin = new Thickness(0, 0, 14, 0);
            boardHeader.Children.Add(_stateLabel);
            Grid.SetColumn(_stateLabel, 3);

            rightLayout.Children.Add(boardHeader);

            var board = new Grid
            {
                Margin = new Thickness(14),
                ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
                RowDefinitions = RowDefinitions.Parse("154,140,232,204,82"),
                RowSpacing = 10,
                ColumnSpacing = 10
            };

            _display = new SevenSegmentDisplay { Height = 110 };
            var indicatorsCard = MakeBoardCard("ИНДИКАТОРЫ · 8 РАЗРЯДОВ", _display);
            board.Children.Add(indicatorsCard);
            Grid.SetColumnSpan(indicatorsCard, 2);

            var ledsLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("*,22")
            };
            _lamps = new LedBar { Height = 58 };
            ledsLayout.Children.Add(_lamps);
            _portA = AppTheme.Label("00h", AppTheme.Text, AppTheme.MonoSmallSize, false, true);
            _portA.VerticalAlignment = VerticalAlignment.Center;
            ledsLayout.Children.Add(_portA);
            Grid.SetRow(_portA, 1);
            var ledsCard = MakeBoardCard("СВЕТОДИОДЫ · ПОРТ B (F9h)", ledsLayout);
            board.Children.Add(ledsCard);
            Grid.SetRow(ledsCard, 1);

            var statusLayout = new StackPanel { Spacing = 2 };
            _regs = AppTheme.Label("A=00  PC=8000", AppTheme.Text, AppTheme.MonoSmallSize, false, true);
            _regs.Height = 46;
            statusLayout.Children.Add(_regs);
            _flags = AppTheme.Label("C  Z  S  P  AC", AppTheme.TextMuted, AppTheme.MonoSmallSize, false, true);
            _flags.Height = 26;
            statusLayout.Children.Add(_flags);
            _portB = AppTheme.Label("A[FAh] 00h · C[F8h] 00h · УПР[FBh] 9Bh",
                AppTheme.TextMuted, AppTheme.MonoSmallSize, false, true);
            _portB.Height = 22;
            _portB.TextWrapping = TextWrapping.Wrap;
            statusLayout.Children.Add(_portB);
            var statusCard = MakeBoardCard("РЕГИСТРЫ И ФЛАГИ", statusLayout);
            board.Children.Add(statusCard);
            Grid.SetRow(statusCard, 1);
            Grid.SetColumn(statusCard, 1);

            var tumblersLayout = new StackPanel { Spacing = 2 };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _tumblers[i] = new ToggleSwitch
                {
                    Text = "SA" + (i + 1) + "   вход ВВ55",
                    Height = 32
                };
                _tumblers[i].CheckedChanged += value =>
                {
                    if (_syncing) return;
                    _host.WithMachine(machine =>
                    {
                        if (index == 0) machine.Ppi.Tumbler1 = value;
                        else if (index == 1) machine.Ppi.Tumbler2 = value;
                        else machine.Ppi.Tumbler3 = value;
                    });
                };
                tumblersLayout.Children.Add(_tumblers[i]);
            }
            var tumblersCard = MakeBoardCard("ПЕРЕКЛЮЧАТЕЛИ · SA1…SA3", tumblersLayout);
            board.Children.Add(tumblersCard);
            Grid.SetRow(tumblersCard, 2);

            var analogLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("*,22")
            };
            var sliders = new StackPanel { Spacing = 2 };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _sliders[i] = new VoltageSlider { Channel = i, Height = 40 };
                _sliders[i].ValueChanged += (channel, value) =>
                {
                    if (_syncing) return;
                    _host.WithMachine(machine => machine.Analog.SetChannel(index, value));
                };
                sliders.Children.Add(_sliders[i]);
            }
            analogLayout.Children.Add(sliders);
            _adc = AppTheme.Label("АЦП: 0 · канал 0", AppTheme.TextMuted, AppTheme.MonoSmallSize, false, true);
            _adc.VerticalAlignment = VerticalAlignment.Center;
            analogLayout.Children.Add(_adc);
            Grid.SetRow(_adc, 1);
            var analogCard = MakeBoardCard("АНАЛОГОВЫЕ ДАТЧИКИ · АЦП", analogLayout);
            board.Children.Add(analogCard);
            Grid.SetRow(analogCard, 2);
            Grid.SetColumn(analogCard, 1);

            _keyboard = new MatrixKeyboardControl();
            _keyboard.Attach(_host.Keyboard);
            _keyboard.KeyChanged += (row, column, pressed) =>
                _host.SetKeyState(row, column, pressed);
            var keyboardCard = MakeBoardCard("МАТРИЧНАЯ КЛАВИАТУРА · ПОРТ C (F8h)", _keyboard);
            board.Children.Add(keyboardCard);
            Grid.SetRow(keyboardCard, 3);
            Grid.SetColumnSpan(keyboardCard, 2);

            var hint = AppTheme.Label(
                "Выберите лабораторную работу слева, откройте пример в студии, " +
                "соберите его (F7) и запустите (F5). Устройства на этом стенде " +
                "обновляются в реальном времени; после HLT результат проверяется " +
                "автоматически.",
                AppTheme.TextMuted,
                AppTheme.SmallSize);
            hint.TextWrapping = TextWrapping.Wrap;
            var hintCard = MakeBoardCard("КАК ПОЛЬЗОВАТЬСЯ", hint);
            board.Children.Add(hintCard);
            Grid.SetRow(hintCard, 4);
            Grid.SetColumnSpan(hintCard, 2);

            var boardScroll = new ScrollViewer
            {
                Content = board,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = AppTheme.Brush(AppTheme.Surface)
            };
            rightLayout.Children.Add(boardScroll);
            Grid.SetRow(boardScroll, 1);

            var rightCard = AppTheme.Card(rightLayout, new Thickness(0), AppTheme.Surface);
            root.Children.Add(rightCard);
            Grid.SetColumn(rightCard, 1);

            Content = root;

            _labs.SelectedIndex = 0;
            UpdateButtons();
        }

        public event Action<string> OpenInStudioRequested;

        public void SelectLab(int index)
        {
            _labs.SelectedIndex = index;
        }

        private static Border MakeBoardCard(string title, Control content)
        {
            var card = new Border
            {
                Background = AppTheme.Brush(AppTheme.Surface),
                BorderBrush = AppTheme.Brush(AppTheme.BorderSoft),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 30, 12, 10),
                Margin = new Thickness(0, 0, 10, 10)
            };

            var label = AppTheme.Label(title, AppTheme.TextDim, AppTheme.SmallSize, true);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(12, -21, 0, 0);

            var host = new Grid();
            host.Children.Add(label);
            if (content != null) host.Children.Add(content);
            card.Child = host;
            return card;
        }

        private void ShowLab()
        {
            LabDefinition lab = _labs.Selected;
            if (lab == null) return;

            _labTitle.Text = lab.Number + " · " + lab.Title;
            _description.Text = lab.Summary + "\n\n" + lab.Description;
            _wasHalted = _host.State == RunState.Halted;
            UpdateCheckSummary(lab);
        }

        private void UpdateCheckSummary(LabDefinition lab)
        {
            if (lab == null || _checkLabel == null) return;

            LabExpectation expectation = lab.Expectation;
            if (expectation == null || expectation.IsEmpty)
            {
                _checkLabel.Foreground = AppTheme.Brush(AppTheme.TextMuted);
                _checkLabel.Text = "Эталон не задан · проверьте значения вручную";
                return;
            }

            var parts = new List<string>();
            if (expectation.A.HasValue) parts.Add("A=" + expectation.A.Value.ToString("X2") + "h");
            if (expectation.DE.HasValue) parts.Add("DE=" + expectation.DE.Value.ToString("X4") + "h");
            if (expectation.Memory.Count > 0)
            {
                parts.Add("память: " + expectation.Memory.Count + " ячеек");
            }

            _checkLabel.Foreground = AppTheme.Brush(AppTheme.TextDim);
            _checkLabel.Text = "Эталон · " + string.Join(" · ", parts);
        }

        private void RunCheck(bool automatic)
        {
            LabDefinition lab = _labs.Selected;
            if (lab == null) return;

            MachineSnapshot snapshot = _host.Snapshot();
            if (snapshot == null) return;

            LabCheckResult result = LabCheck.Evaluate(lab, snapshot);
            _checkLabel.Foreground = AppTheme.Brush(!result.IsDefined
                ? AppTheme.TextMuted
                : (result.Passed ? AppTheme.Green : AppTheme.Red));
            _checkLabel.Text = (automatic ? "Автопроверка · " : "Проверка · ") +
                               result.Summary + "\n" + string.Join("  ", result.Details);
        }

        public void UpdateFromSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;

            _syncing = true;
            try
            {
                _display.SetDigits(snapshot.Digits);
                _lamps.SetValue(snapshot.PpiEffectiveB);
                _portA.Text = "B[F9h] = " + snapshot.PpiEffectiveB.ToString("X2") + "h";
                _portB.Text = "A[FAh] " + snapshot.PpiEffectiveA.ToString("X2") +
                              "h · C[F8h] " + snapshot.PpiEffectiveC.ToString("X2") +
                              "h · УПР[FBh] " + snapshot.PpiControlWord.ToString("X2") +
                              "h · режим A" + (int)snapshot.PpiModeA + "/B" + (int)snapshot.PpiModeB;
                _tumblers[0].Checked = snapshot.Tumbler1;
                _tumblers[1].Checked = snapshot.Tumbler2;
                _tumblers[2].Checked = snapshot.Tumbler3;

                if (snapshot.AnalogVoltages != null)
                {
                    for (int i = 0; i < _sliders.Length && i < snapshot.AnalogVoltages.Length; i++)
                    {
                        _sliders[i].SetValueSilent(snapshot.AnalogVoltages[i]);
                    }
                }

                _adc.Text = "АЦП: " + snapshot.AdcValue + " · канал " + snapshot.SelectedAnalogChannel;
                _regs.Text = "A=" + snapshot.A.ToString("X2") + "  B=" + snapshot.B.ToString("X2") +
                             "  C=" + snapshot.C.ToString("X2") + "  D=" + snapshot.D.ToString("X2") +
                             "\nPC=" + snapshot.PC.ToString("X4") + "  SP=" + snapshot.SP.ToString("X4");
                _flags.Text = "C=" + OnOff(snapshot.Flags.C) + "  Z=" + OnOff(snapshot.Flags.Z) +
                              "  S=" + OnOff(snapshot.Flags.S) + "  P=" + OnOff(snapshot.Flags.P) +
                              "  AC=" + OnOff(snapshot.Flags.AC);
                _keyboard?.SyncFromModel();
            }
            finally
            {
                _syncing = false;
            }

            bool halted = snapshot.Halted || _host.State == RunState.Halted;
            if (halted && !_wasHalted)
            {
                RunCheck(true);
            }
            _wasHalted = halted;

            UpdateButtons();
        }

        private static string OnOff(bool value) => value ? "1" : "0";

        private void UpdateButtons()
        {
            bool running = _host.State == RunState.Running;
            bool halted = _host.State == RunState.Halted;
            bool loaded = _host.IsProgramLoaded;

            _run.IsEnabled = loaded && !running && !halted;
            _pause.IsEnabled = running;
            _reset.IsEnabled = loaded;

            string text;
            Color color;
            if (running)
            {
                text = "● ВЫПОЛНЯЕТСЯ";
                color = AppTheme.Green;
            }
            else if (halted)
            {
                text = "● HLT";
                color = AppTheme.Red;
            }
            else if (!loaded)
            {
                text = "● НЕ ЗАГРУЖЕНО";
                color = AppTheme.TextDim;
            }
            else if (_host.State == RunState.Paused)
            {
                text = "● ПАУЗА";
                string reason = _host.StopReason;
                if (!string.IsNullOrEmpty(reason)) text += " · " + reason;
                color = AppTheme.Amber;
            }
            else
            {
                text = "● ОСТАНОВЛЕНО";
                color = AppTheme.TextMuted;
            }

            _stateLabel.Text = text;
            _stateLabel.Foreground = AppTheme.Brush(color);
        }

        /// <summary>Owner-drawn lab catalogue with the number and title columns.</summary>
        private sealed class LabList : Control
        {
            private const double RowHeight = 30;
            private readonly List<LabDefinition> _items = new List<LabDefinition>();
            private int _selected = -1;
            private int _hover = -1;

            public LabList()
            {
                ClipToBounds = true;
                Focusable = true;
                Cursor = new Cursor(StandardCursorType.Hand);
            }

            public event Action SelectionChanged;

            public LabDefinition Selected =>
                _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

            public int SelectedIndex
            {
                get => _selected;
                set
                {
                    int index = value >= 0 && value < _items.Count ? value : -1;
                    if (_selected == index) return;
                    _selected = index;
                    InvalidateVisual();
                    SelectionChanged?.Invoke();
                }
            }

            public void Add(LabDefinition lab)
            {
                if (lab == null) return;
                _items.Add(lab);
                InvalidateMeasure();
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
                return new Size(width, Math.Max(RowHeight, _items.Count * RowHeight));
            }

            protected override void OnPointerMoved(PointerEventArgs e)
            {
                int row = (int)(e.GetPosition(this).Y / RowHeight);
                int hover = row >= 0 && row < _items.Count ? row : -1;
                if (hover != _hover)
                {
                    _hover = hover;
                    InvalidateVisual();
                }
                base.OnPointerMoved(e);
            }

            protected override void OnPointerExited(PointerEventArgs e)
            {
                _hover = -1;
                InvalidateVisual();
                base.OnPointerExited(e);
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

                int row = (int)(e.GetPosition(this).Y / RowHeight);
                if (row < 0 || row >= _items.Count) return;

                SelectedIndex = row;
                e.Handled = true;
            }

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                    new Rect(0, 0, Bounds.Width, Bounds.Height));

                for (int i = 0; i < _items.Count; i++)
                {
                    double y = i * RowHeight;
                    bool selected = i == _selected;
                    bool hover = i == _hover && !selected;

                    if (selected)
                    {
                        context.DrawRectangle(AppTheme.Brush(AppTheme.AccentSoft), null,
                            new Rect(0, y, Bounds.Width, RowHeight));
                        context.DrawRectangle(AppTheme.Brush(AppTheme.Accent), null,
                            new Rect(0, y + 4, 2, RowHeight - 8));
                    }
                    else if (hover)
                    {
                        context.DrawRectangle(AppTheme.Brush(AppTheme.SurfaceRaised), null,
                            new Rect(0, y, Bounds.Width, RowHeight));
                    }

                    LabDefinition lab = _items[i];
                    AppTheme.DrawText(context, lab.Number, new Point(12, y + 7),
                        AppTheme.SmallSize,
                        AppTheme.Brush(selected ? AppTheme.Accent : AppTheme.TextDim), true);

                    FormattedText title = AppTheme.MakeText(lab.Title, AppTheme.UiSize,
                        AppTheme.Brush(selected ? AppTheme.Text : AppTheme.TextMuted));
                    using (context.PushClip(new Rect(68, y, Math.Max(0, Bounds.Width - 74), RowHeight)))
                    {
                        context.DrawText(title, new Point(68, y + (RowHeight - title.Height) / 2));
                    }
                }
            }
        }
    }
}
