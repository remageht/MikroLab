using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Three-way confirmation used before discarding an edited document.</summary>
    internal sealed class ConfirmDialog : DialogWindow
    {
        private ConfirmDialog(string title, string message)
            : base(title ?? "MikroLab", 460, 190)
        {
            var root = new StackPanel { Margin = new Thickness(20), Spacing = 14 };

            var text = new TextBlock
            {
                Text = message ?? string.Empty,
                Foreground = AppTheme.Brush(AppTheme.TextMuted),
                FontFamily = AppTheme.UiFamily,
                FontSize = AppTheme.UiSize,
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(text);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            row.Children.Add(MakeButton("Отмена", UiButtonKind.Secondary, 96, () => Close(null)));
            row.Children.Add(MakeButton("Нет", UiButtonKind.Secondary, 90, () => Close((bool?)false)));
            row.Children.Add(MakeButton("Да", UiButtonKind.Primary, 90, () => Close((bool?)true)));
            root.Children.Add(row);

            Content = root;
        }

        public static async Task<bool?> ShowAsync(Window owner, string message, string title = "MikroLab")
        {
            var dialog = new ConfirmDialog(title, message);
            return await dialog.ShowDialog<bool?>(owner);
        }
    }
}
