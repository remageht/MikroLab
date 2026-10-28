using System;
using System.Windows.Forms;
using MikroLab.Presentation.Services;

namespace MikroLab.App.Services
{
    /// <summary>WinForms implementation of the portable file-dialog service.</summary>
    internal sealed class WinFormsFileDialogService : IFileDialogService
    {
        private readonly Func<IWin32Window> _owner;

        public WinFormsFileDialogService(Func<IWin32Window> owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public string OpenFile(FileDialogRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = request.Title;
                dialog.Filter = request.Filter;
                dialog.CheckFileExists = request.CheckFileExists;
                if (!string.IsNullOrEmpty(request.DefaultFileName))
                {
                    dialog.FileName = request.DefaultFileName;
                }

                return Show(dialog) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        public string SaveFile(FileDialogRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = request.Title;
                dialog.Filter = request.Filter;
                if (!string.IsNullOrEmpty(request.DefaultFileName))
                {
                    dialog.FileName = request.DefaultFileName;
                }

                return Show(dialog) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private DialogResult Show(FileDialog dialog)
        {
            IWin32Window owner = _owner();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }
    }
}
