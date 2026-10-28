namespace MikroLab.Presentation.Services
{
    /// <summary>Description of an open/save file dialog request.</summary>
    public sealed class FileDialogRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Filter { get; set; } = "Все файлы (*.*)|*.*";
        public string DefaultFileName { get; set; } = string.Empty;
        public bool CheckFileExists { get; set; }
    }

    /// <summary>
    /// Abstract file dialogs so the presentation layer and its view models do
    /// not depend on WinForms. Implementations return null when cancelled.
    /// </summary>
    public interface IFileDialogService
    {
        string OpenFile(FileDialogRequest request);
        string SaveFile(FileDialogRequest request);
    }
}
