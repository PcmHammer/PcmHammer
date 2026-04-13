using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking {
    public class ProgressUpdate {
        public int PayloadLength { get; set; }
        public int TotalLength { get; set; }
        public string? Address { get; set; }
        public string? TimeRemaining { get; set; }
        public double Rate { get; set; }
        public double Percentage { get; set; }
        public int RetryCount { get; set; }
    }
}
