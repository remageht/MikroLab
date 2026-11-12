using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Presentation;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>
    /// Settings of the studio. The set of options follows the laboratory bench
    /// emulator: the load address of the assembled program, automatic assembly
    /// and disassembly of opened files, the bench tumblers and the speed.
    /// </summary>
    internal sealed class SettingsDialog : DialogWindow
    {
        private static readonly string[] SpeedNames =
        {
            "Оригинал (2–5 мс на команду)",
            "Максимум",
            "100 000 команд в секунду",
            "50 000 команд в секунду",
            "10 000 команд в секунду",
            "1 000 команд в секунду",
            "100 команд в секунду",
            "10 команд в секунду"
        };

        private static readonly int[] FontScales = { 90, 100, 110, 125, 150 };

        private readonly ComboBox _speed = new ComboBox();
        private readonly CheckBox _pauseOnHalt = new CheckBox();
        private readonly CheckBox _stepToBranch = new CheckBox();
        private readonly CheckBox _trace = new CheckBox();
        private readonly CheckBox _autoAssemble = new CheckBox();
        private readonly CheckBox _autoDisassemble = new CheckBox();
        private readonly TextBox _loadAddress = MakeInput("8000h", 120);
        private readonly CheckBox _tumbler1 = new CheckBox();
        private readonly CheckBox _tumbler2 = new CheckBox();
        private readonly CheckBox _tumbler3 = new CheckBox();
        private readonly ComboBox _fontScale = new ComboBox();
        private readonly CheckBox _showLog = new CheckBox();
        private readonly CheckBox _showDevices = new CheckBox();

        private SettingsDialog(MikroLabSettings settings)
            : base("Настройки MikroLab", 560, 520)
        {
            _speed.ItemsSource = SpeedNames;
            _fontScale.ItemsSource = new[] { "90 %", "100 %", "110 %", "125 %", "150 %" };

            var root = new Grid
            {
                Margin = new Thickness(18, 14, 18, 12),
                RowSpacing = 8
            };
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var scroll = new ScrollViewer
            {
                Content = BuildForm(settings),
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 0);

            var ok = MakeButton("Применить", UiButtonKind.Primary, 108, () =>
            {
                Apply(settings);
                Close(true);
            });
            var cancel = MakeButton("Отмена", UiButtonKind.Secondary, 96, () => Close(false));
            var reset = MakeButton("По умолчанию", UiButtonKind.Ghost, 128, () => LoadInto(new MikroLabSettings()));

            root.Children.Add(scroll);
            Grid.SetRow(root.Children[^1], 0);

            var buttons = MakeButtonRow(reset, cancel, ok);
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 1);

            Content = root;
            LoadInto(settings);
        }

        private Control BuildForm(MikroLabSettings settings)
        {
            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(MakeLabel("Выполнение", AppTheme.Accent, true));
            panel.Children.Add(Row("Скорость", _speed));
            panel.Children.Add(Row("Останавливаться при HLT", _pauseOnHalt));
            panel.Children.Add(Row("Шаг до следующего перехода", _stepToBranch));
            panel.Children.Add(Row("Трассировать все команды", _trace));

            panel.Children.Add(MakeLabel("Программа", AppTheme.Accent, true));
            panel.Children.Add(Row("Адрес загрузки программы", _loadAddress));
            panel.Children.Add(Row("Собирать мнемокод при открытии .asm", _autoAssemble));
            panel.Children.Add(Row("Дизассемблировать .hex при открытии", _autoDisassemble));

            panel.Children.Add(MakeLabel("Приставки стенда", AppTheme.Accent, true));
            panel.Children.Add(Row("Тумблер 1", _tumbler1));
            panel.Children.Add(Row("Тумблер 2", _tumbler2));
            panel.Children.Add(Row("Тумблер 3", _tumbler3));

            panel.Children.Add(MakeLabel("Интерфейс", AppTheme.Accent, true));
            panel.Children.Add(Row("Масштаб шрифта редактора", _fontScale));
            panel.Children.Add(Row("Показывать журнал выполнения", _showLog));
            panel.Children.Add(Row("Показывать приставки", _showDevices));

            return panel;
        }

        private static Control Row(string caption, Control control)
        {
            var grid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("250,*"),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var label = MakeLabel(caption, AppTheme.TextMuted);
            label.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label);
            Grid.SetColumn(label, 0);

            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(control);
            Grid.SetColumn(control, 1);
            return grid;
        }

        private void LoadInto(MikroLabSettings settings)
        {
            _speed.SelectedIndex = Math.Max(0, Math.Min(SpeedNames.Length - 1, settings.SpeedIndex));
            _pauseOnHalt.IsChecked = settings.PauseOnHalt;
            _stepToBranch.IsChecked = settings.StepToNextBranch;
            _trace.IsChecked = settings.TraceEveryInstruction;
            _autoAssemble.IsChecked = settings.AutoAssembleOnOpen;
            _autoDisassemble.IsChecked = settings.AutoDisassembleOnOpen;
            _loadAddress.Text = settings.LoadAddress ?? "8000h";
            _tumbler1.IsChecked = settings.Tumbler1;
            _tumbler2.IsChecked = settings.Tumbler2;
            _tumbler3.IsChecked = settings.Tumbler3;
            _fontScale.SelectedIndex = Math.Max(0, FontScales.IndexOf(
                settings.EditorFontScale <= 0 ? 100 : settings.EditorFontScale));
            _showLog.IsChecked = settings.ShowExecutionLog;
            _showDevices.IsChecked = settings.ShowDevices;
        }

        private void Apply(MikroLabSettings settings)
        {
            settings.SpeedIndex = Math.Max(0, _speed.SelectedIndex);
            settings.PauseOnHalt = _pauseOnHalt.IsChecked == true;
            settings.StepToNextBranch = _stepToBranch.IsChecked == true;
            settings.TraceEveryInstruction = _trace.IsChecked == true;
            settings.AutoAssembleOnOpen = _autoAssemble.IsChecked == true;
            settings.AutoDisassembleOnOpen = _autoDisassemble.IsChecked == true;
            settings.LoadAddress = (_loadAddress.Text ?? string.Empty).Trim();
            settings.Tumbler1 = _tumbler1.IsChecked == true;
            settings.Tumbler2 = _tumbler2.IsChecked == true;
            settings.Tumbler3 = _tumbler3.IsChecked == true;
            settings.EditorFontScale = FontScales[Math.Max(0, _fontScale.SelectedIndex)];
            settings.ShowExecutionLog = _showLog.IsChecked == true;
            settings.ShowDevices = _showDevices.IsChecked == true;
            settings.Save();
        }

        /// <summary>Shows the dialog and returns true when the values were applied.</summary>
        public static async Task<bool> ShowAsync(Window owner, MikroLabSettings settings)
        {
            var dialog = new SettingsDialog(settings);
            bool? result = await dialog.ShowDialog<bool?>(owner);
            return result == true;
        }
    }
}
