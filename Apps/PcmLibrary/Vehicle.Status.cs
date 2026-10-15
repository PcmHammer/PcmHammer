// SPDX-License-Identifier: GPL-3.0-only
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class Vehicle
    {
        /// <summary>
        /// Find out what the PCM is doing: recovery mode, a running kernel, or a normal operating
        /// system on whichever bus it answers on. Null when nothing answered.
        /// </summary>
        /// <remarks>
        /// This is the shared connection check. It follows the same order as the VPW flow that came
        /// before it - recovery, then kernel, then identify - with bus detection in place of the
        /// VPW-only operating system query, so a CAN PCM is found by the same code path. On success
        /// the device is left on the bus the PCM answered on, ready for normal operations.
        /// </remarks>
        public async Task<VehicleStatus?> QueryStatus(CancellationToken cancellationToken)
        {
            // Recovery and kernel are VPW-only states, so these two checks only run when the device
            // is on VPW. They are skipped once the PCM is known to be on CAN, because probing VPW
            // for states that cannot exist there costs a full timeout on every check.
            if (this.LastDetectedBus != BusProtocol.Can500k && await this.device.SetProtocol(BusProtocol.VPW))
            {
                this.SetTarget(Target.Pcm);

                byte? programmedState = await this.device.ReadBroadcastState(Mode.ReportProgrammedState);
                if (programmedState.HasValue)
                {
                    this.logger.AddUserMessage(
                        RecoveryMode.DescribeProgrammingRequest(new ProgrammingRequest(BusProtocol.VPW, programmedState.Value)));
                    return VehicleStatus.Recovery();
                }

                ulong kernelVersion = await this.GetKernelVersion(cancellationToken, maxRetries: 1);
                if (kernelVersion != 0)
                {
                    return VehicleStatus.Kernel(kernelVersion);
                }
            }

            DetectedModule? pcm = await this.DetectAndSelectPcm(cancellationToken);
            if (pcm == null)
            {
                return null;
            }

            // Voltage comes from a VPW PID, which has no GMLAN equivalent. Where that is unavailable -
            // on CAN, or when the PCM does not answer - fall back to the interface's own measurement,
            // which is bus-independent. Devices that cannot measure it still report nothing.
            string voltage = string.Empty;
            if (pcm.Bus == BusProtocol.VPW)
            {
                Response<string> response = await this.QueryVoltage();
                if (response.Status == ResponseStatus.Success)
                {
                    voltage = response.Value;
                }
            }

            if (string.IsNullOrEmpty(voltage))
            {
                Response<double> deviceVoltage = await this.device.ReadDeviceVoltage();
                if (deviceVoltage.Status == ResponseStatus.Success)
                {
                    voltage = deviceVoltage.Value.ToString("F1");
                }
            }

            if (string.IsNullOrEmpty(voltage))
            {
                voltage = VehicleStatus.VoltageUnavailable;
            }

            return VehicleStatus.OperatingSystem(pcm.Bus, pcm.Osid, voltage);
        }
    }
}
