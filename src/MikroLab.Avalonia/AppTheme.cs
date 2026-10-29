using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MikroLab.Studio
{
    /// <summary>
    /// Central palette and typography for the cross-platform studio. The
    /// values mirror the WinForms theme so both builds look like one product.
    /// </summary>
    internal static class AppTheme
    {
        public static readonly Color Window = Color.FromRgb(17, 19, 24);
        public static readonly Color Surface = Color.FromRgb(24, 27, 34);
        public static readonly Color SurfaceRaised = Color.FromRgb(31, 35, 44);
        public static readonly Color SurfaceHover = Color.FromRgb(39, 44, 55);
        public static readonly Color Border = Color.FromRgb(45, 50, 62);
        public static readonly Color BorderSoft = Color.FromRgb(35, 39, 49);

        public static readonly Color Text = Color.FromRgb(231, 234, 242);
        public static readonly Color TextMuted = Color.FromRgb(143, 151, 169);
        public static readonly Color TextDim = Color.FromRgb(104, 112, 130);

        public static readonly Color Accent = Color.FromRgb(91, 140, 255);
        public static readonly Color AccentHover = Color.FromRgb(116, 160, 255);
        public static readonly Color AccentSoft = Color.FromRgb(34, 55, 105);
        public static readonly Color Green = Color.FromRgb(67, 211, 158);
        public static readonly Color Amber = Color.FromRgb(245, 183, 92);
        public static readonly Color Red = Color.FromRgb(255, 107, 107);
        public static readonly Color Violet = Color.FromRgb(178, 140, 255);
        public static readonly Color Cyan = Color.FromRgb(93, 211, 224);

        public static readonly Color EditorBackground = Color.FromRgb(20, 22, 28);
        public static readonly Color GutterBackground = Color.FromRgb(23, 26, 32);
        public static readonly Color Selection = Color.FromRgb(42, 69, 128);

        public static readonly FontFamily UiFamily = FontFamily.Parse(
            "Segoe UI Variable Text, Segoe UI, Inter, DejaVu Sans, Noto Sans, Liberation Sans, sans-serif");
        public static readonly FontFamily DisplayFamily = FontFamily.Parse(
            "Segoe UI Variable Display, Segoe UI, Inter, DejaVu Sans, Noto Sans, Liberation Sans, sans-serif");
        public static readonly FontFamily MonoFamily = FontFamily.Parse(
            "Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, Liberation Mono, Courier New, monospace");

        public const double UiSize = 13;
        public const double SmallSize = 11;
        public const double MonoSize = 14;
        public const double MonoSmallSize = 12;
        public const double MonoBoldSize = 14.5;
        public const double TitleSize = 19;
        public const double BrandSize = 21;
        public const double DisplaySize = 24;

        public static readonly Typeface UiFace = new Typeface(UiFamily, FontStyle.Normal, FontWeight.Normal);
        public static readonly Typeface UiMediumFace = new Typeface(UiFamily, FontStyle.Normal, FontWeight.SemiBold);
        public static readonly Typeface SmallFace = new Typeface(UiFamily, FontStyle.Normal, FontWeight.Normal);
        public static readonly Typeface SmallBoldFace = new Typeface(UiFamily, FontStyle.Normal, FontWeight.SemiBold);
        public static readonly Typeface TitleFace = new Typeface(DisplayFamily, FontStyle.Normal, FontWeight.Bold);
        public static readonly Typeface BrandFace = new Typeface(DisplayFamily, FontStyle.Normal, FontWeight.Bold);
        public static readonly Typeface MonoFace = new Typeface(MonoFamily, FontStyle.Normal, FontWeight.Normal);
        public static readonly Typeface MonoBoldFace = new Typeface(MonoFamily, FontStyle.Normal, FontWeight.Bold);
        public static readonly Typeface DisplayFace = new Typeface(MonoFamily, FontStyle.Normal, FontWeight.Bold);

        private static readonly Dictionary<Color, IBrush> BrushCache = new Dictionary<Color, IBrush>();

        public static IBrush Brush(Color color)
        {
            if (!BrushCache.TryGetValue(color, out IBrush brush))
            {
                brush = new SolidColorBrush(color);
                BrushCache[color] = brush;
            }
            return brush;
        }

        public static IPen Pen(Color color, double thickness = 1)
        {
            return new Pen(Brush(color), thickness);
        }

        public static Color Mix(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * amount),
                (byte)(a.G + (b.G - a.G) * amount),
                (byte)(a.B + (b.B - a.B) * amount));
        }

        public static FormattedText MakeText(
            string text,
            double size,
            IBrush brush,
            bool bold = false,
            bool mono = false,
            FontFamily family = null)
        {
            Typeface face = mono
                ? (bold ? MonoBoldFace : MonoFace)
                : (bold ? SmallBoldFace : new Typeface(family ?? UiFamily, FontStyle.Normal, FontWeight.Normal));
            return new FormattedText(
                text ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                face,
                size,
                brush);
        }

        public static FormattedText MakeText(string text, Typeface face, double size, IBrush brush)
        {
            return new FormattedText(
                text ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                face,
                size,
                brush);
        }

        public static void DrawText(DrawingContext context, FormattedText text, Point origin)
        {
            context.DrawText(text, origin);
        }

        public static void DrawText(
            DrawingContext context,
            string text,
            Point origin,
            double size,
            IBrush brush,
            bool bold = false,
            bool mono = false)
        {
            context.DrawText(MakeText(text, size, brush, bold, mono), origin);
        }

        public static void FillRounded(DrawingContext context, Rect bounds, double radius, IBrush brush)
        {
            context.DrawRectangle(brush, null, bounds, radius, radius);
        }

        public static void DrawRoundedBorder(DrawingContext context, Rect bounds, double radius, IPen pen)
        {
            context.DrawRectangle(null, pen, bounds, radius, radius);
        }

        public static TextBlock Label(string text, Color color, double size = UiSize, bool bold = false, bool mono = false)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush(color),
                FontFamily = mono ? MonoFamily : UiFamily,
                FontSize = size,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Border Card(Control child, Thickness? padding = null, Color? background = null)
        {
            return new Border
            {
                Background = Brush(background ?? Surface),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(0),
                Child = child
            };
        }

        public static Border Separator(bool vertical = true)
        {
            return new Border
            {
                Background = Brush(Border),
                Width = vertical ? 1 : double.NaN,
                Height = vertical ? double.NaN : 1,
                Margin = vertical ? new Thickness(8, 6) : new Thickness(0, 8)
            };
        }
    }
}
