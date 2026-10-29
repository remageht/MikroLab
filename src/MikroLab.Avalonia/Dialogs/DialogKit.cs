using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Shared dark chrome for the small modal windows.</summary>
    internal abstract class DialogWindow : Window
    {
        protected DialogWindow(string title, double width, double height)
        {
            Title = title;
            Width = width;
            Height = height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = AppTheme.Brush(AppTheme.Surface);
            Foreground = AppTheme.Brush(AppTheme.Text);
            FontFamily = AppTheme.UiFamily;
            FontSize = AppTheme.UiSize;
        }

        protected static TextBox MakeInput(string text, double width = 150)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                Width = width,
                Height = 30,
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                Foreground = AppTheme.Brush(AppTheme.Text),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSmallSize,
                Padding = new Thickness(7, 4),
                CaretBrush = AppTheme.Brush(AppTheme.Text),
                SelectionBrush = AppTheme.Brush(AppTheme.Selection)
            };
        }

        protected static UiButton MakeButton(string text, UiButtonKind kind, double width, Action action)
        {
            var button = new UiButton
            {
                Text = text,
                Kind = kind,
                Width = width,
                Height = 30,
                FontSize = AppTheme.SmallSize
            };
            if (action != null) button.Click += action;
            return button;
        }

        protected static StackPanel MakeButtonRow(params Control[] controls)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };
            foreach (Control control in controls) row.Children.Add(control);
            return row;
        }

        protected static TextBlock MakeLabel(string text, Color color, bool bold = false)
        {
            return AppTheme.Label(text, color, AppTheme.SmallSize, bold);
        }

        protected static void MarkInvalid(TextBox box, bool invalid)
        {
            box.BorderBrush = AppTheme.Brush(invalid ? AppTheme.Red : AppTheme.Border);
            box.Background = AppTheme.Brush(invalid ? Color.FromRgb(76, 36, 42) : AppTheme.EditorBackground);
        }
    }
}
