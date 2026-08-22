// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>Whether VPW monitoring can follow a read that switches to 4X.</summary>
    public enum VpwMonitorReadiness
    {
        Ready,

        /// <summary>Device can't do 4X at all; 1X monitoring still works, so warn and continue.</summary>
        NoFourXSupport,

        /// <summary>4X supported but switched off. The user can fix it, so refuse.</summary>
        FourXDisabled,
    }

    /// <summary>
    /// Passively sniffs a vehicle bus and reports each frame as a formatted line. Read-only: it never
    /// transmits, it only follows the VPW speed switch so it stays in sync with a 4X read in progress.
    /// </summary>
    public sealed class BusMonitor
    {
        // Drop back to 1X after this much VPW silence at 4X: the bus reverts to standard speed when it
        // goes idle and there is no "return to 1X" frame to watch for. Long enough to span the gaps
        // inside an active 4X read so we don't downshift mid-read.
        private static readonly TimeSpan FourXIdleRevert = TimeSpan.FromSeconds(5);

        // Broadcast mode that ends 4X by request: the factory tool sends e.g. "49 FE 10 06" to drop the
        // bus back to standard speed. Watched alongside the idle timeout so we follow it down immediately.
        private const byte ReturnToStandardSpeed = 0x06;

        private readonly Device device;
        private readonly ILogger logger;

        public BusMonitor(Device device, ILogger logger)
        {
            this.device = device;
            this.logger = logger;
        }

        /// <summary>
        /// True if this run will follow a switch to VPW 4X. Requires both device support and the 4X
        /// config to be enabled; the caller checks this before starting and warns/refuses as needed.
        /// </summary>
        public bool WillFollowFourX => this.device.Supports4X && this.device.Enable4xReadWrite;

        /// <summary>7E0 tool to PCM, 7E8 PCM to tool, 101 all-node request.</summary>
        public static readonly IReadOnlyCollection<uint> DefaultCanIds = new uint[] { 0x7E0, 0x7E8, 0x101 };

        public static readonly string DefaultCanFilter = FormatCanIds(DefaultCanIds);

        public static string FormatCanIds(IReadOnlyCollection<uint> ids)
        {
            return string.Join(" ", ids.Select(id => id.ToString("X3")));
        }

        /// <summary>
        /// Whitespace- or comma-separated hex ids. Junk tokens are skipped and an empty result means
        /// "accept every id", so a typo widens the view instead of showing nothing.
        /// </summary>
        public static IReadOnlyCollection<uint>? ParseCanIds(string? text)
        {
            List<uint> ids = new List<uint>();
            foreach (string token in (text ?? string.Empty).Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id))
                {
                    ids.Add(id);
                }
            }

            return ids.Count == 0 ? null : ids;
        }

        /// <summary>Shared by all front ends so they apply the same rule and wording.</summary>
        public static VpwMonitorReadiness CheckVpwReadiness(Vehicle vehicle, out string message)
        {
            if (!vehicle.Supports4X)
            {
                message = "This device can't switch to 4X, so a 4X read will not be captured.";
                return VpwMonitorReadiness.NoFourXSupport;
            }

            if (!vehicle.Enable4xReadWrite)
            {
                message =
                    "Bus monitoring needs 4X enabled so it can follow a 4X read." + Environment.NewLine +
                    Environment.NewLine +
                    "Enable 4X for this device, then reconnect.";
                return VpwMonitorReadiness.FourXDisabled;
            }

            message = string.Empty;
            return VpwMonitorReadiness.Ready;
        }

        /// <summary>
        /// Run the monitor until cancelled. Each accepted frame is handed to <paramref name="onLine"/>
        /// already formatted (timestamp + payload). <paramref name="canAcceptIds"/> filters CAN by id
        /// (null = accept all); it does not apply to VPW.
        /// </summary>
        public async Task RunAsync(BusProtocol protocol, IReadOnlyCollection<uint>? canAcceptIds, Action<string> onLine, CancellationToken token)
        {
            if (!await this.device.BeginMonitor(protocol))
            {
                onLine(Stamp() + "Could not start monitoring on " + protocol + ".");
                return;
            }

            try
            {
                await this.device.SetTimeout(TimeoutScenario.Detect);
                this.device.ClearMessageQueue();

                if (protocol == BusProtocol.Can500k)
                {
                    await this.RunCan(canAcceptIds, onLine, token);
                }
                else
                {
                    await this.RunVpw(onLine, token);
                }
            }
            finally
            {
                await this.device.EndMonitor();
            }
        }

        private async Task RunVpw(Action<string> onLine, CancellationToken token)
        {
            bool follow = this.WillFollowFourX;
            VpwSpeed speed = VpwSpeed.Standard;
            DateTime lastFrame = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                Message message = await this.device.ReceiveMessage();
                if (message == null)
                {
                    if (follow && speed == VpwSpeed.FourX && (DateTime.UtcNow - lastFrame) > FourXIdleRevert)
                    {
                        await this.SetMonitorSpeed(VpwSpeed.Standard);
                        speed = VpwSpeed.Standard;
                        onLine(Stamp() + "(bus idle - reverted to 1X)");
                    }
                    continue;
                }

                byte[] bytes = message.GetBytes();
                lastFrame = DateTime.UtcNow;
                onLine(Stamp() + bytes.ToHex());

                if (follow && speed == VpwSpeed.Standard && bytes.Length >= 4 && bytes[3] == Mode.HighSpeed)
                {
                    await this.SetMonitorSpeed(VpwSpeed.FourX);
                    speed = VpwSpeed.FourX;
                    onLine(Stamp() + "(switched to 4X)");
                }
                else if (follow && speed == VpwSpeed.FourX && bytes.Length >= 4 && bytes[1] == DeviceId.Broadcast && bytes[3] == ReturnToStandardSpeed)
                {
                    await this.SetMonitorSpeed(VpwSpeed.Standard);
                    speed = VpwSpeed.Standard;
                    onLine(Stamp() + "(returned to 1X by request)");
                }
            }
        }

        // Change VPW speed, then re-assert monitor filtering: a speed change reconnects/reconfigures the
        // device and restores its normal (narrow) acceptance filter, which would hide most traffic.
        private async Task SetMonitorSpeed(VpwSpeed speed)
        {
            await this.device.SetVpwSpeed(speed);
            await this.device.BeginMonitor(BusProtocol.Vpw);
        }

        private async Task RunCan(IReadOnlyCollection<uint>? canAcceptIds, Action<string> onLine, CancellationToken token)
        {
            if (!(this.device is IRawCanMonitor channel))
            {
                onLine(Stamp() + "This device cannot stream raw CAN frames.");
                return;
            }

            HashSet<uint>? accept = canAcceptIds == null ? null : new HashSet<uint>(canAcceptIds);

            while (!token.IsCancellationRequested)
            {
                (uint id, byte[] frame) = await channel.ReceiveCanFrame();
                if (frame.Length == 0)
                {
                    continue;
                }

                if (accept != null && !accept.Contains(id))
                {
                    continue;
                }

                onLine(Stamp() + id.ToString("X3") + "  " + frame.ToHex());
            }
        }

        private static string Stamp()
        {
            return "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]  ";
        }
    }
}
