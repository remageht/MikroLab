using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Presentation;
using MikroLab.Studio.Controls;

namespace MikroLab.Studio.Dialogs
{
    /// <summary>Offers the user to open the release page of a newer version.</summary>
    internal sealed class UpdateDialog : DialogWindow
    {
        private readonly UpdateInfo _info;

        private UpdateDialog(UpdateInfo info)
            : base("Обновление MikroLab", 420, 190)
        {
            _info = info;

            var root = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
            root.Children.Add(MakeLabel(
                "Доступна новая версия MikroLab " + info.LatestVersion +
                " (установлена " + info.CurrentVersion + ").",
                AppTheme.Text));
            root.Children.Add(MakeLabel(
                "Открыть страницу релиза в браузере?",
                AppTheme.TextMuted));

            var open = MakeButton("Открыть релиз", UiButtonKind.Primary, 128, () =>
            {
                OpenRelease();
                Close(true);
            });
            var later = MakeButton("Позже", UiButtonKind.Secondary, 96, () => Close(false));

            root.Children.Add(MakeButtonRow(later, open));
            Content = root;
        }

        private void OpenRelease()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _info.Url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // не смогли открыть браузер — молча показываем ссылку в вывод
            }
        }

        public static async Task<bool> ShowAsync(Window owner, UpdateInfo info)
        {
            var dialog = new UpdateDialog(info);
            bool? result = await dialog.ShowDialog<bool?>(owner);
            return result == true;
        }
    }
}
