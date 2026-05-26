using System;

namespace PcmHacking
{
    public class ConsoleLogger : ILogger
    {
        private readonly bool verbose;
        private string lastActivity = null;
        private string lastPercent = null;
        private DateTime lastProgressTime = DateTime.MinValue;

        public ConsoleLogger(bool verbose = false)
        {
            this.verbose = verbose;
        }

        public void AddUserMessage(string message)
        {
            Console.WriteLine(message);
        }

        public void AddDebugMessage(string message)
        {
            if (this.verbose)
            {
                Console.WriteLine("[DEBUG] " + message);
            }
        }

        public void StatusUpdateActivity(string activity)
        {
            if (activity == this.lastActivity) return;
            this.lastActivity = activity;
            if (string.IsNullOrEmpty(activity)) return;

            // Always show non-read activities (e.g. CRC checks); throttle the per-block read messages
            bool isBlockRead = activity.StartsWith("Reading ");
            if (!isBlockRead || (DateTime.Now - this.lastProgressTime).TotalSeconds >= 10)
            {
                Console.WriteLine(activity);
            }
        }

        public void StatusUpdatePercentDone(string percent)
        {
            if (!string.IsNullOrEmpty(percent) &&
                percent != this.lastPercent &&
                (DateTime.Now - this.lastProgressTime).TotalSeconds >= 10)
            {
                Console.WriteLine("Progress: " + percent);
                this.lastPercent = percent;
                this.lastProgressTime = DateTime.Now;
            }
        }

        public void StatusUpdateTimeRemaining(string remaining) { }
        public void StatusUpdateRetryCount(string retries) { }
        public void StatusUpdateProgressBar(double completed, bool visible) { }
        public void StatusUpdateKbps(string Kbps) { }

        public void StatusUpdateReset()
        {
            this.lastActivity = null;
            this.lastPercent = null;
            this.lastProgressTime = DateTime.MinValue;
        }
    }
}
