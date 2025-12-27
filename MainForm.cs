using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

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

        // For periodic UI update
        private System.Windows.Forms.Timer uiTimer;

        public MainForm()
        {
            InitializeComponent();
            this.Text = "Mouse Recorder Prototype";
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

            statusLabel = new Label() { Left = 10, Top = 40, Width = 800, Text = "Idle" };
            topPanel.Controls.Add(statusLabel);

            // Play area
            playArea = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            this.Controls.Add(playArea);

            debugLabel = new Label() { Left = 4, Top = 4, Width = 420, Height = 22, BackColor = Color.FromArgb(160, Color.Black), ForeColor = Color.White, Text = "", AutoSize = false };
            debugLabel.Visible = true;
            playArea.Controls.Add(debugLabel);
            debugLabel.BringToFront();

            targetButton = new Button() { Visible = false, BackColor = Color.LightGreen };
            targetButton.Click += TargetButton_Click;
            playArea.Controls.Add(targetButton);

            // UI timer to update status
            uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            uiTimer.Tick += UiTimer_Tick;
            uiTimer.Start();
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (logger != null)
            {
                statusLabel.Text = sessionActive ? $"Session: {sessionId} | Queued: {logger.QueueSize}" : "Idle";
            }
            else
            {
                statusLabel.Text = "Idle";
            }
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
            logger = new MouseLogger(sessionId, sessionStartTicks, sessionStartUtc, filepath);

            // Register raw input; Passive mode attaches RIDEV_INPUTSINK=true
            bool isPassive = (modeComboBox.SelectedItem?.ToString() == "Passive");
            rawInput.RegisterRawInput(this.Handle, isPassive);
            rawInput.RawInputReceived += RawInput_RawInputReceived;

            // Start logger background writer
            logger.Start();

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

            var spawn = spawner!.Spawn(playArea, targetButton);
            targetButton.Visible = true;
            targetButton.BringToFront();
            debugLabel.Text = $"Trial {trialId}: spawn {spawn.Width}x{spawn.Height}, margin {spawn.Margin} at {targetButton.Location}";
            debugLabel.Visible = true;

            // log TRIAL_SPAWN event
            var elapsedUs = (Stopwatch.GetTimestamp() - sessionStartTicks) * 1_000_000L / Stopwatch.Frequency;
            string tsIso = (sessionStartUtc + TimeSpan.FromTicks(elapsedUs * 10)).ToString("o");
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
                X = PointToClient(Cursor.Position).X,
                Y = PointToClient(Cursor.Position).Y,
            };
            logger?.EnqueueEvent(evt);
            SpawnNext();
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
