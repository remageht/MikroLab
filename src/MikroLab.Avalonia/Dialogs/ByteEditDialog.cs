using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Editor for one memory byte.</summary>
    internal sealed class ByteEditDialog : DialogWindow
    {
        private readonly TextBox _box;

        private ByteEditDialog(ushort address, byte current)
            : base("Изменить байт " + address.ToString("X4") + "h", 300, 176)
        {
            var root = new StackPanel { Margin = new Thickness(18), Spacing = 7 };
            root.Children.Add(MakeLabel("Значение (hex, 00–FF):", AppTheme.TextMuted));

            _box = MakeInput(current.ToString("X2"), 120);
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

        public static async Task<byte?> ShowAsync(Window owner, ushort address, byte current)
        {
            var dialog = new ByteEditDialog(address, current);
            return await dialog.ShowDialog<byte?>(owner);
        }

        private void Accept()
        {
            string text = (_box.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            {
                Close((byte?)value);
                return;
            }

            MarkInvalid(_box, true);
        }
    }
}
