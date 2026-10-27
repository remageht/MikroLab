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
            ClientSize = new Size(600, 700);

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
                Size = new Size(548, 386),
                Text =
                    "ГОРЯЧИЕ КЛАВИШИ\n" +
                    "F5 — пуск            F6 — пауза\n" +
                    "F7 — собрать        F8 — один шаг\n" +
                    "F9 — точка останова в текущей строке\n" +
                    "Ctrl+R — сброс   Ctrl+1 / Ctrl+2 — студия / лаборатория\n" +
                    "Ctrl+N / Ctrl+O / Ctrl+S — новый / открыть / сохранить\n" +
                    "Ctrl+I / Ctrl+E — импорт / экспорт Intel HEX\n" +
                    "Ctrl+Alt+I / Ctrl+Alt+E — импорт / экспорт BIN\n" +
                    "Ctrl+Shift+I / Ctrl+Shift+E — импорт / экспорт .580\n" +
                    "Ctrl+Shift+P — профиль памяти (ORG / PC / SP)\n" +
                    "Ctrl+Shift+S / Ctrl+Shift+O — сохранить / загрузить состояние\n" +
                    "Двойной клик по регистру — изменить значение\n" +
                    "Двойной клик в памяти — изменить байт\n" +
                    "Клик по номеру строки — точка останова\n" +
                    "Меню «Файл» — программы, HEX, BIN, .580, профиль и состояние\n" +
                    "Скорость в шапке — от оригинала до максимума\n\n" +
                    "ПРОФИЛИ ПАМЯТИ\n" +
                    "ВНТУ — ORG 8000h, PC 8000h, SP 83F0h\n" +
                    "УМК — ORG 0800h, PC 0800h, SP 0FFFh\n" +
                    "Свой профиль — любые ORG / PC / SP\n\n" +
                    "УСТРОЙСТВА\n" +
                    "OUT 0FBh — управляющее слово ВВ55\n" +
                    "OUT 0F9h — светодиоды (порт B)\n" +
                    "IN 0FAh — переключатели SA1…SA3 (порт A)\n" +
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
                Location = new Point(26, 492)
            };

            var close = new UiButton
            {
                Text = "Закрыть",
                Kind = UiButtonKind.Primary,
                Width = 110,
                Height = 34,
                Location = new Point(464, 640)
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
