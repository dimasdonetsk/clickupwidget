using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClickUpWidget.Mac
{
    /// <summary>Значки малюються кодом: синій квадратик з галочкою (як у Windows-версії).</summary>
    internal static class Icons
    {
        private static readonly Color Blue = Color.Parse("#7AA7FF");
        private static readonly Color Ink = Color.Parse("#0E0E11");
        private static readonly Color Green = Color.Parse("#6FD39A");

        /// <summary>
        /// Значок у рядку меню. На Mac — шаблонний (лише форма, колір підставляє система):
        /// контур квадратика з галочкою, коли йде таймер — крапка в куті.
        /// </summary>
        public static WindowIcon Tray(bool timerRunning)
        {
            bool template = OperatingSystem.IsMacOS();
            using RenderTargetBitmap bmp = Render(36, ctx =>
            {
                double s = 36 / 32.0;
                using (ctx.PushTransform(Matrix.CreateScale(s, s)))
                {
                    if (template)
                    {
                        var black = new SolidColorBrush(Colors.Black);
                        var pen = new Pen(black, 2.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                        ctx.DrawRectangle(null, pen, new Rect(4, 4, 24, 24), 6, 6);
                        ctx.DrawGeometry(null, pen, Check());
                        if (timerRunning) ctx.DrawEllipse(black, null, new Point(26, 26), 5.5, 5.5);
                    }
                    else
                        DrawColored(ctx, timerRunning);
                }
            });
            return ToIcon(bmp);
        }

        /// <summary>Значок програми будь-якого розміру (для AppIcon.icns на Mac).</summary>
        public static void SaveAppIcon(string path, int size)
        {
            using RenderTargetBitmap bmp = Render(size, ctx =>
            {
                double s = size / 32.0;
                using (ctx.PushTransform(Matrix.CreateScale(s, s)))
                    DrawColored(ctx, false, inset: true);
            });
            bmp.Save(path);
        }

        private static void DrawColored(DrawingContext ctx, bool timerRunning, bool inset = false)
        {
            // У значка програми на Mac — поля, як у системних (квадрат ~80% полотна).
            Rect box = inset ? new Rect(3.2, 3.2, 25.6, 25.6) : new Rect(2, 2, 28, 28);
            double r = inset ? 5.8 : 7;
            ctx.DrawRectangle(new SolidColorBrush(Blue), null, box, r, r);
            var pen = new Pen(new SolidColorBrush(Ink), 3.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            ctx.DrawGeometry(null, pen, Check());
            if (timerRunning)
            {
                ctx.DrawEllipse(new SolidColorBrush(Ink), null, new Point(25.5, 25.5), 6.5, 6.5);
                ctx.DrawEllipse(new SolidColorBrush(Green), null, new Point(25.5, 25.5), 4.5, 4.5);
            }
        }

        private static Geometry Check() => Geometry.Parse("M9,16.5 L14,21.5 L23,11.5");

        private static RenderTargetBitmap Render(int size, Action<DrawingContext> draw)
        {
            var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (DrawingContext ctx = bmp.CreateDrawingContext())
                draw(ctx);
            return bmp;
        }

        private static WindowIcon ToIcon(Bitmap bmp)
        {
            var ms = new MemoryStream();
            bmp.Save(ms);
            ms.Position = 0;
            return new WindowIcon(ms);
        }
    }
}
