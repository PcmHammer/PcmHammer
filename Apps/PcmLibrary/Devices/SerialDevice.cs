// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Base class for serial-port devices.
    /// </summary>
    public abstract class SerialDevice : Device
    {
        /// <summary>
        /// The serial port this device will use.
        /// </summary>
        protected IPort Port { get; private set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public SerialDevice(IPort port, ILogger logger) : base(logger)
        {
            this.Port = port;
        }

        /// <summary>
        /// Disposer.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.Port != null)
                {
                    this.Port.Dispose();
                    this.Port = null!;
                }
            }
        }

        /// <summary>
        /// Generate a descriptive string for this device and the port that it is using.
        /// </summary>
        public override string ToString()
        {
            // Port is null once this device has been disposed (e.g. switching interfaces), but the
            // UI may still call ToString() on the stale instance to refresh a label - so never
            // dereference a null port here.
            IPort port = this.Port;
            return this.GetDeviceType() + " on " + (port != null ? port.ToString() : "(closed)");
        }
    }
}
