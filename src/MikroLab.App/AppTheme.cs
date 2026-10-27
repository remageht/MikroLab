using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MikroLab.App
{
    /// <summary>
    /// Central palette and typography for the application. Keeping the theme in
    /// one place makes the Studio and Laboratory workspaces feel like a single
    /// product instead of two unrelated windows.
    /// </summary>
    internal static class AppTheme
    {
        public static readonly Color Window = Color.FromArgb(17, 19, 24);
        public static readonly Color Surface = Color.FromArgb(24, 27, 34);
        public static readonly Color SurfaceRaised = Color.FromArgb(31, 35, 44);
        public static readonly Color SurfaceHover = Color.FromArgb(39, 44, 55);
        public static readonly Color Border = Color.FromArgb(45, 50, 62);
        public static readonly Color BorderSoft = Color.FromArgb(35, 39, 49);

        public static readonly Color Text = Color.FromArgb(231, 234, 242);
        public static readonly Color TextMuted = Color.FromArgb(143, 151, 169);
        public static readonly Color TextDim = Color.FromArgb(104, 112, 130);

        public static readonly Color Accent = Color.FromArgb(91, 140, 255);
        public static readonly Color AccentHover = Color.FromArgb(116, 160, 255);
        public static readonly Color AccentSoft = Color.FromArgb(34, 55, 105);
        public static readonly Color Green = Color.FromArgb(67, 211, 158);
        public static readonly Color Amber = Color.FromArgb(245, 183, 92);
        public static readonly Color Red = Color.FromArgb(255, 107, 107);
        public static readonly Color Violet = Color.FromArgb(178, 140, 255);
        public static readonly Color Cyan = Color.FromArgb(93, 211, 224);

        public static readonly Color EditorBackground = Color.FromArgb(20, 22, 28);
        public static readonly Color GutterBackground = Color.FromArgb(23, 26, 32);
        public static readonly Color Selection = Color.FromArgb(42, 69, 128);

        public static readonly Font UiFont = CreateFont("Segoe UI Variable Text", "Segoe UI", 9.75f);
        public static readonly Font UiFontMedium = CreateFont("Segoe UI Variable Text", "Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font SmallFont = CreateFont("Segoe UI Variable Text", "Segoe UI", 8.25f);
        public static readonly Font SmallBoldFont = CreateFont("Segoe UI Variable Text", "Segoe UI", 8.25f, FontStyle.Bold);
        public static readonly Font TitleFont = CreateFont("Segoe UI Variable Display", "Segoe UI", 14f, FontStyle.Bold);
        public static readonly Font BrandFont = CreateFont("Segoe UI Variable Display", "Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font MonoFont = CreateFont("Cascadia Mono", "Consolas", 10.5f);
        public static readonly Font MonoSmall = CreateFont("Cascadia Mono", "Consolas", 9f);
        public static readonly Font MonoBold = CreateFont("Cascadia Mono", "Consolas", 11f, FontStyle.Bold);
        public static readonly Font DisplayFont = CreateFont("Consolas", "Consolas", 18f, FontStyle.Bold);

        private static Font CreateFont(string preferred, string fallback, float size, FontStyle style = FontStyle.Regular)
        {
            string family = fallback;
            try
            {
                foreach (FontFamily candidate in FontFamily.Families)
                {
                    if (string.Equals(candidate.Name, preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        family = preferred;
                        break;
                    }
                }
            }
            catch
            {
                family = fallback;
            }

            return new Font(family, size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (var brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRoundedBorder(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (var pen = new Pen(color))
            {
                graphics.DrawPath(pen, path);
            }
        }

        public static Color Mix(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        public static Label Label(string text, Color color, Font font = null)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                Font = font ?? UiFont,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
        }

        public static Panel Card()
        {
            var panel = new Panel
            {
                BackColor = Surface,
                Padding = new Padding(16)
            };
            panel.Paint += (sender, args) =>
            {
                var p = (Panel)sender;
                using (var pen = new Pen(Border))
                {
                    args.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return panel;
        }

        /// <summary>
        /// Asks DWM for a dark title bar. Windows 10 builds 1809+ use
        /// attribute 20, earlier builds use the undocumented 19.
        /// </summary>
        public static void EnableDarkTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;

            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
                }
            }
            catch
            {
                // Dark title bars are cosmetic and unavailable on older Windows.
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
