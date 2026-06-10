// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking.DialogBoxes
{
    /// <summary>
    /// Brute-force security unlock dialog. Sweeps the known GM key algorithms first (optional),
    /// then tries numeric key values across a hex range, until the PCM accepts a key.
    /// </summary>
    public sealed class BruteForceDialogBox : Form
    {
        // Session-only persistence: remembered between opens while the app is running.
        private static string lastStart = "0000";
        private static string lastEnd = "FFFF";
        private static bool lastSweepFirst = true;
        // Remembered Speed selection: "Auto" or a whole-second value (boxed int).
        private static object lastSpeedSelection = AutoSpeedItem;

        // Dropdown text for the automatic speed (uses the built-in default); all other items are
        // whole-second values. Labelled "Speed" rather than "Delay" so a value like 2 reads as a
        // speed setting, not a 2-second timeout.
        private const string AutoSpeedItem = "Auto";

        private readonly Vehicle vehicle;
        private readonly ILogger logger;

        private readonly TextBox startBox;
        private readonly TextBox endBox;
        private readonly TextBox currentBox;
        private readonly CheckBox sweepCheckBox;
        private readonly Label delayLabel;
        private readonly ComboBox delayComboBox;
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private readonly Label etaLabel;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button exitButton;

        // Lockout countdown bar: full red at the start of a security-access wait, draining
        // downward as the expected delay elapses, so the user sees the ~10s pauses progress.
        private readonly Label timerLabel;
        private readonly Panel timerTrack;
        private readonly Panel timerFill;
        private readonly System.Windows.Forms.Timer countdownTimer;
        private DateTime countdownStartUtc;
        private double countdownTotalSeconds;

        private CancellationTokenSource? cancellationTokenSource;
        private bool isRunning;
        private bool closeRequested;

        // Tracks the most recent candidate reported, so Stop can resume the numeric search.
        private UInt16 lastKey;
        private BruteForcePhase lastPhase;

        public BruteForceDialogBox(Vehicle vehicle, ILogger logger)
        {
            this.vehicle = vehicle;
            this.logger = logger;

            this.Text = "Brute Force Unlock";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(400, 250);

            GroupBox rangeGroup = new GroupBox
            {
                Text = "Key Range (hex)",
                Location = new Point(12, 12),
                Size = new Size(336, 84)
            };

            Label startLabel = new Label { Text = "Start", Location = new Point(16, 26), AutoSize = true };
            this.startBox = MakeHexBox(new Point(16, 44));

            Label endLabel = new Label { Text = "End", Location = new Point(124, 26), AutoSize = true };
            this.endBox = MakeHexBox(new Point(124, 44));

            Label currentLabel = new Label { Text = "Current", Location = new Point(232, 26), AutoSize = true };
            this.currentBox = MakeHexBox(new Point(232, 44));
            this.currentBox.ReadOnly = true;
            this.currentBox.TabStop = false;

            rangeGroup.Controls.Add(startLabel);
            rangeGroup.Controls.Add(this.startBox);
            rangeGroup.Controls.Add(endLabel);
            rangeGroup.Controls.Add(this.endBox);
            rangeGroup.Controls.Add(currentLabel);
            rangeGroup.Controls.Add(this.currentBox);

            this.sweepCheckBox = new CheckBox
            {
                Text = "Algo sweep first",
                Location = new Point(16, 104),
                AutoSize = true,
                Checked = lastSweepFirst
            };

            // Speed: "Auto" (the default, suits every PCM) on top, then explicit whole-second values for
            // the rare case of hand-tuning a known PCM type. It paces the search, not a literal timeout,
            // so it is labelled "Speed".
            this.delayLabel = new Label
            {
                Text = "Speed:",
                Location = new Point(176, 105),
                AutoSize = true
            };
            this.delayComboBox = new ComboBox
            {
                Location = new Point(224, 101),
                Size = new Size(60, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            this.delayComboBox.Items.Add(AutoSpeedItem);
            for (int seconds = 1; seconds <= BruteForcer.MaxSecurityDelaySeconds; seconds++)
            {
                this.delayComboBox.Items.Add(seconds);
            }
            this.delayComboBox.SelectedItem = lastSpeedSelection;
            if (this.delayComboBox.SelectedIndex < 0)
            {
                this.delayComboBox.SelectedItem = AutoSpeedItem;
            }

            this.statusLabel = new Label
            {
                Text = "Ready.",
                Location = new Point(14, 132),
                Size = new Size(334, 20),
                AutoEllipsis = true
            };

            this.progressBar = new ProgressBar
            {
                Location = new Point(16, 154),
                Size = new Size(336, 18),
                Minimum = 0,
                Maximum = 1000
            };
            this.etaLabel = new Label
            {
                Text = string.Empty,
                Location = new Point(14, 178),
                Size = new Size(338, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };

            this.startButton = new Button { Text = "Start", Location = new Point(16, 210), Size = new Size(96, 28) };
            this.stopButton = new Button { Text = "Stop", Location = new Point(132, 210), Size = new Size(96, 28), Enabled = false };
            this.exitButton = new Button { Text = "Exit", Location = new Point(252, 210), Size = new Size(96, 28) };

            this.startButton.Click += this.StartButton_Click;
            this.stopButton.Click += this.StopButton_Click;
            this.exitButton.Click += this.ExitButton_Click;

            this.timerLabel = new Label
            {
                Text = "Timer",
                Location = new Point(352, 100),
                AutoSize = true
            };
            this.timerTrack = new Panel
            {
                Location = new Point(360, 120),
                Size = new Size(24, 86),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.ControlLightLight
            };
            this.timerFill = new Panel
            {
                BackColor = Color.Red,
                Location = new Point(0, 0),
                Size = new Size(0, 0)
            };
            this.timerTrack.Controls.Add(this.timerFill);

            this.countdownTimer = new System.Windows.Forms.Timer { Interval = 50 };
            this.countdownTimer.Tick += this.CountdownTimer_Tick;

            this.Controls.Add(rangeGroup);
            this.Controls.Add(this.sweepCheckBox);
            this.Controls.Add(this.delayLabel);
            this.Controls.Add(this.delayComboBox);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.etaLabel);
            this.Controls.Add(this.timerLabel);
            this.Controls.Add(this.timerTrack);
            this.Controls.Add(this.startButton);
            this.Controls.Add(this.stopButton);
            this.Controls.Add(this.exitButton);

            this.AcceptButton = this.startButton;

            this.startBox.Text = lastStart;
            this.endBox.Text = lastEnd;
        }

        private static TextBox MakeHexBox(Point location)
        {
            TextBox box = new TextBox
            {
                Location = location,
                Size = new Size(80, 23),
                MaxLength = 4,
                CharacterCasing = CharacterCasing.Upper,
                Font = new Font(FontFamily.GenericMonospace, 10f)
            };
            return box;
        }

        /// <summary>
        /// Parse a 1-4 digit hex string into a 16-bit value, or null if it is not valid hex.
        /// </summary>
        private static int? ParseHex(string text)
        {
            text = text.Trim();
            if (text.Length == 0 || text.Length > 4)
            {
                return null;
            }

            int value = 0;
            foreach (char c in text)
            {
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else return null;
                value = (value << 4) | digit;
            }
            return value & 0xFFFF;
        }

        private async void StartButton_Click(object? sender, EventArgs e)
        {
            if (this.isRunning)
            {
                return;
            }

            int? start = ParseHex(this.startBox.Text);
            int? end = ParseHex(this.endBox.Text);
            if (start == null)
            {
                MessageBox.Show(this, "Start must be 1-4 hex digits (0-9, A-F).", "Brute Force Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (end == null)
            {
                MessageBox.Show(this, "End must be 1-4 hex digits (0-9, A-F).", "Brute Force Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (end.Value < start.Value)
            {
                MessageBox.Show(this, "End must be greater than or equal to Start.", "Brute Force Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Normalise the boxes to 4-digit upper-case hex.
            this.startBox.Text = start.Value.ToString("X4");
            this.endBox.Text = end.Value.ToString("X4");

            bool sweepFirst = this.sweepCheckBox.Checked;
            // "Auto" uses the built-in default; a numeric selection is an explicit speed.
            object speedSelection = this.delayComboBox.SelectedItem ?? AutoSpeedItem;
            int delaySeconds = speedSelection is int s ? s : BruteForcer.DefaultSecurityDelaySeconds;

            // Remember for the next time the dialog opens this session.
            lastStart = this.startBox.Text;
            lastEnd = this.endBox.Text;
            lastSweepFirst = sweepFirst;
            lastSpeedSelection = speedSelection;

            this.lastKey = (UInt16)start.Value;
            this.lastPhase = sweepFirst ? BruteForcePhase.Sweeping : BruteForcePhase.Trying;

            this.SetRunningState(true);
            this.logger.AddUserMessage("Brute force: Start.");

            this.cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = this.cancellationTokenSource.Token;
            Progress<BruteForceProgress> progress = new Progress<BruteForceProgress>(this.OnProgress);
            BruteForcer bruteForcer = new BruteForcer(this.vehicle, this.logger, progress);

            try
            {
                using (new AwayMode())
                {
                    // Task.Run keeps the device I/O (and the awaits inside BruteForce) off the UI
                    // thread; the Progress<T> created above still marshals updates back to the UI.
                    BruteForceResult result = await Task.Run(
                        () => bruteForcer.BruteForce(start.Value, end.Value, sweepFirst, delaySeconds, token));
                    this.OnFinished(result);
                }
            }
            catch (Exception exception)
            {
                this.logger.AddUserMessage("Brute force failed: " + exception.Message);
                this.statusLabel.Text = "Error: " + exception.Message;
            }
            finally
            {
                this.cancellationTokenSource?.Dispose();
                this.cancellationTokenSource = null;
                this.SetRunningState(false);

                if (this.closeRequested)
                {
                    this.Close();
                }
            }
        }

        private void StopButton_Click(object? sender, EventArgs e)
        {
            if (!this.isRunning)
            {
                return;
            }

            this.logger.AddUserMessage("Brute force: Stop.");

            // If we are in the numeric phase, remember the current key so we can resume next time.
            if (this.lastPhase == BruteForcePhase.Trying)
            {
                this.startBox.Text = this.lastKey.ToString("X4");
                lastStart = this.startBox.Text;
            }

            this.stopButton.Enabled = false;
            this.statusLabel.Text = "Stopping...";
            this.cancellationTokenSource?.Cancel();
        }

        private void ExitButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void OnProgress(BruteForceProgress p)
        {
            this.lastKey = p.Key;
            this.lastPhase = p.Phase;

            this.currentBox.Text = p.Key.ToString("X4");
            this.progressBar.Value = Math.Max(0, Math.Min(1000, (int)(p.Fraction * 1000)));
            this.etaLabel.Text = string.IsNullOrEmpty(p.Eta) ? string.Empty : "Max wait: " + p.Eta;

            // One progress update per key: show the key under test and start a single countdown sized
            // to the expected time for that whole key. The brute forcer hides its internal seed/key
            // retries, so the bar simply re-arms each time we move to a new key.
            this.statusLabel.Text = (p.Phase == BruteForcePhase.Sweeping ? "Sweeping " : "Trying ") + p.Key.ToString("X4");
            if (p.WaitSeconds > 0)
            {
                this.StartCountdown(p.WaitSeconds);
            }
        }

        /// <summary>Begin the lockout countdown: fill the bar and animate it down over the wait.</summary>
        private void StartCountdown(double seconds)
        {
            this.countdownTotalSeconds = seconds;
            this.countdownStartUtc = DateTime.UtcNow;
            this.UpdateCountdownBar(1.0);
            this.countdownTimer.Start();
        }

        /// <summary>Stop the countdown and empty the bar.</summary>
        private void StopCountdown()
        {
            this.countdownTimer.Stop();
            this.UpdateCountdownBar(0.0);
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            double remaining = this.countdownTotalSeconds - (DateTime.UtcNow - this.countdownStartUtc).TotalSeconds;
            if (remaining <= 0 || this.countdownTotalSeconds <= 0)
            {
                this.StopCountdown();
                return;
            }

            this.UpdateCountdownBar(remaining / this.countdownTotalSeconds);
        }

        /// <summary>
        /// Set the red fill to <paramref name="fraction"/> (0..1) of the track height, anchored at the
        /// bottom so the top edge moves down as time runs out.
        /// </summary>
        private void UpdateCountdownBar(double fraction)
        {
            fraction = Math.Max(0.0, Math.Min(1.0, fraction));
            int trackHeight = this.timerTrack.ClientSize.Height;
            int trackWidth = this.timerTrack.ClientSize.Width;
            int fillHeight = (int)Math.Round(trackHeight * fraction);
            this.timerFill.SetBounds(0, trackHeight - fillHeight, trackWidth, fillHeight);
        }

        private void OnFinished(BruteForceResult result)
        {
            string status;
            switch (result.Outcome)
            {
                case BruteForceOutcome.Found:
                    status = result.Algorithm >= 0
                        ? $"Key found! {result.Key:X4} (match Algo {result.Algorithm})"
                        : $"Key found! {result.Key:X4}";
                    this.currentBox.Text = result.Key.ToString("X4");
                    this.progressBar.Value = this.progressBar.Maximum;
                    break;

                case BruteForceOutcome.Exhausted:
                    status = "Key not found";
                    break;

                case BruteForceOutcome.AlreadyUnlocked:
                    status = "The PCM is already unlocked.";
                    break;

                case BruteForceOutcome.UnlockNotRequired:
                    status = "No unlock required (seed 0x0000).";
                    break;

                case BruteForceOutcome.Canceled:
                    status = "Stopped.";
                    break;

                default:
                    status = "Stopped (communication error).";
                    break;
            }

            this.statusLabel.Text = status;
            this.etaLabel.Text = string.Empty;
            this.StopCountdown();
        }

        private void SetRunningState(bool running)
        {
            this.isRunning = running;
            this.startButton.Enabled = !running;
            this.stopButton.Enabled = running;
            this.startBox.Enabled = !running;
            this.endBox.Enabled = !running;
            this.sweepCheckBox.Enabled = !running;
            this.delayComboBox.Enabled = !running;
            // Exit stays enabled; closing while running cancels and waits (see OnFormClosing).
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.isRunning)
            {
                // Cancel the run and let it unwind; don't close until it has finished so the
                // device isn't torn out from under an in-flight operation. The run's finally
                // block re-issues Close() once it sees closeRequested.
                this.closeRequested = true;
                this.cancellationTokenSource?.Cancel();
                e.Cancel = true;
                this.statusLabel.Text = "Stopping...";
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.countdownTimer?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
