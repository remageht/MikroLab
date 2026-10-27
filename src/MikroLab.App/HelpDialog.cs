using System;
using System.Drawing;
using System.Windows.Forms;

namespace MikroLab.App
{
    internal sealed class HelpDialog : Form
    {
        public HelpDialog()
        {
            Text = "MikroLab — справка";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(600, 660);

            var title = new Label
            {
                Text = "MikroLab",
                Font = AppTheme.TitleFont,
                ForeColor = AppTheme.Text,
                AutoSize = true,
                Location = new Point(24, 20)
            };
            var subtitle = new Label
            {
                Text = "Учебный эмулятор КР580ВМ80А (Intel 8080)",
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
                AutoSize = true,
                Location = new Point(26, 52)
            };

            var shortcuts = new RichTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.UiFont,
                ReadOnly = true,
                DetectUrls = false,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Location = new Point(26, 88),
                Size = new Size(548, 350),
                Text =
                    "ГОРЯЧИЕ КЛАВИШИ\n" +
                    "F5 — пуск            F6 — пауза\n" +
                    "F7 — собрать        F8 — один шаг\n" +
                    "F9 — точка останова в текущей строке\n" +
                    "Ctrl+R — сброс   Ctrl+1 / Ctrl+2 — студия / лаборатория\n" +
                    "Ctrl+N / Ctrl+O / Ctrl+S — новый / открыть / сохранить\n" +
                    "Двойной клик в памяти — изменить байт\n" +
                    "Клик по номеру строки — точка останова\n\n" +
                    "УСТРОЙСТВА\n" +
                    "OUT 01h — светодиоды (порт A)\n" +
                    "IN FAh — переключатели SA1…SA3 (порт B)\n" +
                    "OUT FDh — выбор аналогового канала\n" +
                    "IN FEh — АЦП, 0…10 В → 0…255\n" +
                    "83F8h…83FFh — восемь семисегментных разрядов\n" +
                    "CALL 0100h — преобразование цифр в сегменты"
            };

            var license = new Label
            {
                Text = "Ядро основано на открытом проекте ZeeWanderer/Emulator\n" +
                       "(эмулятор КР580ВМ80А для студентов ВНТУ), GNU GPL v3.\n" +
                       "Подробности об авторстве — в THIRD-PARTY-NOTICES.md.",
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextDim,
                AutoSize = true,
                Location = new Point(26, 456)
            };

            var close = new UiButton
            {
                Text = "Закрыть",
                Kind = UiButtonKind.Primary,
                Width = 110,
                Height = 34,
                Location = new Point(464, 604)
            };
            close.Click += (sender, args) => Close();

            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(shortcuts);
            Controls.Add(license);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppTheme.EnableDarkTitleBar(this);
        }
    }
}
