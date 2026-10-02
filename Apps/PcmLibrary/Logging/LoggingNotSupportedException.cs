// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Logging cannot work in this configuration, so the caller must not retry. Unlike
    /// <see cref="LogStartFailedException"/>, which means "that attempt failed".
    /// </summary>
    public class LoggingNotSupportedException : Exception
    {
        public LoggingNotSupportedException(string message) : base(message)
        {
        }
    }
}
