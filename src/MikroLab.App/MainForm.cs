using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.App.Workspaces;
using MikroLab.Core.Machine;

namespace MikroLab.App
{
    /// <summary>
    /// Product shell: one window, two workspaces. Studio gives the full
    /// editor/debugger, Laboratory gives the guided lab bench. Both share the
    /// same <see cref="MachineHost"/>, so a program assembled in Studio keeps
    /// running when the user switches to the bench.
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        private enum Workspace
        {
            Studio,
            Laboratory
        }

        private readonly MachineHost _host;
        private readonly StudioWorkspace _studio;
        private readonly LaboratoryWorkspace _laboratory;
        private readonly Panel _content;
        private readonly Panel _header;
        private readonly Panel _switchBar;
        private readonly UiButton _studioTab;
        private readonly UiButton _labTab;
        private readonly UiButton _fileButton;
        private readonly ThemedDropDown _speedBox;
        private readonly Label _speedLabel;
        private readonly Timer _uiTimer;
        private readonly ToolTip _toolTips;

        private readonly Label _stateLabel;
        private readonly Label _cyclesLabel;
        private readonly Label _pcLabel;
        private readonly Label _breakpointLabel;
        private readonly Label _workspaceLabel;

        private Workspace _workspace = Workspace.Studio;
        private MachineSnapshot _snapshot;

        // Screenshot harness state.
        private Timer _captureTimer;
        private System.Threading.Timer _captureWatchdog;
        private string _captureDirectory;
        private int _captureStep;
        private int _captureSample;
        private string _captureWorkspace;

        public MainForm()
        {
            Text = "MikroLab — КР580ВМ80А";
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1420, 880);
            MinimumSize = new Size(1120, 720);
            DoubleBuffered = true;
            KeyPreview = true;

            _host = new MachineHost();
            _toolTips = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 450,
                ReshowDelay = 120
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = AppTheme.Window,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            // ------------------------------------------------------------------ header

            _header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0)
            };
            _header.Paint += (sender, args) =>
            {
                using (var pen = new Pen(AppTheme.Border))
                {
                    args.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
                }
            };

            var mark = new BrandMark
            {
                Size = new Size(42, 42),
                Location = new Point(18, 11),
                BackColor = Color.Transparent
            };

            var brand = new Label
            {
                Text = "MikroLab",
                Font = AppTheme.BrandFont,
                ForeColor = AppTheme.Text,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(70, 8)
            };
            var tagline = new Label
            {
                Text = "КР580ВМ80А · Intel 8080 · учебный эмулятор",
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(72, 34)
            };

            _studioTab = new UiButton
            {
                Text = "Студия",
                Kind = UiButtonKind.Ghost,
                Width = 128,
                Height = 32,
                Font = AppTheme.SmallFont,
                CornerRadius = 7,
                Margin = new Padding(0),
                ClearColor = AppTheme.SurfaceRaised
            };
            _labTab = new UiButton
            {
                Text = "Лаборатория",
                Kind = UiButtonKind.Ghost,
                Width = 128,
                Height = 32,
                Font = AppTheme.SmallFont,
                CornerRadius = 7,
                Margin = new Padding(0),
                ClearColor = AppTheme.SurfaceRaised
            };
            _studioTab.Click += (sender, args) => SwitchTo(Workspace.Studio);
            _labTab.Click += (sender, args) => SwitchTo(Workspace.Laboratory);

            _switchBar = new Panel
            {
                Width = 264,
                Height = 40,
                BackColor = AppTheme.Surface
            };
            _switchBar.Paint += (sender, args) =>
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, _switchBar.Width - 1, _switchBar.Height - 1);
                AppTheme.FillRounded(args.Graphics, bounds, 10, AppTheme.SurfaceRaised);
                AppTheme.DrawRoundedBorder(args.Graphics, bounds, 10, AppTheme.BorderSoft);
            };
            _studioTab.Location = new Point(4, 4);
            _labTab.Location = new Point(132, 4);
            _switchBar.Controls.Add(_studioTab);
            _switchBar.Controls.Add(_labTab);

            var about = new UiButton
            {
                Text = "Справка · F1",
                Kind = UiButtonKind.Ghost,
                Width = 104,
                Height = 32,
                Font = AppTheme.SmallFont,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            about.Click += (sender, args) => ShowHelp();
            _toolTips.SetToolTip(about, "Горячие клавиши, возможности и лицензия");

            _fileButton = new UiButton
            {
                Text = "Файл",
                Kind = UiButtonKind.Ghost,
                Width = 82,
                Height = 32,
                Font = AppTheme.SmallFont,
                Location = new Point(352, 16)
            };
            _fileButton.Click += (sender, args) => ShowFileMenu();
            _toolTips.SetToolTip(_fileButton, "Intel HEX, состояние машины и файлы программ");

            _speedLabel = AppTheme.Label("Скорость", AppTheme.TextDim, AppTheme.SmallFont);
            _speedLabel.AutoSize = true;
            _speedLabel.BackColor = Color.Transparent;

            _speedBox = new ThemedDropDown
            {
                Width = 148,
                Height = 30,
                Font = AppTheme.SmallFont
            };
            _speedBox.SetItems(SpeedLabels);
            _speedBox.SelectIndex(0);
            _speedBox.SelectionChanged += OnSpeedSelected;
            _speedBox.SetToolTip("Скорость выполнения: от оригинала до максимума");

            _header.Controls.Add(mark);
            _header.Controls.Add(brand);
            _header.Controls.Add(tagline);
            _header.Controls.Add(_fileButton);
            _header.Controls.Add(_switchBar);
            _header.Controls.Add(_speedLabel);
            _header.Controls.Add(_speedBox);
            _header.Controls.Add(about);
            _header.Resize += (sender, args) =>
            {
                _switchBar.Left = Math.Max(448, (_header.Width - _switchBar.Width) / 2);
                _switchBar.Top = 12;
                about.Location = new Point(_header.Width - about.Width - 18, 16);
                _speedBox.Location = new Point(about.Left - _speedBox.Width - 10, 17);
                _speedLabel.Location = new Point(_speedBox.Left - _speedLabel.Width - 8, 24);
            };

            // ------------------------------------------------------------------ workspaces

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Window,
                Margin = new Padding(0)
            };

            _studio = new StudioWorkspace(_host) { Dock = DockStyle.Fill };
            _laboratory = new LaboratoryWorkspace(_host) { Dock = DockStyle.Fill, Visible = false };
            _studio.DocumentTitleChanged += UpdateTitle;
            _laboratory.OpenInStudioRequested += OpenLabInStudio;

            _content.Controls.Add(_studio);
            _content.Controls.Add(_laboratory);

            // ------------------------------------------------------------------ status bar

            var status = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0)
            };
            status.Paint += (sender, args) =>
            {
                using (var pen = new Pen(AppTheme.BorderSoft))
                {
                    args.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
                }
            };

            var statusLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(14, 0, 14, 0)
            };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _stateLabel = MakeStatusLabel("● ОСТАНОВЛЕНО", AppTheme.TextMuted);
            _cyclesLabel = MakeStatusLabel("тактов: 0", AppTheme.TextMuted);
            _pcLabel = MakeStatusLabel("PC 8000h", AppTheme.Cyan);
            _breakpointLabel = MakeStatusLabel("точек останова: 0", AppTheme.TextMuted);
            _workspaceLabel = MakeStatusLabel("Студия", AppTheme.TextDim);
            _workspaceLabel.TextAlign = ContentAlignment.MiddleRight;
            _workspaceLabel.Dock = DockStyle.Fill;

            statusLayout.Controls.Add(_stateLabel, 0, 0);

            var separator = new Panel
            {
                Dock = DockStyle.Fill,
                Width = 1,
                BackColor = AppTheme.Border,
                Margin = new Padding(12, 9, 12, 9)
            };
            statusLayout.Controls.Add(separator, 1, 0);
            statusLayout.Controls.Add(_pcLabel, 2, 0);
            statusLayout.Controls.Add(_cyclesLabel, 3, 0);
            statusLayout.Controls.Add(_breakpointLabel, 4, 0);
            statusLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface }, 5, 0);
            statusLayout.Controls.Add(_workspaceLabel, 6, 0);
            status.Controls.Add(statusLayout);

            root.Controls.Add(_header, 0, 0);
            root.Controls.Add(_content, 0, 1);
            root.Controls.Add(status, 0, 2);
            Controls.Add(root);

            _uiTimer = new Timer { Interval = 50 };
            _uiTimer.Tick += (sender, args) => PumpUi();
            _uiTimer.Start();

            _host.Session.PublishSnapshot();
            SwitchTo(Workspace.Studio);
            UpdateTitle();
            PumpUi();
        }

        public StudioWorkspace Studio => _studio;
        public LaboratoryWorkspace Laboratory => _laboratory;

        // ================================================================== shell

        private static Label MakeStatusLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                Font = AppTheme.SmallFont,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
        }

        private void SwitchTo(Workspace workspace)
        {
            _workspace = workspace;
            bool studio = workspace == Workspace.Studio;

            _studio.Visible = studio;
            _laboratory.Visible = !studio;
            if (studio)
            {
                _studio.BringToFront();
            }
            else
            {
                _laboratory.BringToFront();
            }

            _studioTab.Active = studio;
            _labTab.Active = !studio;
            _studioTab.Invalidate();
            _labTab.Invalidate();
            _workspaceLabel.Text = studio ? "Студия" : "Лаборатория";

            PumpUi();
            if (studio)
            {
                _studio.Editor.FocusEditor();
            }
        }

        private void OpenLabInStudio(string sampleName)
        {
            SampleLibraryLoad(sampleName);
            SwitchTo(Workspace.Studio);
            _studio.Output.Log("Открыта лабораторная программа · " + sampleName);
        }

        private void SampleLibraryLoad(string sampleName)
        {
            for (int i = 0; i < SampleLibrary.All.Length; i++)
            {
                if (string.Equals(SampleLibrary.All[i].Name, sampleName, StringComparison.OrdinalIgnoreCase))
                {
                    _studio.HostLoadSample(i);
                    return;
                }
            }
            _studio.HostLoadSample(0);
        }

        private void PumpUi()
        {
            if (IsDisposed || Disposing) return;

            MachineSnapshot snapshot = _host.Snapshot();
            if (snapshot != null)
            {
                _snapshot = snapshot;
            }

            if (_snapshot != null)
            {
                if (_workspace == Workspace.Studio)
                {
                    _studio.UpdateFromSnapshot(_snapshot);
                }
                else
                {
                    _laboratory.UpdateFromSnapshot(_snapshot);
                }

                UpdateStatus(_snapshot);
            }
        }

        private void UpdateStatus(MachineSnapshot snapshot)
        {
            RunState state = _host.State;
            string label;
            Color color;
            switch (state)
            {
                case RunState.Running:
                    label = "● ВЫПОЛНЯЕТСЯ";
                    color = AppTheme.Green;
                    break;
                case RunState.Paused:
                    label = "● ПАУЗА";
                    color = AppTheme.Amber;
                    break;
                case RunState.Halted:
                    label = "● HLT";
                    color = AppTheme.Red;
                    break;
                default:
                    label = "● ОСТАНОВЛЕНО";
                    color = AppTheme.TextMuted;
                    break;
            }

            if (_stateLabel.Text != label) _stateLabel.Text = label;
            if (_stateLabel.ForeColor != color) _stateLabel.ForeColor = color;

            _pcLabel.Text = "PC " + snapshot.PC.ToString("X4", CultureInfo.InvariantCulture) + "h";
            _cyclesLabel.Text = "тактов: " + snapshot.Cycles.ToString("N0", CultureInfo.InvariantCulture);
            _breakpointLabel.Text = "точек останова: " + _host.ActiveBreakpointCount;
        }

        private void UpdateTitle()
        {
            string name = _studio.DocumentName;
            Text = "MikroLab — " + name;
        }

        private void RunToAddress()
        {
            using (var dialog = new AddressDialog("Выполнить до адреса", "Адрес останова (hex):", _host.CurrentPc()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                if (_workspace == Workspace.Studio)
                {
                    _studio.HostRunTo(dialog.Value);
                }
                else
                {
                    _host.RunTo(dialog.Value);
                }
                PumpUi();
            }
        }

        private void ShowDebugPoints()
        {
            if (_workspace != Workspace.Studio) SwitchTo(Workspace.Studio);
            _studio.HostShowDebugPoints();
            PumpUi();
        }

        private void StepBack()
        {
            if (_workspace == Workspace.Studio) _studio.HostStepBack();
            else _host.StepBack();
            PumpUi();
        }

        private void ShowHelp()
        {
            using (var dialog = new HelpDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_captureTimer != null)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            switch (keyData)
            {
                case Keys.F1:
                    ShowHelp();
                    return true;
                case Keys.F5:
                    if (_workspace == Workspace.Studio) _studio.HostRun();
                    else _host.Run();
                    PumpUi();
                    return true;
                case Keys.F6:
                    _host.Pause();
                    PumpUi();
                    return true;
                case Keys.F7:
                    if (_workspace == Workspace.Studio) _studio.HostAssemble();
                    PumpUi();
                    return true;
                case Keys.F8:
                    if (_workspace == Workspace.Studio) _studio.HostStep();
                    else _host.Step();
                    PumpUi();
                    return true;
                case Keys.F9:
                    if (_workspace == Workspace.Studio)
                    {
                        _studio.HostToggleBreakpoint(_studio.Editor.CaretLine);
                        PumpUi();
                    }
                    return true;
                case Keys.Control | Keys.R:
                    if (_workspace == Workspace.Studio) _studio.HostReset();
                    else _host.Reset();
                    PumpUi();
                    return true;
                case Keys.Control | Keys.N:
                    if (_workspace == Workspace.Studio) _studio.HostNew();
                    return true;
                case Keys.Control | Keys.O:
                    if (_workspace == Workspace.Studio) _studio.HostOpen();
                    return true;
                case Keys.Control | Keys.S:
                    if (_workspace == Workspace.Studio) _studio.HostSave();
                    return true;
                case Keys.Control | Keys.I:
                    ImportHex();
                    return true;
                case Keys.Control | Keys.E:
                    ExportHex();
                    return true;
                case Keys.Control | Keys.Shift | Keys.I:
                    Import580();
                    return true;
                case Keys.Control | Keys.Shift | Keys.E:
                    Export580();
                    return true;
                case Keys.Control | Keys.Alt | Keys.I:
                    ImportBinary();
                    return true;
                case Keys.Control | Keys.Alt | Keys.E:
                    ExportBinary();
                    return true;
                case Keys.Control | Keys.Shift | Keys.P:
                    EditProfile();
                    return true;
                case Keys.Control | Keys.F5:
                    RunToAddress();
                    return true;
                case Keys.Control | Keys.B:
                    ShowDebugPoints();
                    return true;
                case Keys.Control | Keys.F10:
                    StepBack();
                    return true;
                case Keys.Control | Keys.Shift | Keys.S:
                    SaveState();
                    return true;
                case Keys.Control | Keys.Shift | Keys.O:
                    LoadState();
                    return true;
                case Keys.Control | Keys.D1:
                    SwitchTo(Workspace.Studio);
                    return true;
                case Keys.Control | Keys.D2:
                    SwitchTo(Workspace.Laboratory);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================================================================== screenshot harness

        /// <summary>
        /// Drives the app through a fixed set of scenes and writes PNG files.
        /// Used by docs/screens generation and by the visual regression check.
        /// A watchdog guarantees the process can never hang in CI.
        /// </summary>
        public void BeginCapture(string directory, int sample, string workspace)
        {
            _captureDirectory = directory;
            _captureSample = sample;
            _captureWorkspace = workspace ?? "studio";
            Directory.CreateDirectory(_captureDirectory);

            _captureWatchdog = new System.Threading.Timer(
                state => Environment.Exit(3),
                null,
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(-1));

            _captureTimer = new Timer { Interval = 260 };
            _captureTimer.Tick += (sender, args) => CaptureStep();
            _captureTimer.Start();
        }

        private void CaptureStep()
        {
            if (_captureTimer != null) _captureTimer.Stop();

            try
            {
                switch (_captureStep)
                {
                    case 0:
                        WindowState = FormWindowState.Normal;
                        StartPosition = FormStartPosition.Manual;
                        Bounds = new Rectangle(40, 40, 1440, 900);
                        TopMost = true;
                        Activate();
                        if (_captureWorkspace == "lab" || _captureWorkspace == "laboratory")
                        {
                            SwitchTo(Workspace.Laboratory);
                        }
                        else
                        {
                            SwitchTo(Workspace.Studio);
                            _studio.HostLoadSample(_captureSample);
                        }
                        break;

                    case 1:
                        CaptureScene("01-studio-editor.png");
                        break;

                    case 2:
                        _studio.HostSelectInspector(1);
                        break;

                    case 3:
                        CaptureScene("02-studio-memory.png");
                        break;

                    case 4:
                        _studio.HostSelectInspector(2);
                        break;

                    case 5:
                        CaptureScene("03-studio-devices.png");
                        break;

                    case 6:
                        _studio.HostSelectInspector(0);
                        _studio.HostLoadSample(4);
                        _studio.HostRun();
                        break;

                    case 7:
                        CaptureScene("04-studio-display.png");
                        break;

                    case 8:
                        _studio.HostLoadSample(1);
                        _studio.HostRun();
                        SwitchTo(Workspace.Laboratory);
                        _laboratory.SelectLab(1);
                        break;

                    case 9:
                        CaptureScene("05-laboratory.png");
                        break;

                    case 10:
                        _studio.HostLoadSample(2);
                        _host.WithMachine(machine =>
                        {
                            machine.Ppi.Tumbler1 = true;
                            machine.Ppi.Tumbler3 = true;
                        });
                        _studio.HostRun();
                        _laboratory.SelectLab(4);
                        break;

                    case 11:
                        CaptureScene("06-laboratory-switches.png");
                        break;

                    case 12:
                        _studio.HostLoadSample(6);
                        _laboratory.SelectLab(5);
                        _host.WithMachine(machine =>
                        {
                            machine.Analog.SetChannel(2, 6.4);
                            machine.Analog.SelectedMuxChannel = 2;
                        });
                        _studio.HostRun();
                        break;

                    case 13:
                        break;

                    case 14:
                        CaptureScene("07-laboratory-analog.png");
                        break;

                    case 15:
                        _host.Pause();
                        _captureTimer.Stop();
                        _captureWatchdog.Dispose();
                        _uiTimer.Stop();
                        _host.Dispose();
                        Close();
                        Environment.Exit(0);
                        break;
                }
            }
            catch (Exception exception)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(_captureDirectory ?? ".", "capture-error.txt"),
                        exception.ToString());
                }
                catch
                {
                    // Ignore logging failures, exit status is enough.
                }
                Environment.Exit(4);
            }

            _captureStep++;
            if (_captureTimer != null)
            {
                _captureTimer.Start();
            }
        }

        private void CaptureScene(string fileName)
        {
            _uiTimer.Stop();
            PumpUi();
            Invalidate(true);
            Update();
            Application.DoEvents();
            System.Threading.Thread.Sleep(90);

            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.Combine(_captureDirectory, fileName), System.Drawing.Imaging.ImageFormat.Png);
            }

            _uiTimer.Start();
        }

        // ================================================================== disposal

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppTheme.EnableDarkTitleBar(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _captureTimer?.Stop();
                _captureTimer?.Dispose();
                _captureWatchdog?.Dispose();
                _toolTips?.Dispose();
                _fileMenu?.Dispose();
                _host?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class BrandMark : Control
        {
            public BrandMark()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor,
                    true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                AppTheme.FillRounded(graphics, bounds, 10, AppTheme.AccentSoft);
                AppTheme.DrawRoundedBorder(graphics, bounds, 10, AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.45));

                Rectangle chip = new Rectangle(11, 12, 20, 18);
                using (var body = new SolidBrush(AppTheme.Accent))
                using (var pen = new Pen(AppTheme.Text, 1.2f))
                {
                    graphics.FillRectangle(body, chip);
                    graphics.DrawRectangle(pen, chip);
                }

                using (var pen = new Pen(AppTheme.Accent))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int y = 15 + i * 6;
                        graphics.DrawLine(pen, 7, y, 11, y);
                        graphics.DrawLine(pen, 31, y, 35, y);
                    }
                }

                using (var dot = new SolidBrush(AppTheme.Text))
                {
                    graphics.FillEllipse(dot, chip.X + 7, chip.Y + 7, 5, 5);
                }
            }
        }
    }
}
