using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Debugging;
using MikroLab.Core.Memory;
using MikroLab.Presentation;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>
    /// Editor for instruction breakpoints and memory watchpoints. Line
    /// breakpoints and manual address breakpoints are listed separately and
    /// can be removed independently; "clear all" only affects breakpoints.
    /// </summary>
    internal sealed class DebugPointsDialog : DialogWindow
    {
        private readonly MachineHost _host;
        private readonly ListBox _breakList;
        private readonly TextBox _breakAddress;
        private readonly ListBox _watchList;
        private readonly TextBox _watchAddress;
        private readonly ComboBox _watchType;

        private sealed class BreakItem
        {
            public ushort Address { get; }
            public int Line { get; }
            public bool IsLine => Line > 0;

            public BreakItem(ushort address, int line)
            {
                Address = address;
                Line = line;
            }

            public override string ToString()
            {
                if (IsLine && Address == 0)
                {
                    return "строка " + Line + "  ·  адрес не назначен";
                }

                string address = Address.ToString("X4", CultureInfo.InvariantCulture) + "h";
                if (IsLine) return "строка " + Line + "  ·  " + address;
                return address + "  ·  вручную";
            }
        }

        private DebugPointsDialog(MachineHost host)
            : base("Точки останова и наблюдения", 540, 640)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            var root = new StackPanel { Margin = new Thickness(18), Spacing = 6 };
            root.Children.Add(MakeLabel("ОТЛАДКА", AppTheme.TextDim, true));

            root.Children.Add(MakeLabel("Точки останова (строки и адреса)", AppTheme.Text, true));
            _breakList = MakeList();
            root.Children.Add(_breakList);

            var breakRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _breakAddress = MakeInput(string.Empty, 120);
            breakRow.Children.Add(_breakAddress);
            breakRow.Children.Add(MakeButton("Добавить", UiButtonKind.Secondary, 90, AddBreakpoint));
            breakRow.Children.Add(MakeButton("Удалить", UiButtonKind.Danger, 90, RemoveBreakpoint));
            breakRow.Children.Add(MakeButton("Очистить всё", UiButtonKind.Ghost, 110, () =>
            {
                _host.ClearAllBreakpoints();
                RefreshLists();
            }));
            root.Children.Add(breakRow);

            root.Children.Add(MakeLabel("Наблюдение памяти (чтение/запись)", AppTheme.Text, true));
            _watchList = MakeList();
            root.Children.Add(_watchList);

            var watchRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _watchAddress = MakeInput(string.Empty, 120);
            watchRow.Children.Add(_watchAddress);

            _watchType = new ComboBox
            {
                Width = 128,
                Height = 30,
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                Foreground = AppTheme.Brush(AppTheme.Text),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                FontSize = AppTheme.SmallSize,
                ItemsSource = new[] { "Чтение", "Запись", "Чтение+запись" },
                SelectedIndex = 1
            };
            watchRow.Children.Add(_watchType);
            watchRow.Children.Add(MakeButton("Добавить", UiButtonKind.Secondary, 90, AddWatchpoint));
            watchRow.Children.Add(MakeButton("Удалить", UiButtonKind.Danger, 90, RemoveWatchpoint));
            root.Children.Add(watchRow);

            var bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };
            bottom.Children.Add(MakeButton("Очистить наблюдения", UiButtonKind.Ghost, 160, () =>
            {
                _host.ClearWatchpoints();
                RefreshLists();
            }));
            var close = MakeButton("Закрыть", UiButtonKind.Primary, 100, Close);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Children.Add(close);
            root.Children.Add(bottom);

            Content = root;
            RefreshLists();
        }

        public static async Task ShowAsync(Window owner, MachineHost host)
        {
            var dialog = new DebugPointsDialog(host);
            await dialog.ShowDialog(owner);
        }

        private static ListBox MakeList()
        {
            return new ListBox
            {
                Height = 150,
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                Foreground = AppTheme.Brush(AppTheme.Text),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSmallSize
            };
        }

        private void AddBreakpoint()
        {
            string text = (_breakAddress.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                MarkInvalid(_breakAddress, true);
                return;
            }

            MarkInvalid(_breakAddress, false);
            _host.AddAddressBreakpoint(address);
            _breakAddress.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveBreakpoint()
        {
            if (!(_breakList.SelectedItem is BreakItem item)) return;

            if (item.IsLine) _host.RemoveBreakpointLine(item.Line);
            else _host.RemoveAddressBreakpoint(item.Address);
            RefreshLists();
        }

        private void AddWatchpoint()
        {
            string text = (_watchAddress.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                MarkInvalid(_watchAddress, true);
                return;
            }

            MarkInvalid(_watchAddress, false);
            MemoryAccess access;
            switch (_watchType.SelectedIndex)
            {
                case 0: access = MemoryAccess.Read; break;
                case 2: access = MemoryAccess.ReadWrite; break;
                default: access = MemoryAccess.Write; break;
            }

            _host.AddWatchpoint(address, access);
            _watchAddress.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveWatchpoint()
        {
            if (_watchList.SelectedItem is MemoryWatchpoint watchpoint)
            {
                _host.RemoveWatchpoint(watchpoint);
                RefreshLists();
            }
        }

        private void RefreshLists()
        {
            var breaks = new List<BreakItem>();
            foreach (int line in _host.BreakpointLines)
            {
                _host.TryGetAddressForLine(line, out ushort address);
                breaks.Add(new BreakItem(address, line));
            }
            foreach (ushort address in _host.ManualBreakpoints)
            {
                breaks.Add(new BreakItem(address, 0));
            }

            _breakList.ItemsSource = null;
            _breakList.ItemsSource = breaks;

            _watchList.ItemsSource = null;
            _watchList.ItemsSource = new List<MemoryWatchpoint>(_host.Watchpoints);
        }
    }
}
