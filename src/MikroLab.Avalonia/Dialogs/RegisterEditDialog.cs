using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Editor for one register value (byte or word).</summary>
    internal sealed class RegisterEditDialog : DialogWindow
    {
        private readonly TextBox _box;
        private readonly bool _word;

        private RegisterEditDialog(string name, ushort current, int digits)
            : base("Изменить регистр " + name, 320, 178)
        {
            _word = digits >= 4;

            var root = new StackPanel { Margin = new Thickness(18), Spacing = 7 };
            root.Children.Add(MakeLabel(
                "Значение (hex, " + (_word ? "0000–FFFF" : "00–FF") + "):",
                AppTheme.TextMuted));

            _box = MakeInput(current.ToString(_word ? "X4" : "X2", CultureInfo.InvariantCulture), 130);
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

        public static async Task<ushort?> ShowAsync(Window owner, string name, ushort current, int digits)
        {
            var dialog = new RegisterEditDialog(name, current, digits);
            return await dialog.ShowDialog<ushort?>(owner);
        }

        private void Accept()
        {
            string text = (_box.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (_word)
            {
                if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort word))
                {
                    Close((ushort?)word);
                    return;
                }
            }
            else if (byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            {
                Close((ushort?)value);
                return;
            }

            MarkInvalid(_box, true);
        }
    }
}
