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

        public SpawnResult Spawn(Panel playArea, Button target, System.Drawing.Rectangle? safeClientRect = null)
        {
            var clientRect = safeClientRect ?? new System.Drawing.Rectangle(0, 0, playArea.ClientSize.Width, playArea.ClientSize.Height);

            int w = _rng.Next(_minSize, _maxSize + 1);
            int h = w;
            int margin = _rng.Next(_minMargin, _maxMargin + 1);

            int minVisibleSize = 16; // ensure visible target

            // Clamp margin to leave room for a visible target within clientRect
            int maxMarginX = Math.Max(0, (clientRect.Width - minVisibleSize) / 2);
            int maxMarginY = Math.Max(0, (clientRect.Height - minVisibleSize) / 2);
            if (margin > maxMarginX || margin > maxMarginY)
            {
                margin = Math.Min(maxMarginX, maxMarginY);
            }

            int usableW = Math.Max(0, clientRect.Width - 2 * margin);
            int usableH = Math.Max(0, clientRect.Height - 2 * margin);

            if (usableW <= 0 || usableH <= 0)
            {
                int size = Math.Max(4, Math.Min(clientRect.Width, clientRect.Height));
                int cx = clientRect.Left + Math.Max(0, (clientRect.Width - size) / 2);
                int cy = clientRect.Top + Math.Max(0, (clientRect.Height - size) / 2);
                target.Size = new Size(size, size);
                target.Location = new Point(cx, cy);
                // z-order is maintained by MainForm (overlay sits on top)
                return new SpawnResult { Width = size, Height = size, Margin = margin };
            }

            int finalW = Math.Min(w, usableW);
            finalW = Math.Max(minVisibleSize, Math.Min(finalW, usableW));
            int finalH = Math.Min(h, usableH);
            finalH = Math.Max(minVisibleSize, Math.Min(finalH, usableH));

            int maxLeft = clientRect.Left + margin + Math.Max(0, usableW - finalW);
            int maxTop = clientRect.Top + margin + Math.Max(0, usableH - finalH);

            int x = clientRect.Left + margin;
            int y = clientRect.Top + margin;
            if (maxLeft > clientRect.Left + margin) x = _rng.Next(clientRect.Left + margin, maxLeft + 1);
            if (maxTop > clientRect.Top + margin) y = _rng.Next(clientRect.Top + margin, maxTop + 1);

            target.Size = new Size(finalW, finalH);
            target.Location = new Point(x, y);
            // z-order is maintained by MainForm (overlay sits on top)

            return new SpawnResult { Width = finalW, Height = finalH, Margin = margin };
        }
    }
}