using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace MouseRecorder
{
    public class MainForm : Form
    {
        private ComboBox modeComboBox;
        private Button startStopButton;
        private Button openFolderButton;
        private Panel playArea;
        private Button targetButton;
        private Label debugLabel;
        private NumericUpDown minSizeBox;
        private NumericUpDown maxSizeBox;
        private NumericUpDown minMarginBox;
        private NumericUpDown maxMarginBox;
        private Label statusLabel;

        // Gameplay UI
        private Label scoreLabel;
        private Label timerLabel;
        private Label titleLabel;
        private Label avgReactionLabel;
        private int score = 0;

        private MouseLogger? logger;
        private RawInputManager rawInput;
        private ISpawner? spawner;
        private Guid sessionId;
        private long sessionStartTicks;
        private DateTimeOffset sessionStartUtc;
        private bool sessionActive;
        private int trialId;
        private Random rng = new Random();
        private string dataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MouseRecorder");
        private string? currentTmpPath;

        // Reaction tracking (UI-only)
        private readonly Dictionary<int,long> spawnTimesUs = new Dictionary<int,long>();
        private readonly List<double> reactionTimesMs = new List<double>();
        private readonly object reactionLock = new object();

        // Simple ripple visual effect
        private class Ripple { public int X; public int Y; public int StartTick; public int DurationMs; public int MaxRadius; }
        private List<Ripple> activeRipples = new List<Ripple>();
        private System.Windows.Forms.Timer animTimer;

        // Click-through overlay for top-layer visuals (ripples, aim reticle)
        private class ClickThroughOverlay : Control
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    // WS_EX_TRANSPARENT
                    cp.ExStyle |= 0x20;
                    return cp;
                }
            }

            public ClickThroughOverlay()
            {
                // Enable double-buffering, custom painting, and transparent back color support
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                UpdateStyles();
                this.BackColor = Color.Transparent;
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_NCHITTEST = 0x84;
                const int HTTRANSPARENT = -1;
                if (m.Msg == WM_NCHITTEST)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private ClickThroughOverlay? overlayPanel;
        private Point lastCursorPos = Point.Empty;
        private bool cursorInPlayArea = false;
        private int aimSize = 28;

        // Current target (rendered by overlay)
        private System.Drawing.Rectangle currentTargetRect = System.Drawing.Rectangle.Empty;
        private bool hasTarget = false;

        // Pulse animation for header (Avg RT & Title)
        private System.Windows.Forms.Timer pulseTimer;
        private double pulsePhase = 0.0;
        private float avgBaseFontSize = 36f;
        private float titleBaseFontSize = 24f;
        private Color avgPulseBase = Color.FromArgb(255, 230, 100);
        private Color avgPulseHighlight = Color.FromArgb(255, 255, 255);

        // For periodic UI update
        private System.Windows.Forms.Timer uiTimer;

        public MainForm()
        {
            InitializeComponent();
            this.Text = "Mouse Recorder Prototype";
            // Global key handling: allow 'S' to toggle Start/Stop
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
            rawInput = new RawInputManager();
            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (sessionActive) StopSession();
        }

        private void InitializeComponent()
        {
            this.Width = 1000;
            this.Height = 700;

            // Top panel for controls
            var topPanel = new Panel() { Dock = DockStyle.Top, Height = 80 };
            this.Controls.Add(topPanel);

            modeComboBox = new ComboBox() { Left = 10, Top = 10, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            modeComboBox.Items.AddRange(new[] { "Active", "Passive" });
            modeComboBox.SelectedIndex = 0;
            topPanel.Controls.Add(modeComboBox);

            startStopButton = new Button() { Left = 160, Top = 10, Width = 120, Text = "Start Session" };
            startStopButton.Click += StartStopButton_Click;
            topPanel.Controls.Add(startStopButton);

            openFolderButton = new Button() { Left = 700, Top = 10, Width = 140, Text = "Open Data Folder" };
            openFolderButton.Click += OpenFolderButton_Click;
            topPanel.Controls.Add(openFolderButton);

            var labelMinSize = new Label() { Left = 300, Top = 12, Width = 60, Text = "Size px" };
            topPanel.Controls.Add(labelMinSize);
            minSizeBox = new NumericUpDown() { Left = 360, Top = 10, Width = 60, Minimum = 8, Maximum = 500, Value = 24 };
            topPanel.Controls.Add(minSizeBox);
            maxSizeBox = new NumericUpDown() { Left = 430, Top = 10, Width = 60, Minimum = 8, Maximum = 1000, Value = 64 };
            topPanel.Controls.Add(maxSizeBox);

            var labelMargin = new Label() { Left = 500, Top = 12, Width = 60, Text = "Margin px" };
            topPanel.Controls.Add(labelMargin);
            minMarginBox = new NumericUpDown() { Left = 560, Top = 10, Width = 60, Minimum = 0, Maximum = 500, Value = 8 };
            topPanel.Controls.Add(minMarginBox);
            maxMarginBox = new NumericUpDown() { Left = 630, Top = 10, Width = 60, Minimum = 0, Maximum = 1000, Value = 120 };
            topPanel.Controls.Add(maxMarginBox);

            statusLabel = new Label() { Left = 10, Top = 40, Width = 480, Text = "Idle" };
            topPanel.Controls.Add(statusLabel);

            scoreLabel = new Label() { Left = 850, Top = 10, Width = 130, Height = 48, Text = "Score: 0", TextAlign = ContentAlignment.MiddleRight, Font = new Font(this.Font.FontFamily, 16f, FontStyle.Bold), ForeColor = Color.DarkGreen };
            topPanel.Controls.Add(scoreLabel);

            timerLabel = new Label() { Left = 850, Top = 62, Width = 130, Height = 44, Text = "Time: 00:00:00", TextAlign = ContentAlignment.MiddleRight, Font = new Font(this.Font.FontFamily, 12f, FontStyle.Bold) };
            topPanel.Controls.Add(timerLabel);

            // Bottom HUD panel (Avg RT and Title)
            var bottomPanel = new Panel() { Dock = DockStyle.Bottom, Height = 120, BackColor = Color.FromArgb(250, 250, 250) };
            var hudLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            hudLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
            hudLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));

            avgReactionLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Avg RT: N/A",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(this.Font.FontFamily, 36f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 230, 100),
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Title: Newbie",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(this.Font.FontFamily, 24f, FontStyle.Bold | FontStyle.Italic),
                BackColor = Color.FromArgb(255, 255, 255),
                ForeColor = Color.FromArgb(40, 40, 40)
            };
            hudLayout.Controls.Add(avgReactionLabel, 0, 0);
            hudLayout.Controls.Add(titleLabel, 0, 1);
            bottomPanel.Controls.Add(hudLayout);
            this.Controls.Add(bottomPanel);

            // Play area
            playArea = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
            this.Controls.Add(playArea);
            playArea.Paint += PlayArea_Paint;

            debugLabel = new Label() { Left = 4, Top = 4, Width = 420, Height = 22, BackColor = Color.FromArgb(160, Color.Black), ForeColor = Color.White, Text = "", AutoSize = false };
            debugLabel.Visible = true;
            playArea.Controls.Add(debugLabel);
            debugLabel.BringToFront();

            // Visual target (drawn as bullseye in Paint); keep as Button for accessibility/click handling
            targetButton = new Button() { Visible = false, BackColor = Color.Transparent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Text = "", Width = 40, Height = 40 };
            targetButton.FlatAppearance.BorderSize = 0;
            targetButton.Click += TargetButton_Click;
            targetButton.Paint += TargetButton_Paint;
            playArea.Controls.Add(targetButton);

            // Top-most click-through overlay for reticle and ripples
            overlayPanel = new ClickThroughOverlay { Dock = DockStyle.Fill };
            overlayPanel.Paint += Overlay_Paint;
            playArea.Controls.Add(overlayPanel);
            overlayPanel.BringToFront();

            // Ensure overlay is top-most and doesn't interfere with clicks
            overlayPanel.BringToFront();


            // Mouse tracking on playArea to render aim reticle (overlay is click-through)
            playArea.MouseMove += PlayArea_MouseMove;
            playArea.MouseLeave += PlayArea_MouseLeave;
            playArea.MouseEnter += PlayArea_MouseEnter;
            playArea.MouseUp += PlayArea_MouseUp;

            // UI timer to update status
            uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            uiTimer.Tick += UiTimer_Tick;
            uiTimer.Start();
            // Pulse animation for header
            pulseTimer = new System.Windows.Forms.Timer { Interval = 60 };
            pulseTimer.Tick += PulseTimer_Tick;
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (logger != null)
            {
                if (sessionActive) statusLabel.Text = $"Session: {sessionId} | Queued: {logger.QueueSize} (Press 'S' to stop)";
                else statusLabel.Text = "Idle (Press 'S' to start)";
            }
            else
            {
                statusLabel.Text = "Idle (Press 'S' to start)";
            }

            if (sessionActive)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - sessionStartTicks;
                double elapsedSec = (double)elapsedTicks / Stopwatch.Frequency;
                var elapsed = TimeSpan.FromSeconds(elapsedSec);
                timerLabel.Text = $"Time: {elapsed:hh\\:mm\\:ss}";
                titleLabel.Text = $"Title: {GetTitleForElapsed(elapsed)}";
            }

            lock (reactionLock)
            {
                if (reactionTimesMs.Count > 0)
                {
                    var avg = reactionTimesMs.Average();
                    avgReactionLabel.Text = $"Avg RT: {avg:0.0} ms";
                    // change base pulse color based on performance
                    if (avg < 200.0) avgPulseBase = Color.FromArgb(102, 204, 102); // green
                    else if (avg < 350.0) avgPulseBase = Color.FromArgb(255, 204, 51); // yellow/orange
                    else avgPulseBase = Color.FromArgb(255, 102, 102); // red
                }
                else
                {
                    avgPulseBase = Color.FromArgb(255, 230, 100);
                    avgReactionLabel.Text = "Avg RT: N/A";
                }
            }
            // ensure overlay redraw to refresh pulsing colors
            overlayPanel?.Invalidate();
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            try
            {
                // Only handle plain 'S' (no Ctrl/Alt/Shift)
                if (e == null) return;
                if (e.Modifiers != Keys.None) return;

                // Avoid intercepting when typing in text controls
                var active = this.ActiveControl;
                if (active is System.Windows.Forms.TextBoxBase) return;

                if (e.KeyCode == Keys.S)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    // Toggle Start/Stop
                    if (!sessionActive) StartSession(); else StopSession();
                    // Move focus briefly to the Start/Stop button so user sees state change
                    startStopButton.Focus();
                }
            }
            catch
            {
                // ignore key handling problems
            }
        }

        private void PlayArea_Paint(object? sender, PaintEventArgs e)
        {
            // Keep background / optional grid, but ripples/reticle are drawn on overlay to remain above targets
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // subtle grid or background highlight (optional)
            // nothing else here now
        }

        private void StartRipple(int x, int y)
        {
            var r = new Ripple { X = x, Y = y, StartTick = Environment.TickCount, DurationMs = 600, MaxRadius = Math.Min(playArea.ClientSize.Width, playArea.ClientSize.Height) / 4 };
            activeRipples.Add(r);
            if (animTimer == null)
            {
                animTimer = new System.Windows.Forms.Timer { Interval = 30 };
                animTimer.Tick += AnimTimer_Tick;
            }
            if (!animTimer.Enabled) animTimer.Start();
            // ensure overlay updates
            overlayPanel?.Invalidate();
        }

        private void Overlay_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw current target first (so reticle & ripples are above it)
            if (hasTarget && !currentTargetRect.IsEmpty)
            {
                var t = currentTargetRect;
                int cx = t.Left + t.Width / 2;
                int cy = t.Top + t.Height / 2;
                int size = Math.Min(t.Width, t.Height);
                int rings = 4;

                // Drop shadow
                using (var sh = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    e.Graphics.FillEllipse(sh, cx - size / 2 - 2, cy - size / 2 + 2, size + 4, size + 4);
                }

                for (int r = 0; r < rings; r++)
                {
                    int rad = (int)(size * (1.0 - r / (double)rings) / 2.0);
                    var rectR = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);
                    Color col = (r % 2 == 0) ? Color.FromArgb(220, 220, 40) : Color.White;
                    using (var b = new SolidBrush(col))
                    {
                        e.Graphics.FillEllipse(b, rectR);
                    }
                }
                // center dot
                int ctr = Math.Max(4, size / 10);
                using (var b2 = new SolidBrush(Color.FromArgb(220, 200, 40)))
                {
                    e.Graphics.FillEllipse(b2, cx - ctr / 2, cy - ctr / 2, ctr, ctr);
                }
                using (var pen = new Pen(Color.FromArgb(120, 20, 20, 20), 2))
                {
                    e.Graphics.DrawEllipse(pen, cx - size / 2 + 1, cy - size / 2 + 1, size - 2, size - 2);
                }
            }

            int now = Environment.TickCount;
            // Draw ripples
            for (int i = activeRipples.Count - 1; i >= 0; i--)
            {
                var r = activeRipples[i];
                int elapsed = now - r.StartTick;
                if (elapsed >= r.DurationMs)
                {
                    activeRipples.RemoveAt(i);
                    continue;
                }
                double progress = Math.Min(1.0, (double)elapsed / r.DurationMs);
                int radius = (int)(r.MaxRadius * progress);
                int alpha = (int)(255 * (1 - progress));
                if (alpha <= 0) continue;
                using (var pen = new Pen(Color.FromArgb(alpha, 30, 144, 255), 3))
                {
                    e.Graphics.DrawEllipse(pen, r.X - radius, r.Y - radius, radius * 2, radius * 2);
                }
            }

            // Draw aim reticle at lastCursorPos if inside play area
            if (cursorInPlayArea)
            {
                int cx = lastCursorPos.X;
                int cy = lastCursorPos.Y;
                int r0 = Math.Max(6, aimSize / 2);
                // subtle shadow
                using (var sh = new Pen(Color.FromArgb(140, 0, 0, 0), 4))
                {
                    e.Graphics.DrawEllipse(sh, cx - r0 - 1, cy - r0 - 1, (r0 + 1) * 2, (r0 + 1) * 2);
                }
                // outer ring
                using (var p = new Pen(Color.FromArgb(230, 255, 255, 255), 2))
                {
                    e.Graphics.DrawEllipse(p, cx - r0, cy - r0, r0 * 2, r0 * 2);
                }
                // crosshair lines
                using (var p2 = new Pen(Color.FromArgb(220, 30, 144, 255), 2))
                {
                    e.Graphics.DrawLine(p2, cx - r0 - 6, cy, cx - r0 + 6, cy);
                    e.Graphics.DrawLine(p2, cx + r0 - 6, cy, cx + r0 + 6, cy);
                    e.Graphics.DrawLine(p2, cx, cy - r0 - 6, cx, cy - r0 + 6);
                    e.Graphics.DrawLine(p2, cx, cy + r0 - 6, cx, cy + r0 + 6);
                }
                // center dot
                using (var b = new SolidBrush(Color.FromArgb(220, 255, 50, 50)))
                {
                    e.Graphics.FillEllipse(b, cx - 3, cy - 3, 6, 6);
                }
            }
        }

        private void PlayArea_MouseMove(object? sender, MouseEventArgs e)
        {
            lastCursorPos = e.Location;
            cursorInPlayArea = true;
            overlayPanel?.Invalidate();
        }

        private void PlayArea_MouseLeave(object? sender, EventArgs e)
        {
            cursorInPlayArea = false;
            Cursor.Show();
            overlayPanel?.Invalidate();
        }

        private void PlayArea_MouseEnter(object? sender, EventArgs e)
        {
            // hide OS cursor inside play area; overlay draws aiming reticle
            Cursor.Hide();
        }

        private void TargetButton_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var btn = (Button)sender;
            var rect = btn.ClientRectangle;
            int size = Math.Min(rect.Width, rect.Height);
            int cx = rect.Width / 2;
            int cy = rect.Height / 2;

            // ensure circular hit area matches visual
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(0, 0, size - 1, size - 1);
                btn.Region = new Region(path);
            }

            // Draw concentric rings (bullseye) - outer to inner
            int rings = 4;
            for (int r = 0; r < rings; r++)
            {
                int rad = (int)(size * (1.0 - r / (double)rings) / 2.0);
                var rectR = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);
                Color col = (r % 2 == 0) ? Color.FromArgb(220, 220, 40) : Color.White;
                using (var b = new SolidBrush(col))
                {
                    e.Graphics.FillEllipse(b, rectR);
                }
            }
            // center dot
            int ctr = Math.Max(4, size / 10);
            using (var b2 = new SolidBrush(Color.FromArgb(220, 200, 40)))
            {
                e.Graphics.FillEllipse(b2, cx - ctr / 2, cy - ctr / 2, ctr, ctr);
            }

            // optional subtle stroke
            using (var pen = new Pen(Color.FromArgb(120, 20, 20, 20), 2))
            {
                e.Graphics.DrawEllipse(pen, 1, 1, size - 3, size - 3);
            }
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            if (activeRipples.Count == 0) { animTimer?.Stop(); return; }
            bool any = false;
            int now = Environment.TickCount;
            for (int i = activeRipples.Count - 1; i >= 0; i--)
            {
                var r = activeRipples[i];
                if (now - r.StartTick >= r.DurationMs)
                {
                    activeRipples.RemoveAt(i);
                }
                else
                {
                    any = true;
                }
            }
            // invalidate overlay which draws ripples
            overlayPanel?.Invalidate();
            if (!any)
            {
                animTimer?.Stop();
            }
        }        private void PulseTimer_Tick(object? sender, EventArgs e)
        {
            pulsePhase += 0.12;
            if (pulsePhase > Math.PI * 2) pulsePhase -= Math.PI * 2;
            double sine = 0.5 + 0.5 * Math.Sin(pulsePhase);
            try
            {
                avgReactionLabel.BackColor = Blend(avgPulseBase, avgPulseHighlight, sine);
                float scale = 1.0f + (float)(0.03 * Math.Sin(pulsePhase + Math.PI / 4));
                titleLabel.Font = new Font(titleLabel.Font.FontFamily, Math.Max(10f, titleBaseFontSize * scale), FontStyle.Bold);
                float avgScale = 1.0f + (float)(0.02 * Math.Sin(pulsePhase));
                avgReactionLabel.Font = new Font(avgReactionLabel.Font.FontFamily, Math.Max(10f, avgBaseFontSize * avgScale), FontStyle.Bold);
            }
            catch
            {
                // ignore transient UI state
            }
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Min(1.0, Math.Max(0.0, t));
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(255, r, g, bl);
        }

        private string GetTitleForElapsed(TimeSpan elapsed)
        {
            var m = elapsed.TotalMinutes;
            if (m < 1.0) return "Newbie";
            if (m < 5.0) return "Apprentice";
            if (m < 10.0) return "Sharpshooter";
            if (m < 20.0) return "Trailblazer";
            if (m < 30.0) return "Speed Demon";
            if (m < 45.0) return "Master Mouse";
            if (m < 60.0) return "Legendary Clicker";
            return "Immortal Clicker";
        }

        private void StartStopButton_Click(object? sender, EventArgs e)
        {
            if (!sessionActive) StartSession();
            else StopSession();
        }

        private void OpenFolderButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!System.IO.Directory.Exists(dataFolder)) System.IO.Directory.CreateDirectory(dataFolder);
                Process.Start(new ProcessStartInfo { FileName = dataFolder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartSession()
        {
            // Create session metadata
            sessionId = Guid.NewGuid();
            sessionStartTicks = Stopwatch.GetTimestamp();
            sessionStartUtc = PreciseTime.GetSystemTimePreciseUtc();

            // Create logger
            System.IO.Directory.CreateDirectory(dataFolder);
            string recordingMode = (modeComboBox.SelectedItem?.ToString() ?? "Active").ToLowerInvariant();
            string filename = $"session_{recordingMode}_{DateTime.Now:yyyyMMdd_HHmmss}_{sessionId}.tmp";
            string filepath = System.IO.Path.Combine(dataFolder, filename);
            currentTmpPath = filepath;
            logger = new MouseLogger(sessionId, sessionStartTicks, sessionStartUtc, filepath);

            // Register raw input; Passive mode attaches RIDEV_INPUTSINK=true
            bool isPassive = (modeComboBox.SelectedItem?.ToString() == "Passive");
            rawInput.RegisterRawInput(this.Handle, isPassive);
            rawInput.RawInputReceived += RawInput_RawInputReceived;

            // Start logger background writer
            logger.Start();

            // Reset gameplay stats (UI-only)
            score = 0;
            spawnTimesUs.Clear();
            lock (reactionLock) reactionTimesMs.Clear();
            scoreLabel.Text = "Score: 0";
            avgReactionLabel.Text = "Avg RT: N/A";
            timerLabel.Text = "Time: 00:00:00";
            titleLabel.Text = "Title: Newbie";
            // Start header pulse animation
            pulseTimer?.Start();

            // Hide system cursor while inside play area — we draw our own aim reticle
            Cursor.Hide();

            // Spawn initial target
            spawner = new RobustSpawner((int)minSizeBox.Value, (int)maxSizeBox.Value, (int)minMarginBox.Value, (int)maxMarginBox.Value);
            trialId = 0;
            SpawnNext();

            sessionActive = true;
            startStopButton.Text = "Stop Session";
            // Write session start event to CSV
            logger.WriteSessionMetadata(GetSessionMetadata());
            logger.EnqueueEvent(new MouseEvent { EventType = "session_start", TsMonotonicUs = 0, TsUtcIso = sessionStartUtc.ToString("o"), SessionId = sessionId });
        }

        private SessionMetadata GetSessionMetadata()
        {
            var screenBounds = SystemInformation.VirtualScreen;
            var windowRect = this.Bounds;
            var g = CreateGraphics();
            var dpiX = g.DpiX;
            var dpiY = g.DpiY;
            g.Dispose();
            return new SessionMetadata
            {
                SessionId = sessionId,
                SessionStartUtc = sessionStartUtc,
                RecordingMode = modeComboBox.SelectedItem?.ToString() ?? "Active",
                IsPassive = (modeComboBox.SelectedItem?.ToString() == "Passive"),
                ScreenWidth = screenBounds.Width,
                ScreenHeight = screenBounds.Height,
                WindowLeft = windowRect.Left,
                WindowTop = windowRect.Top,
                WindowWidth = windowRect.Width,
                WindowHeight = windowRect.Height,
                DpiX = (int)dpiX,
                DpiY = (int)dpiY
            };
        }

        private void StopSession()
        {
            sessionActive = false;
            startStopButton.Text = "Start Session";

            rawInput.RawInputReceived -= RawInput_RawInputReceived;
            rawInput.UnregisterRawInput();

            // write final session end event
            var elapsedUs = (Stopwatch.GetTimestamp() - sessionStartTicks) * 1_000_000L / Stopwatch.Frequency;
            logger?.EnqueueEvent(new MouseEvent { EventType = "session_end", TsMonotonicUs = elapsedUs, TsUtcIso = (sessionStartUtc + TimeSpan.FromTicks(elapsedUs * 10)).ToString("o"), SessionId = sessionId });

            // Stop logger and close file (rename .tmp to .csv)
            logger?.StopAndClose();
            // Stop header pulse
            pulseTimer?.Stop();
            // restore system cursor
            Cursor.Show();

            // Hide target
            targetButton.Visible = false;
        }

        private void SpawnNext()
        {
            trialId++;
            // If play area is too small, hide target and show debug note
            if (playArea.ClientSize.Width < 20 || playArea.ClientSize.Height < 20)
            {
                targetButton.Visible = false;
                debugLabel.Text = "Play area too small to place target";
                debugLabel.Visible = true;
                return;
            }

            // Compute a safe client rectangle inside playArea that excludes overlapping siblings (e.g., HUD bars)
            var client = playArea.ClientSize;
            int topCrop = 0;
            int bottomCrop = client.Height;
            foreach (Control c in this.Controls)
            {
                if (c == playArea || !c.Visible) continue;
                // map sibling to playArea coordinates
                var siblingScreen = c.PointToScreen(Point.Empty);
                var siblingInPlay = playArea.PointToClient(siblingScreen);
                var rectInPlay = new System.Drawing.Rectangle(siblingInPlay, c.ClientSize);
                var inter = System.Drawing.Rectangle.Intersect(rectInPlay, new System.Drawing.Rectangle(0, 0, client.Width, client.Height));
                if (inter.IsEmpty) continue;
                // if sibling occupies top half -> crop top; if bottom half -> crop bottom
                if (rectInPlay.Bottom <= client.Height / 2)
                {
                    topCrop = Math.Max(topCrop, rectInPlay.Bottom);
                }
                else if (rectInPlay.Top >= client.Height / 2)
                {
                    bottomCrop = Math.Min(bottomCrop, rectInPlay.Top);
                }
                else
                {
                    // overlapping middle; be conservative and crop bottom
                    bottomCrop = Math.Min(bottomCrop, rectInPlay.Top);
                }
            }
            var safeRect = new System.Drawing.Rectangle(0, topCrop, client.Width, Math.Max(0, bottomCrop - topCrop));

            // Try spawning up to a few times, shrinking the safe rect if we detect overlap with siblings
            SpawnResult spawn = null!;
            var attempts = 0;
            var currentSafe = safeRect;
            while (attempts < 4)
            {
                spawn = spawner!.Spawn(playArea, targetButton, currentSafe);

                // verify we don't overlap visible siblings (in playArea coords)
                var tRect = new System.Drawing.Rectangle(targetButton.Location, targetButton.Size);
                bool overlaps = false;
                foreach (Control c in this.Controls)
                {
                    if (c == playArea || !c.Visible) continue;
                    var siblingScreen = c.PointToScreen(Point.Empty);
                    var siblingInPlay = playArea.PointToClient(siblingScreen);
                    var rectInPlay = new System.Drawing.Rectangle(siblingInPlay, c.ClientSize);
                    if (System.Drawing.Rectangle.Intersect(tRect, rectInPlay) != System.Drawing.Rectangle.Empty)
                    {
                        overlaps = true;
                        // shrink bottom if needed
                        if (rectInPlay.Top > currentSafe.Top)
                        {
                            currentSafe.Height = Math.Max(0, rectInPlay.Top - currentSafe.Top - 4);
                        }
                        else
                        {
                            // move top inward
                            var delta = Math.Min(rectInPlay.Bottom - currentSafe.Top + 4, currentSafe.Height - 8);
                            currentSafe.Y += delta; currentSafe.Height = Math.Max(0, currentSafe.Height - delta);
                        }
                        break;
                    }
                }
                if (!overlaps) break;
                attempts++;
            }

            // hide the underlying button; overlay draws the target instead
            targetButton.Visible = false;
            // set currentTargetRect (playArea coords)
            currentTargetRect = new System.Drawing.Rectangle(targetButton.Location, targetButton.Size);
            hasTarget = true;
            // ensure overlay remains on top and refresh the HUD
            overlayPanel?.BringToFront();
            overlayPanel?.Invalidate();

            debugLabel.Text = $"Trial {trialId}: spawn {spawn.Width}x{spawn.Height}, margin {spawn.Margin} at {targetButton.Location} (safeRect={currentSafe}, attempts={attempts})";
            debugLabel.Visible = true;

            // log TRIAL_SPAWN event
            var elapsedUs = (Stopwatch.GetTimestamp() - sessionStartTicks) * 1_000_000L / Stopwatch.Frequency;
            string tsIso = (sessionStartUtc + TimeSpan.FromTicks(elapsedUs * 10)).ToString("o");
            // record spawn time for reaction-time computation
            spawnTimesUs[trialId] = elapsedUs;
            var evt = new MouseEvent
            {
                EventType = "trial_spawn",
                TrialId = trialId,
                SessionId = sessionId,
                TsMonotonicUs = elapsedUs,
                TsUtcIso = tsIso,
                X = targetButton.Location.X,
                Y = targetButton.Location.Y,
                SpawnW = spawn.Width,
                SpawnH = spawn.Height,
                SpawnMargin = spawn.Margin
            };
            logger?.EnqueueEvent(evt);
        }

        private void TargetButton_Click(object? sender, EventArgs e)
        {
            // If target button was clicked (fallback), delegate to unified handler
            var pt = PointToClient(Cursor.Position);
            HandleTargetHit(pt);
        }

        private void PlayArea_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!sessionActive) return;
            if (e.Button != MouseButtons.Left) return;
            if (!hasTarget) return;

            if (IsPointInTarget(e.Location))
            {
                HandleTargetHit(e.Location);
            }
        }

        private void HandleTargetHit(Point clickPos)
        {
            // log hit event and spawn next
            var elapsedUs = (Stopwatch.GetTimestamp() - sessionStartTicks) * 1_000_000L / Stopwatch.Frequency;
            string tsIso = (sessionStartUtc + TimeSpan.FromTicks(elapsedUs * 10)).ToString("o");
            var evt = new MouseEvent
            {
                EventType = "trial_hit",
                TrialId = trialId,
                SessionId = sessionId,
                TsMonotonicUs = elapsedUs,
                TsUtcIso = tsIso,
                X = clickPos.X,
                Y = clickPos.Y,
            };
            logger?.EnqueueEvent(evt);

            // Gameplay stats (UI-only): reaction time and score
            long spawnUs;
            if (spawnTimesUs.TryGetValue(trialId, out spawnUs))
            {
                double reactionMs = (elapsedUs - spawnUs) / 1000.0;
                lock (reactionLock) { reactionTimesMs.Add(reactionMs); }
                // avoid storing spawn timestamps forever
                spawnTimesUs.Remove(trialId);
                avgReactionLabel.Text = reactionTimesMs.Count > 0 ? $"Avg RT: {reactionTimesMs.Average():0.0} ms" : "Avg RT: N/A";
            }

            // ensure overlay redraw so reticle and ripples sit above the new target
            overlayPanel?.Invalidate();

            score++;
            scoreLabel.Text = $"Score: {score}";

            // Visual effects: ripple and quick flash (overlay shows ripples)
            var center = new Point(currentTargetRect.Left + currentTargetRect.Width / 2, currentTargetRect.Top + currentTargetRect.Height / 2);
            StartRipple(center.X, center.Y);
            var flashTimer = new System.Windows.Forms.Timer { Interval = 160 };
            flashTimer.Tick += (s2, e2) => { overlayPanel?.Invalidate(); ((System.Windows.Forms.Timer)s2).Stop(); ((System.Windows.Forms.Timer)s2).Dispose(); };
            flashTimer.Start();

            // clear current target then spawn next
            hasTarget = false;
            currentTargetRect = System.Drawing.Rectangle.Empty;
            overlayPanel?.Invalidate();

            SpawnNext();
            overlayPanel?.Invalidate();
        }

        private bool IsPointInTarget(Point p)
        {
            if (!hasTarget) return false;
            var cx = currentTargetRect.Left + currentTargetRect.Width / 2;
            var cy = currentTargetRect.Top + currentTargetRect.Height / 2;
            var dx = p.X - cx;
            var dy = p.Y - cy;
            double dist2 = dx * dx + dy * dy;
            double r = currentTargetRect.Width / 2.0;
            return dist2 <= r * r;
        }
        private void RawInput_RawInputReceived(object? sender, RawMouseEventArgs e)
        {
            // Process raw mouse event into MouseEvent(s)
            if (!sessionActive) return;

            long tick = Stopwatch.GetTimestamp();
            long elapsedUs = (tick - sessionStartTicks) * 1_000_000L / Stopwatch.Frequency;
            var tsIso = (sessionStartUtc + TimeSpan.FromTicks(elapsedUs * 10)).ToString("o");

            // Get current cursor position (screen coordinates)
            var pos = Cursor.Position; // System.Drawing.Point
            int screenX = pos.X;
            int screenY = pos.Y;

            int screenW = SystemInformation.VirtualScreen.Width;
            int screenH = SystemInformation.VirtualScreen.Height;
            int windowLeft = this.Bounds.Left;
            int windowTop = this.Bounds.Top;
            int windowW = this.ClientSize.Width;
            int windowH = this.ClientSize.Height;
            string coordinateSpace = modeComboBox.SelectedItem?.ToString() == "Active" ? "window" : "virtual_screen";

            // compute normalized coords
            float normX = 0f, normY = 0f;
            if (coordinateSpace == "window")
            {
                var clientPt = this.PointToClient(new Point(screenX, screenY));
                normX = windowW > 0 ? (float)clientPt.X / windowW : 0f;
                normY = windowH > 0 ? (float)clientPt.Y / windowH : 0f;
            }
            else
            {
                var virt = SystemInformation.VirtualScreen;
                normX = virt.Width > 0 ? (float)(screenX - virt.Left) / virt.Width : 0f;
                normY = virt.Height > 0 ? (float)(screenY - virt.Top) / virt.Height : 0f;
            }

            var moveEvt = new MouseEvent
            {
                EventType = "move",
                TrialId = trialId,
                SessionId = sessionId,
                TsMonotonicUs = elapsedUs,
                TsUtcIso = tsIso,
                X = screenX,
                Y = screenY,
                NormX = normX,
                NormY = normY,
                RawDx = e.RawDx,
                RawDy = e.RawDy,
                ScreenW = screenW,
                ScreenH = screenH,
                WindowLeft = windowLeft,
                WindowTop = windowTop,
                WindowW = windowW,
                WindowH = windowH,
                CoordinateSpace = coordinateSpace,
                DeviceName = e.DeviceName
            };
            logger?.EnqueueEvent(moveEvt);

            // buttons
            if ((e.ButtonFlags & RawInputConstants.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                logger?.EnqueueEvent(new MouseEvent
                {
                    EventType = "down",
                    TrialId = trialId,
                    SessionId = sessionId,
                    TsMonotonicUs = elapsedUs,
                    TsUtcIso = tsIso,
                    X = screenX,
                    Y = screenY,
                    Button = "Left",
                    ButtonState = "down",
                    ScreenW = screenW,
                    ScreenH = screenH,
                    WindowLeft = windowLeft,
                    WindowTop = windowTop,
                    WindowW = windowW,
                    WindowH = windowH,
                    CoordinateSpace = coordinateSpace,
                    DeviceName = e.DeviceName
                });
            }
            if ((e.ButtonFlags & RawInputConstants.RI_MOUSE_LEFT_BUTTON_UP) != 0)
            {
                logger?.EnqueueEvent(new MouseEvent
                {
                    EventType = "up",
                    TrialId = trialId,
                    SessionId = sessionId,
                    TsMonotonicUs = elapsedUs,
                    TsUtcIso = tsIso,
                    X = screenX,
                    Y = screenY,
                    Button = "Left",
                    ButtonState = "up",
                    ScreenW = screenW,
                    ScreenH = screenH,
                    WindowLeft = windowLeft,
                    WindowTop = windowTop,
                    WindowW = windowW,
                    WindowH = windowH,
                    CoordinateSpace = coordinateSpace,
                    DeviceName = e.DeviceName
                });
            }
            // other buttons
            if ((e.ButtonFlags & RawInputConstants.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            {
                logger?.EnqueueEvent(new MouseEvent
                {
                    EventType = "down",
                    TrialId = trialId,
                    SessionId = sessionId,
                    TsMonotonicUs = elapsedUs,
                    TsUtcIso = tsIso,
                    X = screenX,
                    Y = screenY,
                    Button = "Right",
                    ButtonState = "down",
                    ScreenW = screenW,
                    ScreenH = screenH,
                    WindowLeft = windowLeft,
                    WindowTop = windowTop,
                    WindowW = windowW,
                    WindowH = windowH,
                    CoordinateSpace = coordinateSpace,
                    DeviceName = e.DeviceName
                });
            }
            if ((e.ButtonFlags & RawInputConstants.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            {
                logger?.EnqueueEvent(new MouseEvent
                {
                    EventType = "up",
                    TrialId = trialId,
                    SessionId = sessionId,
                    TsMonotonicUs = elapsedUs,
                    TsUtcIso = tsIso,
                    X = screenX,
                    Y = screenY,
                    Button = "Right",
                    ButtonState = "up",
                    ScreenW = screenW,
                    ScreenH = screenH,
                    WindowLeft = windowLeft,
                    WindowTop = windowTop,
                    WindowW = windowW,
                    WindowH = windowH,
                    CoordinateSpace = coordinateSpace,
                    DeviceName = e.DeviceName
                });
            }

            // wheel
            if ((e.ButtonFlags & RawInputConstants.RI_MOUSE_WHEEL) != 0)
            {
                logger?.EnqueueEvent(new MouseEvent
                {
                    EventType = "wheel",
                    TrialId = trialId,
                    SessionId = sessionId,
                    TsMonotonicUs = elapsedUs,
                    TsUtcIso = tsIso,
                    X = screenX,
                    Y = screenY,
                    WheelDelta = e.WheelDelta,
                    ScreenW = screenW,
                    ScreenH = screenH,
                    WindowLeft = windowLeft,
                    WindowTop = windowTop,
                    WindowW = windowW,
                    WindowH = windowH,
                    CoordinateSpace = coordinateSpace,
                    DeviceName = e.DeviceName
                });
            }
        }

        public async Task<bool> RunAutoTestAsync(bool passive)
        {
            try
            {
                modeComboBox.SelectedItem = passive ? "Passive" : "Active";
                await Task.Delay(200);

                // Start session
                StartSession();
                await Task.Delay(200);

                var client = playArea.ClientSize;
                var rand = new Random();

                for (int i = 0; i < 6; i++)
                {
                    int cx = Math.Clamp(rand.Next(8, Math.Max(16, client.Width - 8)), 4, Math.Max(4, client.Width - 4));
                    int cy = Math.Clamp(rand.Next(8, Math.Max(16, client.Height - 8)), 4, Math.Max(4, client.Height - 4));
                    var screenPt = this.PointToScreen(new Point(cx, cy));
                    Cursor.Position = screenPt;

                    RawInput_RawInputReceived(this, new RawMouseEventArgs { RawDx = rand.Next(-5, 6), RawDy = rand.Next(-5, 6), ButtonFlags = 0, DeviceName = "AUTO_TEST" });
                    await Task.Delay(80);
                }

                // simulate click down/up
                RawInput_RawInputReceived(this, new RawMouseEventArgs { RawDx = 0, RawDy = 0, ButtonFlags = RawInputConstants.RI_MOUSE_LEFT_BUTTON_DOWN, DeviceName = "AUTO_TEST" });
                await Task.Delay(20);
                RawInput_RawInputReceived(this, new RawMouseEventArgs { RawDx = 0, RawDy = 0, ButtonFlags = RawInputConstants.RI_MOUSE_LEFT_BUTTON_UP, DeviceName = "AUTO_TEST" });
                await Task.Delay(50);

                // simulate wheel
                RawInput_RawInputReceived(this, new RawMouseEventArgs { RawDx = 0, RawDy = 0, ButtonFlags = RawInputConstants.RI_MOUSE_WHEEL, WheelDelta = 120, DeviceName = "AUTO_TEST" });
                await Task.Delay(50);

                // click target (logs trial_hit)
                if (targetButton.Visible) targetButton.PerformClick();
                await Task.Delay(100);

                // stop session
                StopSession();

                // wait for writer to finalize
                await Task.Delay(800);

                if (string.IsNullOrEmpty(currentTmpPath))
                {
                    Console.WriteLine("AUTO-TEST: missing session path");
                    return false;
                }

                var finalPath = Path.ChangeExtension(currentTmpPath, ".csv");
                if (!File.Exists(finalPath))
                {
                    Console.WriteLine($"AUTO-TEST: final file not found: {finalPath}");
                    return false;
                }

                var lines = File.ReadAllLines(finalPath);
                bool hasMode = lines.Any(l => l.StartsWith("#RECORDING_MODE:"));
                bool hasPassive = lines.Any(l => l.StartsWith("#IS_PASSIVE:"));
                if (!hasMode || !hasPassive)
                {
                    Console.WriteLine("AUTO-TEST: missing header fields");
                    return false;
                }

                int idxEvents = Array.FindIndex(lines, l => l.Trim() == "#EVENTS");
                if (idxEvents < 0)
                {
                    Console.WriteLine("AUTO-TEST: missing #EVENTS");
                    return false;
                }

                var eventRows = lines.Skip(idxEvents + 2).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                if (eventRows.Length == 0)
                {
                    Console.WriteLine("AUTO-TEST: no event rows");
                    return false;
                }

                // validate normalized coords and monotonic timestamps
                long prevTs = -1;
                foreach (var row in eventRows)
                {
                    var cols = row.Split(',');
                    if (cols.Length > 8 && cols[1] == "move")
                    {
                        if (float.TryParse(cols[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nx) &&
                            float.TryParse(cols[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ny))
                        {
                            if (nx < 0f || nx > 1f || ny < 0f || ny > 1f)
                            {
                                Console.WriteLine($"AUTO-TEST: normalized coords out of range: {nx},{ny}");
                                return false;
                            }
                        }
                    }

                    if (cols.Length > 12 && long.TryParse(cols[12], out var ts))
                    {
                        if (prevTs > ts)
                        {
                            Console.WriteLine($"AUTO-TEST: non-monotonic ts: prev {prevTs} > {ts}");
                            return false;
                        }
                        prevTs = ts;
                    }
                }

                Console.WriteLine($"AUTO-TEST: PASS - file {finalPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AUTO-TEST: EXCEPTION: {ex}");
                return false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_INPUT = 0x00FF;
            if (m.Msg == WM_INPUT)
            {
                rawInput.HandleRawInput(m.LParam);
            }
            base.WndProc(ref m);
        }
    }
}