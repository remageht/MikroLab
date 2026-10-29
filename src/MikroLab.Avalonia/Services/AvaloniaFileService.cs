using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MikroLab.Presentation.Services;

namespace MikroLab.Studio.Services
{
    /// <summary>
    /// Asynchronous file dialogs backed by the Avalonia storage provider. The
    /// portable <see cref="IFileDialogService"/> is synchronous and therefore
    /// unsuitable for an Avalonia UI thread.
    /// </summary>
    internal interface IAvaloniaFileService
    {
        Task<string> OpenFileAsync(FileDialogRequest request);
        Task<string> SaveFileAsync(FileDialogRequest request);
    }

    internal sealed class AvaloniaFileService : IAvaloniaFileService
    {
        private readonly Func<Window> _owner;

        public AvaloniaFileService(Func<Window> owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public async Task<string> OpenFileAsync(FileDialogRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Window owner = _owner();
            if (owner == null) return null;

            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = request.Title,
                    AllowMultiple = false,
                    FileTypeFilter = BuildTypes(request.Filter)
                });

            return files != null && files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        public async Task<string> SaveFileAsync(FileDialogRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Window owner = _owner();
            if (owner == null) return null;

            string suggested = request.DefaultFileName;
            IStorageFile file = await owner.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = request.Title,
                    SuggestedFileName = suggested,
                    DefaultExtension = string.IsNullOrEmpty(suggested) ? null : Path.GetExtension(suggested).TrimStart('.'),
                    FileTypeChoices = BuildTypes(request.Filter),
                    ShowOverwritePrompt = true
                });

            return file?.Path.LocalPath;
        }

        private static List<FilePickerFileType> BuildTypes(string filter)
        {
            var types = new List<FilePickerFileType>();
            if (string.IsNullOrEmpty(filter))
            {
                types.Add(FilePickerFileTypes.All);
                return types;
            }

            string[] parts = filter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string name = parts[i].Trim();
                string patterns = parts[i + 1].Trim();
                if (name.Length == 0 || patterns.Length == 0) continue;

                string[] globs = patterns.Split(';');
                var list = new List<string>();
                foreach (string glob in globs)
                {
                    string value = glob.Trim();
                    if (value.Length == 0) continue;
                    list.Add(value == "*.*" ? "*" : value);
                }

                types.Add(new FilePickerFileType(name) { Patterns = list });
            }

            if (types.Count == 0) types.Add(FilePickerFileTypes.All);
            return types;
        }
    }
}
