using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MikroLab.Core.Machine;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Editor for the board's default origin, initial PC and stack.</summary>
    internal sealed class ProfileDialog : DialogWindow
    {
        private readonly TextBox _name;
        private readonly TextBox _origin;
        private readonly TextBox _entry;
        private readonly TextBox _stack;

        private ProfileDialog(MachineProfile current)
            : base("Профиль памяти", 380, 292)
        {
            current = current ?? MachineProfile.Vntu;

            var root = new StackPanel { Margin = new Thickness(18), Spacing = 6 };

            root.Children.Add(MakeLabel(
                "Профиль задаёт адрес по умолчанию для ORG, начальный PC и SP.",
                AppTheme.TextMuted));

            root.Children.Add(MakeLabel("Название", AppTheme.TextMuted));
            _name = MakeInput(current.Name, 320);
            root.Children.Add(_name);

            var addresses = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("96,96,96"),
                ColumnSpacing = 10,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var originPanel = new StackPanel { Spacing = 3 };
            originPanel.Children.Add(MakeLabel("ORG", AppTheme.TextMuted));
            _origin = MakeInput(current.Origin.ToString("X4"), 90);
            originPanel.Children.Add(_origin);
            addresses.Children.Add(originPanel);
            Grid.SetColumn(originPanel, 0);

            var entryPanel = new StackPanel { Spacing = 3 };
            entryPanel.Children.Add(MakeLabel("PC", AppTheme.TextMuted));
            _entry = MakeInput(current.EntryPoint.ToString("X4"), 90);
            entryPanel.Children.Add(_entry);
            addresses.Children.Add(entryPanel);
            Grid.SetColumn(entryPanel, 1);

            var stackPanel = new StackPanel { Spacing = 3 };
            stackPanel.Children.Add(MakeLabel("SP", AppTheme.TextMuted));
            _stack = MakeInput(current.StackPointer.ToString("X4"), 90);
            stackPanel.Children.Add(_stack);
            addresses.Children.Add(stackPanel);
            Grid.SetColumn(stackPanel, 2);

            root.Children.Add(addresses);
            root.Children.Add(MakeLabel("Адреса вводятся в hex: 0800, 8000, 0FFF…", AppTheme.TextDim));

            var presets = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };
            presets.Children.Add(MakeButton("ВНТУ", UiButtonKind.Ghost, 84, () => Fill(MachineProfile.Vntu)));
            presets.Children.Add(MakeButton("УМК", UiButtonKind.Ghost, 84, () => Fill(MachineProfile.Umk)));
            root.Children.Add(presets);

            var ok = MakeButton("Применить", UiButtonKind.Primary, 112, Accept);
            var cancel = MakeButton("Отмена", UiButtonKind.Secondary, 96, () => Close(null));
            root.Children.Add(MakeButtonRow(cancel, ok));

            Content = root;
        }

        public static async Task<MachineProfile> ShowAsync(Window owner, MachineProfile current)
        {
            var dialog = new ProfileDialog(current);
            return await dialog.ShowDialog<MachineProfile>(owner);
        }

        private void Fill(MachineProfile profile)
        {
            _name.Text = profile.Name;
            _origin.Text = profile.Origin.ToString("X4");
            _entry.Text = profile.EntryPoint.ToString("X4");
            _stack.Text = profile.StackPointer.ToString("X4");
        }

        private void Accept()
        {
            bool valid = true;
            valid &= TryReadHex(_origin, out ushort origin);
            valid &= TryReadHex(_entry, out ushort entry);
            valid &= TryReadHex(_stack, out ushort stack);
            if (!valid) return;

            Close(new MachineProfile(_name.Text, origin, entry, stack));
        }

        private static bool TryReadHex(TextBox box, out ushort value)
        {
            string text = (box.Text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            text = text.TrimEnd('h', 'H');

            bool ok = ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            MarkInvalid(box, !ok);
            return ok;
        }
    }
}
