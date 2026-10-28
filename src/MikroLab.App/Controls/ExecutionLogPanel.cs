using System;
using System.Drawing;
using System.Windows.Forms;
using MikroLab.Core.Debugging;

namespace MikroLab.App.Controls
{
    internal sealed class ExecutionLogPanel : UserControl
    {
        private readonly ListBox _list;
        private readonly Label _details;
        private readonly ToggleSwitch _follow;
        private readonly Label _countLabel;
        private ExecutionLog _log;
        private long _revision;
        private DateTime _lastRefresh = DateTime.MinValue;

        public event Action ClearRequested;
        public event Action<ExecutionLogFormat> ExportRequested;

        public ExecutionLogPanel()
        {
            BackColor = AppTheme.Surface;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = AppTheme.Surface,
                Padding = new Padding(12, 6, 12, 4)
            };

            Label title = AppTheme.Label("ЖУРНАЛ ВЫПОЛНЕНИЯ", AppTheme.TextDim, AppTheme.SmallBoldFont);
            title.Location = new Point(12, 6);
            header.Controls.Add(title);

            _countLabel = AppTheme.Label("0 записей", AppTheme.TextMuted, AppTheme.SmallFont);
            _countLabel.AutoSize = false;
            _countLabel.TextAlign = ContentAlignment.MiddleRight;
            _countLabel.Dock = DockStyle.Top;
            _countLabel.Height = 18;
            header.Controls.Add(_countLabel);

            var buttonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            var clear = new UiButton
            {
                Text = "Очистить",
                Kind = UiButtonKind.Ghost,
                Width = 84,
                Height = 26,
                Font = AppTheme.SmallFont,
                Margin = new Padding(0, 0, 6, 0)
            };
            clear.Click += (sender, args) => ClearRequested?.Invoke();
            buttonBar.Controls.Add(clear);

            var csv = new UiButton
            {
                Text = "CSV",
                Kind = UiButtonKind.Subtle,
                Width = 52,
                Height = 26,
                Font = AppTheme.SmallFont,
                Margin = new Padding(0, 0, 6, 0)
            };
            csv.Click += (sender, args) => ExportRequested?.Invoke(ExecutionLogFormat.Csv);
            buttonBar.Controls.Add(csv);

            var md = new UiButton
            {
                Text = "MD",
                Kind = UiButtonKind.Subtle,
                Width = 52,
                Height = 26,
                Font = AppTheme.SmallFont,
                Margin = new Padding(0, 0, 6, 0)
            };
            md.Click += (sender, args) => ExportRequested?.Invoke(ExecutionLogFormat.Markdown);
            buttonBar.Controls.Add(md);

            _follow = new ToggleSwitch
            {
                Text = "Следить",
                Checked = true,
                Width = 90,
                Height = 26,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
                Margin = new Padding(6, 0, 0, 0)
            };
            buttonBar.Controls.Add(_follow);

            header.Controls.Add(buttonBar);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                IntegralHeight = false
            };
            _list.DrawItem += OnDrawItem;
            _list.SelectedIndexChanged += (sender, args) => UpdateDetails();

            _details = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = AppTheme.SurfaceRaised,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.MonoSmall,
                Padding = new Padding(10, 6, 10, 6),
                Text = "Выберите запись, чтобы увидеть регистры."
            };

            Controls.Add(_list);
            Controls.Add(_details);
            Controls.Add(header);
        }

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
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (ExecutionLogEntry entry in entries)
            {
                _list.Items.Add(entry);
            }
            _list.EndUpdate();

            _countLabel.Text = log.Count + " / " + log.Capacity + " записей";

            if (_follow.Checked && _list.Items.Count > 0)
            {
                _list.TopIndex = _list.Items.Count - 1;
                _list.SelectedIndex = _list.Items.Count - 1;
            }
            UpdateDetails();
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;

            ExecutionLogEntry entry = _list.Items[e.Index] as ExecutionLogEntry;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? AppTheme.Selection : AppTheme.Surface;
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            string text = entry != null ? entry.ShortText : string.Empty;
            TextRenderer.DrawText(
                e.Graphics,
                text,
                AppTheme.MonoSmall,
                e.Bounds,
                selected ? AppTheme.Text : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void UpdateDetails()
        {
            if (_list.SelectedItem is ExecutionLogEntry entry)
            {
                _details.Text =
                    entry.RegistersText + Environment.NewLine +
                    "Флаги: " + entry.FlagsText + "   Такты: +" + entry.Cycles +
                    " / всего " + entry.TotalCycles;
            }
        }
    }
}
