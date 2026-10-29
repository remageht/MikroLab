using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    internal sealed class HelpDialog : DialogWindow
    {
        private const string Shortcuts =
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
            "Ctrl+F5 — выполнение до адреса\n" +
            "Ctrl+B — точки останова и наблюдение\n" +
            "Ctrl+F10 — шаг назад\n" +
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
            "CALL 0100h — преобразование цифр в сегменты";

        private HelpDialog() : base("MikroLab — справка", 620, 760)
        {
            var root = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto,Auto"),
                Margin = new Thickness(24, 18, 24, 18)
            };

            var title = AppTheme.Label("MikroLab", AppTheme.Text, 24, true);
            root.Children.Add(title);
            Grid.SetRow(title, 0);

            var subtitle = AppTheme.Label("Учебный эмулятор КР580ВМ80А (Intel 8080)", AppTheme.TextMuted);
            subtitle.Margin = new Thickness(0, 2, 0, 10);
            root.Children.Add(subtitle);
            Grid.SetRow(subtitle, 1);

            var shortcuts = new TextBlock
            {
                Text = Shortcuts,
                Foreground = AppTheme.Brush(AppTheme.TextMuted),
                FontFamily = AppTheme.UiFamily,
                FontSize = AppTheme.UiSize,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var scroll = new ScrollViewer
            {
                Content = shortcuts,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 2);

            var license = new TextBlock
            {
                Text = "Ядро основано на открытом проекте ZeeWanderer/Emulator\n" +
                       "(эмулятор КР580ВМ80А для студентов ВНТУ), GNU GPL v3.\n" +
                       "Подробности об авторстве — в THIRD-PARTY-NOTICES.md.",
                Foreground = AppTheme.Brush(AppTheme.TextDim),
                FontFamily = AppTheme.UiFamily,
                FontSize = AppTheme.SmallSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(license);
            Grid.SetRow(license, 3);

            var close = MakeButton("Закрыть", UiButtonKind.Primary, 110, Close);
            close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            root.Children.Add(close);
            Grid.SetRow(close, 4);

            Content = root;
        }

        public static async Task ShowAsync(Window owner)
        {
            var dialog = new HelpDialog();
            await dialog.ShowDialog(owner);
        }
    }
}
