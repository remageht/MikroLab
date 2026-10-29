using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Prompts for a single 16-bit address.</summary>
    internal sealed class AddressDialog : DialogWindow
    {
        private readonly TextBox _box;

        private AddressDialog(string title, string prompt, ushort current)
            : base(title ?? "Адрес", 340, 190)
        {
            var root = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 7
            };

            root.Children.Add(MakeLabel(prompt ?? "Адрес (hex, 0000–FFFF):", AppTheme.TextMuted));

            _box = MakeInput(current.ToString("X4", CultureInfo.InvariantCulture), 150);
            _box.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(_box);

            var ok = MakeButton("OK", UiButtonKind.Primary, 90, Accept);
            var cancel = MakeButton("Отмена", UiButtonKind.Secondary, 90, () => Close(null));
            root.Children.Add(MakeButtonRow(cancel, ok));

            Content = root;
            Opened += (sender, args) =>
            {
                _box.Focus();
                _box.SelectAll();
            };
        }

        public static async Task<ushort?> ShowAsync(Window owner, string title, string prompt, ushort current)
        {
            var dialog = new AddressDialog(title, prompt, current);
            return await dialog.ShowDialog<ushort?>(owner);
        }

        private void Accept()
        {
            string text = (_box.Text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            text = text.TrimEnd('h', 'H');

            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
            {
                Close((ushort?)value);
                return;
            }

            MarkInvalid(_box, true);
        }
    }
}
