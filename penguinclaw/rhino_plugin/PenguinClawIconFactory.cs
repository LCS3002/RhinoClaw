using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PenguinClaw
{
    internal static class PenguinClawIconFactory
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(System.IntPtr handle);

        public static Bitmap CreateBitmap(int size = 24)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float scale = size / 24.0f;

                using (var brush = new SolidBrush(Color.FromArgb(232, 42, 26)))
                {
                    g.FillEllipse(brush, 7 * scale, 8 * scale, 10 * scale, 8 * scale);

                    var leftClaw = new PointF[]
                    {
                        new PointF(1 * scale, 12 * scale), new PointF(4 * scale, 8 * scale),
                        new PointF(7 * scale, 10 * scale), new PointF(4 * scale, 16 * scale)
                    };
                    g.FillPolygon(brush, leftClaw);

                    var rightClaw = new PointF[]
                    {
                        new PointF(23 * scale, 12 * scale), new PointF(20 * scale, 8 * scale),
                        new PointF(17 * scale, 10 * scale), new PointF(20 * scale, 16 * scale)
                    };
                    g.FillPolygon(brush, rightClaw);

                    var topSpike = new PointF[]
                    {
                        new PointF(12 * scale, 8 * scale), new PointF(10 * scale, 4 * scale), new PointF(14 * scale, 4 * scale)
                    };
                    g.FillPolygon(brush, topSpike);
                }

                using (var blackBrush = new SolidBrush(Color.Black))
                {
                    g.FillEllipse(blackBrush, 9 * scale, 10 * scale, 2 * scale, 2 * scale);
                    g.FillEllipse(blackBrush, 13 * scale, 10 * scale, 2 * scale, 2 * scale);
                }

                using (var pen = new Pen(Color.FromArgb(232, 42, 26), System.Math.Max(1f, scale)))
                {
                    g.DrawLine(pen, 6 * scale, 14 * scale, 3 * scale, 18 * scale);
                    g.DrawLine(pen, 7 * scale, 15 * scale, 4 * scale, 19 * scale);
                    g.DrawLine(pen, 18 * scale, 14 * scale, 21 * scale, 18 * scale);
                    g.DrawLine(pen, 17 * scale, 15 * scale, 20 * scale, 19 * scale);
                }
            }

            return bmp;
        }

        public static Icon CreateIcon(int size = 32)
        {
            using (var bmp = CreateBitmap(size))
            {
                var handle = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}