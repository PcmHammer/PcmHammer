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
            if (this.LastDetectedBus != BusProtocol.Can500k && await this.device.SetProtocol(BusProtocol.Vpw))
            {
                this.SetTarget(Target.Pcm);

                byte? programmedState = await this.CheckForRecoveryMode(cancellationToken);
                if (programmedState.HasValue)
                {
                    this.logger.AddUserMessage(RecoveryMode.DescribeProgrammingRequest(programmedState));
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

            // Voltage comes from a VPW PID. There is no GMLAN equivalent implemented, so a CAN PCM
            // reports no voltage rather than a wrong one.
            string voltage = string.Empty;
            if (pcm.Bus == BusProtocol.Vpw)
            {
                Response<string> response = await this.QueryVoltage();
                if (response.Status == ResponseStatus.Success)
                {
                    voltage = response.Value;
                }
            }

            return VehicleStatus.OperatingSystem(pcm.Bus, pcm.Osid, voltage);
        }
    }
}
