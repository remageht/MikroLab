using System;
using System.Drawing;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Assembler;
using MikroLab.Core.Machine;

namespace MikroLab.App.Workspaces
{
    internal sealed class LaboratoryWorkspace : UserControl
    {
        private readonly MachineHost _host;
        private readonly ListBox _labs;
        private readonly RichTextBox _description;
        private readonly Label _labTitle;
        private readonly Label _stateLabel;
        private readonly UiButton _run;
        private readonly UiButton _pause;
        private readonly UiButton _reset;

        private readonly SevenSegmentDisplay _display;
        private readonly LedBar _lamps;
        private readonly ToggleSwitch[] _tumblers = new ToggleSwitch[3];
        private readonly VoltageSlider[] _sliders = new VoltageSlider[4];
        private readonly Label _portA;
        private readonly Label _portB;
        private readonly Label _adc;
        private readonly Label _regs;
        private readonly Label _flags;
        private bool _syncing;

        public event Action<string> OpenInStudioRequested;

        public LaboratoryWorkspace(MachineHost host)
        {
            _host = host;
            BackColor = AppTheme.Window;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Window,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var left = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = AppTheme.Surface,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0)
            };
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            Label labsTitle = AppTheme.Label("ЛАБОРАТОРИИ", AppTheme.TextDim, AppTheme.SmallBoldFont);
            labsTitle.Dock = DockStyle.Fill;
            labsTitle.TextAlign = ContentAlignment.MiddleLeft;

            _labs = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.UiFont,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30,
                IntegralHeight = false
            };
            foreach (LabDefinition lab in LabCatalog.All)
            {
                _labs.Items.Add(lab);
            }
            _labs.DrawItem += DrawLabItem;
            _labs.SelectedIndexChanged += (sender, args) => ShowLab();

            _labTitle = AppTheme.Label("", AppTheme.Text, AppTheme.SmallBoldFont);
            _labTitle.Dock = DockStyle.Fill;
            _labTitle.AutoSize = false;
            _labTitle.TextAlign = ContentAlignment.MiddleLeft;

            _description = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.UiFont,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false,
                HideSelection = false
            };

            var open = new UiButton
            {
                Text = "Открыть пример в студии →",
                Kind = UiButtonKind.Primary,
                Dock = DockStyle.Fill,
                Height = 36,
                Font = AppTheme.SmallFont
            };
            open.Click += (sender, args) =>
            {
                if (_labs.SelectedItem is LabDefinition lab)
                {
                    OpenInStudioRequested?.Invoke(lab.SampleName);
                }
            };

            leftLayout.Controls.Add(labsTitle, 0, 0);
            leftLayout.Controls.Add(_labs, 0, 1);
            leftLayout.Controls.Add(_labTitle, 0, 2);
            leftLayout.Controls.Add(_description, 0, 3);
            leftLayout.Controls.Add(open, 0, 4);
            left.Controls.Add(leftLayout);

            var right = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var boardHeader = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.SurfaceRaised,
                Padding = new Padding(14, 0, 14, 0)
            };
            Label boardTitle = AppTheme.Label("УЧЕБНЫЙ СТЕНД", AppTheme.Text, AppTheme.SmallBoldFont);
            boardTitle.Location = new Point(14, 17);

            _stateLabel = AppTheme.Label("ОСТАНОВЛЕНО", AppTheme.TextMuted, AppTheme.SmallBoldFont);
            _stateLabel.AutoSize = false;
            _stateLabel.TextAlign = ContentAlignment.MiddleRight;
            _stateLabel.Dock = DockStyle.Right;
            _stateLabel.Width = 158;

            var runFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 238,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = AppTheme.SurfaceRaised,
                Padding = new Padding(0, 8, 0, 0)
            };
            _run = new UiButton { Text = "Пуск", Kind = UiButtonKind.Primary, Width = 68, Height = 32, Font = AppTheme.SmallFont, Margin = new Padding(0, 0, 4, 0) };
            _pause = new UiButton { Text = "Пауза", Kind = UiButtonKind.Secondary, Width = 68, Height = 32, Font = AppTheme.SmallFont, Margin = new Padding(0, 0, 4, 0) };
            _reset = new UiButton { Text = "Сброс", Kind = UiButtonKind.Secondary, Width = 82, Height = 32, Font = AppTheme.SmallFont, Margin = new Padding(0) };
            _run.Click += (sender, args) => _host.Run();
            _pause.Click += (sender, args) => _host.Pause();
            _reset.Click += (sender, args) =>
            {
                _host.Reset();
                _host.Session.WithMachine(machine => machine.Cpu.State.PC = _host.EntryPoint);
            };
            runFlow.Controls.Add(_run);
            runFlow.Controls.Add(_pause);
            runFlow.Controls.Add(_reset);

            boardHeader.Controls.Add(boardTitle);
            boardHeader.Controls.Add(runFlow);
            boardHeader.Controls.Add(_stateLabel);

            var board = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface,
                AutoScroll = true,
                Padding = new Padding(14)
            };

            var boardLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            boardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            boardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            boardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            boardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            boardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 232));
            boardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

            var indicatorsCard = MakeBoardCard("ИНДИКАТОРЫ · 8 РАЗРЯДОВ");
            _display = new SevenSegmentDisplay { Dock = DockStyle.Fill, Height = 110 };
            indicatorsCard.Controls.Add(_display);
            boardLayout.Controls.Add(indicatorsCard, 0, 0);
            boardLayout.SetColumnSpan(indicatorsCard, 2);

            var ledsCard = MakeBoardCard("СВЕТОДИОДЫ · ПОРТ B (F9h)");
            _lamps = new LedBar { Dock = DockStyle.Fill, Height = 58 };
            _portA = AppTheme.Label("00h", AppTheme.Text, AppTheme.MonoSmall);
            _portA.Dock = DockStyle.Bottom;
            _portA.Height = 22;
            _portA.TextAlign = ContentAlignment.MiddleLeft;
            ledsCard.Controls.Add(_lamps);
            ledsCard.Controls.Add(_portA);
            boardLayout.Controls.Add(ledsCard, 0, 1);

            var statusCard = MakeBoardCard("РЕГИСТРЫ И ФЛАГИ");
            _regs = AppTheme.Label("A=00  PC=8000", AppTheme.Text, AppTheme.MonoSmall);
            _regs.Dock = DockStyle.Top;
            _regs.AutoSize = false;
            _regs.Height = 46;
            _regs.ForeColor = AppTheme.Text;
            _flags = AppTheme.Label("C  Z  S  P  AC", AppTheme.TextMuted, AppTheme.MonoSmall);
            _flags.Dock = DockStyle.Top;
            _flags.AutoSize = false;
            _flags.Height = 26;
            _flags.ForeColor = AppTheme.TextMuted;
            _portB = AppTheme.Label("A[FAh] 00h · C[F8h] 00h · УПР[FBh] 9Bh", AppTheme.TextMuted, AppTheme.MonoSmall);
            _portB.Dock = DockStyle.Top;
            _portB.AutoSize = false;
            _portB.Height = 22;
            _portB.ForeColor = AppTheme.TextMuted;
            statusCard.Controls.Add(_portB);
            statusCard.Controls.Add(_flags);
            statusCard.Controls.Add(_regs);
            boardLayout.Controls.Add(statusCard, 1, 1);

            var tumblersCard = MakeBoardCard("ПЕРЕКЛЮЧАТЕЛИ · SA1…SA3");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _tumblers[i] = new ToggleSwitch
                {
                    Dock = DockStyle.Top,
                    Text = "SA" + (i + 1) + "   вход ВВ55",
                    Height = 32,
                    Margin = new Padding(0)
                };
                _tumblers[i].CheckedChanged += (sender, args) =>
                {
                    if (_syncing) return;
                    _host.WithMachine(machine =>
                    {
                        if (index == 0) machine.Ppi.Tumbler1 = _tumblers[index].Checked;
                        else if (index == 1) machine.Ppi.Tumbler2 = _tumblers[index].Checked;
                        else machine.Ppi.Tumbler3 = _tumblers[index].Checked;
                    });
                };
            }
            tumblersCard.Controls.Add(_tumblers[2]);
            tumblersCard.Controls.Add(_tumblers[1]);
            tumblersCard.Controls.Add(_tumblers[0]);
            boardLayout.Controls.Add(tumblersCard, 0, 2);

            var analogCard = MakeBoardCard("АНАЛОГОВЫЕ ДАТЧИКИ · АЦП");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _sliders[i] = new VoltageSlider { Dock = DockStyle.Top, Channel = i, Height = 40 };
                _sliders[i].ValueChanged += (channel, value) =>
                {
                    if (_syncing) return;
                    _host.WithMachine(machine => machine.Analog.SetChannel(index, value));
                };
            }
            analogCard.Controls.Add(_sliders[3]);
            analogCard.Controls.Add(_sliders[2]);
            analogCard.Controls.Add(_sliders[1]);
            analogCard.Controls.Add(_sliders[0]);
            _adc = AppTheme.Label("АЦП: 0 · канал 0", AppTheme.TextMuted, AppTheme.MonoSmall);
            _adc.Dock = DockStyle.Bottom;
            _adc.Height = 22;
            _adc.TextAlign = ContentAlignment.MiddleLeft;
            analogCard.Controls.Add(_adc);
            boardLayout.Controls.Add(analogCard, 1, 2);

            var hintCard = MakeBoardCard("КАК ПОЛЬЗОВАТЬСЯ");
            Label hint = AppTheme.Label(
                "Выберите лабораторную работу слева, откройте пример в студии, " +
                "соберите его (F7) и запустите (F5). Устройства на этом стенде " +
                "обновляются в реальном времени.",
                AppTheme.TextMuted,
                AppTheme.SmallFont);
            hint.Dock = DockStyle.Fill;
            hint.AutoSize = false;
            hintCard.Controls.Add(hint);
            boardLayout.Controls.Add(hintCard, 0, 3);
            boardLayout.SetColumnSpan(hintCard, 2);

            board.Controls.Add(boardLayout);
            rightLayout.Controls.Add(boardHeader, 0, 0);
            rightLayout.Controls.Add(board, 0, 1);
            right.Controls.Add(rightLayout);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            Controls.Add(root);

            _labs.SelectedIndex = 0;
            UpdateButtons();
        }

        public void SelectLab(int index)
        {
            if (index >= 0 && index < _labs.Items.Count)
            {
                _labs.SelectedIndex = index;
            }
        }

        private static CardPanel MakeBoardCard(string title)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 10),
                Padding = new Padding(12, 30, 12, 10)
            };
            var label = new Label
            {
                Text = title,
                ForeColor = AppTheme.TextDim,
                Font = AppTheme.SmallBoldFont,
                AutoSize = true,
                Location = new Point(12, 9),
                BackColor = Color.Transparent
            };
            card.Controls.Add(label);
            return card;
        }

        private void DrawLabItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var brush = new SolidBrush(selected ? AppTheme.AccentSoft : AppTheme.Surface))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (selected)
            {
                using (var pen = new Pen(AppTheme.Accent, 2))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top + 4, e.Bounds.Left, e.Bounds.Bottom - 4);
                }
            }

            var lab = (LabDefinition)_labs.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, lab.Number, AppTheme.SmallBoldFont,
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 5, 56, 20), selected ? AppTheme.Accent : AppTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, lab.Title, AppTheme.UiFont,
                new Rectangle(e.Bounds.X + 68, e.Bounds.Y + 3, e.Bounds.Width - 74, 24), selected ? AppTheme.Text : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void ShowLab()
        {
            if (!(_labs.SelectedItem is LabDefinition lab)) return;

            _labTitle.Text = lab.Number + " · " + lab.Title;
            _description.Text = lab.Summary + "\n\n" + lab.Description;
            _description.SelectionStart = 0;
            _description.SelectionLength = 0;
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
            }
            finally
            {
                _syncing = false;
            }

            UpdateButtons();
        }

        private static string OnOff(bool value) => value ? "1" : "0";

        private void UpdateButtons()
        {
            bool running = _host.State == RunState.Running;
            bool halted = _host.State == RunState.Halted;
            bool loaded = _host.IsProgramLoaded;

            _run.Enabled = loaded && !running && !halted;
            _pause.Enabled = running;
            _reset.Enabled = loaded;

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
                color = AppTheme.Amber;
            }
            else
            {
                text = "● ОСТАНОВЛЕНО";
                color = AppTheme.TextMuted;
            }

            _stateLabel.Text = text;
            _stateLabel.ForeColor = color;
        }
    }
}
