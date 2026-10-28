namespace MikroLab.Presentation.Services
{
    /// <summary>
    /// Captures the current view into a PNG file. The presentation layer only
    /// knows this interface; WinForms supplies the actual bitmap rendering.
    /// </summary>
    public interface IScreenshotService
    {
        void Capture(string path);
    }
}
