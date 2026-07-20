// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Runtime.InteropServices;

namespace PcmHacking
{
    public class ConsoleLogger : ILogger
    {
        private readonly bool debug;
        private string? lastActivity = null;
        private string lastPercent = string.Empty;
        private string? currentVerb = null;
        private string? currentAddress = null;
        private int spinnerIndex = 0;
        private bool progressLineActive = false;

        private static readonly char[] Spinner = { '|', '/', '-', '\\' };

        // True when the terminal supports \r-based line overwriting.
        private static readonly bool interactiveConsole = DetectInteractiveConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr hFile);

        private const int StdOutputHandle = -11;
        private const uint FileTypePipe = 0x0003;

        private static bool DetectInteractiveConsole()
        {
            if (!Console.IsOutputRedirected)
            {
                try { return Console.WindowWidth > 0; }
                catch { return false; }
            }

            // MinTTY (Git Bash / Cygwin) wraps the process stdout in a pipe,
            // so IsOutputRedirected is true even though the terminal handles \r fine.
            // Distinguish it from a genuine file/pipe redirect by checking that
            // stdout is a pipe AND the TERM env var is set (MinTTY always sets it).
            // This is a Windows-only quirk; the GetStdHandle/GetFileType calls below
            // are kernel32, so skip them on other platforms (a redirected stream there
            // is a plain file/pipe and is correctly treated as non-interactive).
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                IntPtr handle = GetStdHandle(StdOutputHandle);
                if (GetFileType(handle) == FileTypePipe)
                {
                    string term = Environment.GetEnvironmentVariable("TERM");
                    return !string.IsNullOrEmpty(term);
                }
            }
            catch { }

            return false;
        }

        // Throttle for fallback (non-interactive) mode.
        private DateTime lastProgressTime = DateTime.MinValue;
        private string? lastFallbackPercent = null;

        public ConsoleLogger(bool debug = false)
        {
            this.debug = debug;
        }

        public void AddUserMessage(string message)
        {
            FinishProgressLine();
            Console.WriteLine(message);
        }

        public void AddDebugMessage(string message)
        {
            if (this.debug)
            {
                FinishProgressLine();
                Console.WriteLine("[DEBUG] " + message);
            }
        }

        public void StatusUpdateActivity(string activity)
        {
            if (activity == this.lastActivity) return;
            this.lastActivity = activity;

            if (string.IsNullOrEmpty(activity))
            {
                FinishProgressLine();
                return;
            }

            int hexIdx = activity.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (hexIdx >= 0)
            {
                this.currentVerb = activity.Split(' ')[0];
                string addr = activity.Substring(hexIdx);
                int end = addr.IndexOfAny(new[] { ' ', '\t' });
                if (end >= 0) addr = addr.Substring(0, end);
                this.currentAddress = addr;

                if (interactiveConsole)
                {
                    UpdateProgressLine();
                }
                else if ((DateTime.Now - this.lastProgressTime).TotalSeconds >= 10)
                {
                    Console.WriteLine(activity);
                    this.lastProgressTime = DateTime.Now;
                }
            }
            else
            {
                FinishProgressLine();
                Console.WriteLine(activity);
            }
        }

        public void StatusUpdatePercentDone(string percent)
        {
            if (string.IsNullOrEmpty(percent) || percent == this.lastPercent) return;
            this.lastPercent = percent;

            if (interactiveConsole)
            {
                if (this.currentAddress != null)
                    UpdateProgressLine();
            }
            else if (percent != this.lastFallbackPercent &&
                     (DateTime.Now - this.lastProgressTime).TotalSeconds >= 10)
            {
                Console.WriteLine("Progress: " + percent);
                this.lastFallbackPercent = percent;
                this.lastProgressTime = DateTime.Now;
            }
        }

        private void UpdateProgressLine()
        {
            string pct = string.IsNullOrEmpty(this.lastPercent) ? string.Empty : $" ({this.lastPercent})";
            char spin = Spinner[this.spinnerIndex % Spinner.Length];
            this.spinnerIndex++;
            string line = $"{this.currentVerb} {this.currentAddress}{pct} {spin}";
            int width = 0;
            try { width = Console.WindowWidth - 1; } catch { }
            string output = (width > 0 && line.Length < width) ? line.PadRight(width) : line;
            Console.Write('\r' + output);
            this.progressLineActive = true;
        }

        private void FinishProgressLine()
        {
            if (this.progressLineActive)
            {
                Console.WriteLine();
                this.progressLineActive = false;
            }
        }

        public void StatusUpdateTimeRemaining(string remaining) { }
        public void StatusUpdateRetryCount(string retries) { }
        public void StatusUpdateProgressBar(double completed, bool visible) { }
        public void StatusUpdateKbps(string Kbps) { }

        public void StatusUpdateReset()
        {
            FinishProgressLine();
            this.lastActivity = null;
            this.lastPercent = string.Empty;
            this.lastFallbackPercent = null;
            this.lastProgressTime = DateTime.MinValue;
            this.currentVerb = null;
            this.currentAddress = null;
            this.spinnerIndex = 0;
        }
    }
}
