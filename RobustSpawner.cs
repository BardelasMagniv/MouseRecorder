using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseRecorder
{
    internal class RobustSpawner : ISpawner
    {
        private readonly int _minSize;
        private readonly int _maxSize;
        private readonly int _minMargin;
        private readonly int _maxMargin;
        private readonly Random _rng = new Random();


        public RobustSpawner(int minSize, int maxSize, int minMargin, int maxMargin)
        {
            _minSize = Math.Max(4, minSize);
            _maxSize = Math.Max(_minSize, maxSize);
            _minMargin = Math.Max(0, minMargin);
            _maxMargin = Math.Max(_minMargin, maxMargin);
        }

        public SpawnResult Spawn(Panel playArea, Button target)
        {
            int w = _rng.Next(_minSize, _maxSize + 1);
            int h = w;
            int margin = _rng.Next(_minMargin, _maxMargin + 1);

            var client = playArea.ClientSize;
            int minVisibleSize = 16; // ensure visible target

            // Clamp margin to leave room for a visible target
            int maxMarginX = Math.Max(0, (client.Width - minVisibleSize) / 2);
            int maxMarginY = Math.Max(0, (client.Height - minVisibleSize) / 2);
            if (margin > maxMarginX || margin > maxMarginY)
            {
                margin = Math.Min(maxMarginX, maxMarginY);
            }

            int usableW = Math.Max(0, client.Width - 2 * margin);
            int usableH = Math.Max(0, client.Height - 2 * margin);

            if (usableW <= 0 || usableH <= 0)
            {
                int size = Math.Max(4, Math.Min(client.Width, client.Height));
                int cx = Math.Max(0, (client.Width - size) / 2);
                int cy = Math.Max(0, (client.Height - size) / 2);
                target.Size = new Size(size, size);
                target.Location = new Point(cx, cy);
                target.BringToFront();
                return new SpawnResult { Width = size, Height = size, Margin = margin };
            }

            int finalW = Math.Min(w, usableW);
            finalW = Math.Max(minVisibleSize, Math.Min(finalW, usableW));
            int finalH = Math.Min(h, usableH);
            finalH = Math.Max(minVisibleSize, Math.Min(finalH, usableH));

            int maxLeft = margin + Math.Max(0, usableW - finalW);
            int maxTop = margin + Math.Max(0, usableH - finalH);

            int x = margin;
            int y = margin;
            if (maxLeft > margin) x = _rng.Next(margin, maxLeft + 1);
            if (maxTop > margin) y = _rng.Next(margin, maxTop + 1);

            target.Size = new Size(finalW, finalH);
            target.Location = new Point(x, y);
            target.BringToFront();

            return new SpawnResult { Width = finalW, Height = finalH, Margin = margin };
        }
    }
}
