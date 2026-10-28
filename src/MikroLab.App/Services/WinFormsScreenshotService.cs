using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MikroLab.Presentation.Services;

namespace MikroLab.App.Services
{
    /// <summary>Renders a WinForms window into a PNG file.</summary>
    internal sealed class WinFormsScreenshotService : IScreenshotService
    {
        private readonly Func<Form> _form;

        public WinFormsScreenshotService(Func<Form> form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
        }

        public void Capture(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            Form form = _form();
            if (form == null) throw new InvalidOperationException("Screenshot owner is not available.");

            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }
}
