using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MikroLab.Core.Assembler;

namespace MikroLab.App.Controls
{
    internal sealed class OutputPanel : UserControl
    {
        private readonly RichTextBox _log;
        private readonly ListBox _errors;
        private readonly Label _errorCount;
        private readonly List<AssemblerError> _errorItems = new List<AssemblerError>();

        public event Action<int> ErrorActivated;

        public OutputPanel()
        {
            BackColor = AppTheme.Surface;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = AppTheme.SurfaceRaised
            };
            Label title = AppTheme.Label("ЖУРНАЛ · СБОРКА · ОШИБКИ", AppTheme.TextDim, AppTheme.SmallBoldFont);
            title.Location = new Point(12, 9);

            _errorCount = AppTheme.Label("Ошибок: 0", AppTheme.TextDim, AppTheme.SmallFont);
            _errorCount.AutoSize = false;
            _errorCount.TextAlign = ContentAlignment.MiddleRight;
            _errorCount.Dock = DockStyle.Right;
            _errorCount.Width = 100;

            var clear = new UiButton
            {
                Text = "Очистить",
                Kind = UiButtonKind.Ghost,
                Width = 78,
                Height = 24,
                Dock = DockStyle.Right,
                Font = AppTheme.SmallFont
            };
            clear.Click += (sender, args) => Clear();

            header.Controls.Add(title);
            header.Controls.Add(clear);
            header.Controls.Add(_errorCount);

            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false,
                HideSelection = false,
                Margin = new Padding(0)
            };

            _errors = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                IntegralHeight = false,
                Margin = new Padding(0)
            };
            _errors.DrawItem += DrawErrorItem;
            _errors.DoubleClick += (sender, args) =>
            {
                int index = _errors.SelectedIndex;
                if (index >= 0 && index < _errorItems.Count)
                {
                    ErrorActivated?.Invoke(_errorItems[index].LineNumber);
                }
            };

            split.Controls.Add(_log, 0, 0);
            split.Controls.Add(_errors, 1, 0);
            Controls.Add(split);
            Controls.Add(header);
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

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "]  " + message + Environment.NewLine);
            _log.SelectionColor = _log.ForeColor;
            _log.ScrollToCaret();

            if (_log.Lines.Length > 2000)
            {
                int cut = _log.GetFirstCharIndexFromLine(_log.Lines.Length - 1200);
                if (cut > 0)
                {
                    _log.Select(0, cut);
                    _log.SelectedText = string.Empty;
                }
            }
        }

        public void SetErrors(IReadOnlyList<AssemblerError> errors)
        {
            _errorItems.Clear();
            _errors.BeginUpdate();
            try
            {
                _errors.Items.Clear();
                if (errors != null)
                {
                    foreach (AssemblerError error in errors)
                    {
                        _errorItems.Add(error);
                        _errors.Items.Add("Строка " + error.LineNumber + " · " + error.Message);
                    }
                }
            }
            finally
            {
                _errors.EndUpdate();
            }

            _errorCount.Text = "Ошибок: " + _errorItems.Count;
            _errorCount.ForeColor = _errorItems.Count == 0 ? AppTheme.TextDim : AppTheme.Red;
        }

        public void Clear()
        {
            _log.Clear();
            SetErrors(Array.Empty<AssemblerError>());
        }

        private void DrawErrorItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var brush = new SolidBrush(selected ? AppTheme.AccentSoft : AppTheme.Surface))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            string text = _errors.Items[e.Index].ToString();
            TextRenderer.DrawText(
                e.Graphics,
                text,
                AppTheme.MonoSmall,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                selected ? AppTheme.Text : AppTheme.Red,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
